using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Lhamiel.Util;
using Lhamiel.ViewModels;
namespace Lhamiel.View;

/// <summary>
/// MainWindow.xaml の相互作用ロジック（View のみ。ビジネスロジックは MainWindowViewModel）
/// </summary>
public class MainWindow : Window
{
    private Border? _dropOverlay;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _dropOverlay = this.FindControl<Border>("DropOverlay");
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
            DataContext = new MainWindowViewModel(pickExtractionFolder, pickCompressionFolder, ShowProgressWindow);
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

        if (!e.DataTransfer.Contains(DataFormat.File) || e.DataTransfer.TryGetFiles() is not { } files)
            return;
        var filePaths = new List<string>();
        foreach (var file in files)
        {
            if (file.TryGetLocalPath() is { } path)
                filePaths.Add(path);
        }
        if (filePaths.Count > 0 && DataContext is MainWindowViewModel vm)
            await vm.ProcessDroppedPathsAsync(filePaths);
    }
}
