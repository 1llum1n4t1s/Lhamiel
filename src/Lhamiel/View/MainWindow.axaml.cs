using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Lhamiel.Util;
using Lhamiel.ViewModels;
namespace Lhamiel.View;

/// <summary>
/// MainWindow.xaml の相互作用ロジック（View のみ。ビジネスロジックは MainWindowViewModel）
/// </summary>
public partial class MainWindow : Window
{
    private Border? _dropOverlay;
    private Border? _accentOverlay;
    private bool _isProcessingDrop;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _dropOverlay = this.FindControl<Border>("DropOverlay");
        _accentOverlay = this.FindControl<Border>("AccentOverlay");
        ApplyAccentOverlay();
        InitDebugTapArea();
    }

    /// <summary>
    /// OSのアクセントカラーを取得してオーバーレイに適用
    /// </summary>
    private void ApplyAccentOverlay()
    {
        if (_accentOverlay is null) return;
        try
        {
            var colors = Application.Current?.PlatformSettings?.GetColorValues();
            if (colors is { } c)
            {
                _accentOverlay.Background = new SolidColorBrush(c.AccentColor1);
            }
        }
        catch
        {
            // アクセントカラー取得に失敗した場合はオーバーレイなし
        }
    }

    /// <summary>
    /// MainWindow のコンストラクタ
    /// </summary>
    public MainWindow()
    {
        try
        {
            InitializeComponent();
            var pickExtractionFolder = () => PickFolderAsync(App.Text("Settings.Output.BrowseExtraction"));
            var pickCompressionFolder = () => PickFolderAsync(App.Text("Settings.Output.BrowseCompression"));
            void ShowProgressWindow(ProgressWindow w)
            {
                w.Show();
                w.Activate();
            }
            var viewModel = new MainWindowViewModel(pickExtractionFolder, pickCompressionFolder, ShowProgressWindow);
            DataContext = viewModel;
            InitDirModeRadioButtons(viewModel.SelectedDirectoryStructureMode);
            InitPasswordModeRadioButtons(viewModel.PasswordMode);
            // VM が wipe キャンセル時に PasswordMode を rollback したら radio button もここで戻す
            // (codex P2 #3381085184。radio button は two-way binding 無しの手動制御なので)。
            viewModel.PasswordModeRadioSyncCallback = mode =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => InitPasswordModeRadioButtons(mode));
        }
        catch (Exception ex)
        {
            _ = MessageService.ShowException(App.Text("Error.InitApp"), ex);
            throw;
        }
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return null;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        return folders.Count > 0 && folders[0].TryGetLocalPath() is { } path ? path : null;
    }

    /// <summary>
    /// ドラッグオーバー時：オーバーレイを表示
    /// </summary>
    private void DropZone_DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
            if (_dropOverlay != null)
                _dropOverlay.IsVisible = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    /// <summary>
    /// ドラッグリーブ時：オーバーレイを非表示
    /// </summary>
    private void DropZone_DragLeave(object? sender, DragEventArgs e)
    {
        if (_dropOverlay != null)
            _dropOverlay.IsVisible = false;
    }

    /// <summary>
    /// ドロップ時：オーバーレイを非表示にしてパスを ViewModel に渡す
    /// </summary>
    private async void DropZone_Drop(object? sender, DragEventArgs e)
    {
        if (_dropOverlay != null)
            _dropOverlay.IsVisible = false;

        if (_isProcessingDrop)
            return;

        if (!e.DataTransfer.Contains(DataFormat.File) || e.DataTransfer.TryGetFiles() is not { } files)
            return;
        var filePaths = new List<string>();
        foreach (var file in files)
        {
            if (file.TryGetLocalPath() is { } path)
                filePaths.Add(path);
        }
        if (filePaths.Count > 0 && DataContext is MainWindowViewModel vm)
        {
            _isProcessingDrop = true;
            try
            {
                await vm.ProcessDroppedPathsAsync(filePaths);
            }
            finally
            {
                _isProcessingDrop = false;
            }
        }
    }

    /// <summary>
    /// ディレクトリ構造モードのラジオボタンの初期状態をセット
    /// </summary>
    private void InitDirModeRadioButtons(int mode)
    {
        var radio = mode switch
        {
            0 => this.FindControl<RadioButton>("DirModeIncludeRoot"),
            1 => this.FindControl<RadioButton>("DirModeExcludeRoot"),
            2 => this.FindControl<RadioButton>("DirModeFlat"),
            _ => this.FindControl<RadioButton>("DirModeIncludeRoot")
        };
        if (radio != null) radio.IsChecked = true;
    }

    /// <summary>
    /// ディレクトリ構造モードのラジオボタン変更時
    /// </summary>
    private void DirModeRadio_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true, Tag: string tag } && DataContext is MainWindowViewModel vm)
        {
            if (int.TryParse(tag, out var mode))
                vm.SelectedDirectoryStructureMode = mode;
        }
    }

    /// <summary>
    /// パスワード入力モードのラジオボタンの初期状態をセット (Remember or PromptEachTime)。
    /// </summary>
    private void InitPasswordModeRadioButtons(string mode)
    {
        var radioName = string.Equals(mode, "Remember", System.StringComparison.Ordinal)
            ? "RememberPasswordRadio" : "PromptEachTimeRadio";
        var radio = this.FindControl<RadioButton>(radioName);
        if (radio != null) radio.IsChecked = true;
    }

    /// <summary>
    /// パスワード入力モードのラジオボタン変更時。VM の PasswordMode を Tag 文字列で更新する。
    /// </summary>
    private void PasswordModeRadio_Changed(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true, Tag: string tag } && DataContext is MainWindowViewModel vm)
        {
            if (tag is "PromptEachTime" or "Remember")
                vm.PasswordMode = tag;
        }
    }

    /// <summary>
    /// デバッグ用: 右下隅のトリプルクリックで CRDebugger を起動するエリアを初期化する。
    /// DEBUG ビルドでのみ有効。
    /// </summary>
    private void InitDebugTapArea()
    {
#if DEBUG
        var tapArea = this.FindControl<Border>("DebugTapArea");
        if (tapArea == null) return;
        tapArea.IsVisible = true;

        var clickCount = 0;
        var lastClickTime = DateTime.MinValue;
        const int tripleClickThresholdMs = 500;

        tapArea.PointerPressed += (_, e) =>
        {
            var now = DateTime.Now;
            if ((now - lastClickTime).TotalMilliseconds > tripleClickThresholdMs)
                clickCount = 0;

            clickCount++;
            lastClickTime = now;

            if (clickCount >= 3)
            {
                clickCount = 0;
                Util.DebugHelper.Toggle();
            }
        };
#endif
    }
}
