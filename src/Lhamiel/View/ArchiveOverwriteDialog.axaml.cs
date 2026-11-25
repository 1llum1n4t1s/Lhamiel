using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Lhamiel.Util;

namespace Lhamiel.View;

/// <summary>
/// 圧縮の出力先アーカイブが既に存在するときの上書き確認ダイアログ。
/// 既存のファイルと、新しく圧縮する内容（合計は背景で算出）を並べて示す。
/// 戻り値は上書きのときだけ true。Esc・×・キャンセルはすべて false。
/// </summary>
public partial class ArchiveOverwriteDialog : Window
{
    private readonly IReadOnlyList<string> _sourcePaths;
    private readonly string _destinationPath;
    // ダイアログを閉じたら合計の列挙とアイコン取得を止める。
    private readonly CancellationTokenSource _cts = new();

    public ArchiveOverwriteDialog() : this([], "", default) { }

    /// <param name="sourcePaths">圧縮する入力。</param>
    /// <param name="destinationPath">既存の出力アーカイブ。</param>
    /// <param name="preview">呼び出し側が UI スレッド外で取得済みの更新日時・サイズ。</param>
    internal ArchiveOverwriteDialog(IReadOnlyList<string> sourcePaths, string destinationPath,
        ArchiveOverwriteInfo.OverwritePreview preview)
    {
        _sourcePaths = sourcePaths;
        _destinationPath = destinationPath;

        InitializeComponent();
        // アクリル代替背景 → 共通枠 → アイコンの順で初期化する（順序は DialogChrome.InitializeDialog が固定）。
        DialogChrome.InitializeDialog(this);

        PopulateStaticText(preview);

        Opened += OnOpened;
        Closed += (_, _) =>
        {
            _cts.Cancel();
            _cts.Dispose();
        };
    }

    private void PopulateStaticText(ArchiveOverwriteInfo.OverwritePreview preview)
    {
        var outputName = ArchiveOverwriteInfo.GetDisplayName(_destinationPath);
        var folder = Path.GetDirectoryName(_destinationPath) ?? _destinationPath;
        OutputNameText.Text = outputName;
        LocationText.Text = App.Text("Overwrite.Location", folder);

        var existingParts = new List<string>();
        SetOptionalText(ExistingModifiedText, preview.DestinationModified is { } m ? App.Text("Overwrite.Modified", FormatTime(m)) : null, existingParts);
        SetOptionalText(ExistingSizeText, preview.DestinationSize is { } s ? App.Text("Overwrite.Size", DiskSpaceChecker.FormatSize(s)) : null, existingParts);
        AutomationProperties.SetName(ExistingCard, string.Join(" ", [App.Text("Overwrite.Existing"), outputName, .. existingParts]));

        var (names, more) = ArchiveOverwriteInfo.SummarizeNames(_sourcePaths);
        foreach (var name in names)
            NewNamesPanel.Children.Add(NewNameBlock(name, FontWeight.SemiBold));
        if (more > 0)
            NewNamesPanel.Children.Add(NewNameBlock(App.Text("Overwrite.SourceMore", more), FontWeight.Normal));
        ToolTip.SetTip(NewNamesPanel, string.Join(Environment.NewLine, _sourcePaths));

        var newParts = new List<string>(names);
        SetOptionalText(NewModifiedText, preview.SourceModified is { } nm ? App.Text("Overwrite.Modified", FormatTime(nm)) : null, newParts);
        NewTotalText.Text = App.Text("Overwrite.Calculating");
        AutomationProperties.SetName(NewCard, string.Join(" ", [App.Text("Overwrite.New"), .. newParts]));
    }

    private static TextBlock NewNameBlock(string text, FontWeight weight) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = weight,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static string FormatTime(DateTime time) => time.ToString("yyyy/MM/dd HH:mm");

    /// <summary>値があるときだけ表示し、無いときは枠ごと隠す（偽値は出さない）。</summary>
    private static void SetOptionalText(TextBlock block, string? text, List<string> automationParts)
    {
        block.Text = text;
        block.IsVisible = text != null;
        if (text != null) automationParts.Add(text);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        CancelButton.Focus();
        var token = _cts.Token;
        _ = LoadIconsAsync(token);
        _ = CalculateTotalAsync(token);
    }

    private async Task LoadIconsAsync(CancellationToken token)
    {
        var dest = _destinationPath;
        var first = _sourcePaths.Count > 0 ? _sourcePaths[0] : "";
        var (existing, source) = await Task.Run(() => (LoadIcon(dest), LoadIcon(first)), token).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (token.IsCancellationRequested) return;
            ExistingIcon.Source = existing;
            NewIcon.Source = source;
        });
    }

    /// <summary>実体のアイコンを優先し、取れなければ拡張子ベースのアイコンへフォールバックする（zip などでも必ず出す）。</summary>
    private static Bitmap? LoadIcon(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            return FileIconHelper.GetFileIcon(path) ?? FileIconHelper.GetFileIcon(ArchiveOverwriteInfo.GetDisplayName(path));
        }
        catch
        {
            return null;
        }
    }

    private async Task CalculateTotalAsync(CancellationToken token)
    {
        (long Bytes, int Files)? total = null;
        try
        {
            var paths = _sourcePaths;
            total = await Task.Run(() => ArchiveOverwriteInfo.ScanTotal(paths, token), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Logger.Log($"上書き確認の合計計算に失敗: {ex.GetType().Name}");
        }

        // リソース参照（App.Text）は UI スレッドで行う。
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (token.IsCancellationRequested) return;
            NewTotalText.Text = total is { } t
                ? App.Text("Overwrite.SourceTotal", DiskSpaceChecker.FormatSize(t.Bytes), t.Files)
                : App.Text("Overwrite.CalculationFailed");
        });
    }

    private void OverwriteButton_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);

    /// <summary>
    /// 上書き確認をバックグラウンドスレッドから表示する。
    /// 親ウィンドウなし・出力先が存在しないときは確認せず true。
    /// </summary>
    public static async Task<bool> CanOverwriteFromBackgroundAsync(
        IReadOnlyList<string> sourcePaths, string destinationPath, Window? parentWindow)
    {
        if (parentWindow == null) return true;

        // 存在確認と日時・サイズの取得は UI スレッドの外で行う（呼出しが UI スレッドでも固まらない）。
        var (exists, preview) = await Task.Run(() =>
            (File.Exists(destinationPath) || Directory.Exists(destinationPath),
             ArchiveOverwriteInfo.Gather(sourcePaths, destinationPath))).ConfigureAwait(false);
        if (!exists) return true;

        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new ArchiveOverwriteDialog(sourcePaths, destinationPath, preview);
            return await dialog.ShowDialog<bool?>(parentWindow) ?? false;
        });
    }
}
