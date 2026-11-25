using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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

        AvaloniaXamlLoader.Load(this);
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

    private T Find<T>(string name) where T : Control => this.FindControl<T>(name)!;

    private void PopulateStaticText(ArchiveOverwriteInfo.OverwritePreview preview)
    {
        var outputName = ArchiveOverwriteInfo.GetDisplayName(_destinationPath);
        var folder = Path.GetDirectoryName(_destinationPath) ?? _destinationPath;
        Find<TextBlock>("OutputNameText").Text = outputName;
        Find<TextBlock>("LocationText").Text = App.Text("Overwrite.Location", folder);

        var existingCard = Find<Border>("ExistingCard");
        var existingParts = new List<string>();
        SetOptionalText("ExistingModifiedText", preview.DestinationModified is { } m ? App.Text("Overwrite.Modified", FormatTime(m)) : null, existingParts);
        SetOptionalText("ExistingSizeText", preview.DestinationSize is { } s ? App.Text("Overwrite.Size", DiskSpaceChecker.FormatSize(s)) : null, existingParts);
        AutomationProperties.SetName(existingCard, string.Join(" ", [App.Text("Overwrite.Existing"), outputName, .. existingParts]));

        var (names, more) = ArchiveOverwriteInfo.SummarizeNames(_sourcePaths);
        var namesPanel = Find<StackPanel>("NewNamesPanel");
        foreach (var name in names)
            namesPanel.Children.Add(NewNameBlock(name, FontWeight.SemiBold));
        if (more > 0)
            namesPanel.Children.Add(NewNameBlock(App.Text("Overwrite.SourceMore", more), FontWeight.Normal));
        ToolTip.SetTip(namesPanel, string.Join(Environment.NewLine, _sourcePaths));

        var newParts = new List<string>(names);
        SetOptionalText("NewModifiedText", preview.SourceModified is { } nm ? App.Text("Overwrite.Modified", FormatTime(nm)) : null, newParts);
        Find<TextBlock>("NewTotalText").Text = App.Text("Overwrite.Calculating");
        AutomationProperties.SetName(Find<Border>("NewCard"), string.Join(" ", [App.Text("Overwrite.New"), .. newParts]));
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
    private void SetOptionalText(string controlName, string? text, List<string> automationParts)
    {
        var block = Find<TextBlock>(controlName);
        block.Text = text;
        block.IsVisible = text != null;
        if (text != null) automationParts.Add(text);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Find<Button>("CancelButton").Focus();
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
            Find<Image>("ExistingIcon").Source = existing;
            Find<Image>("NewIcon").Source = source;
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
            Find<TextBlock>("NewTotalText").Text = total is { } t
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
