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
public class MainWindow : Window
{
    private Border? DropZoneBorder;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        DropZoneBorder = this.FindControl<Border>("DropZoneBorder");
    }

    /// <summary>
    /// MainWindow のコンストラクタ
    /// </summary>
    public MainWindow()
    {
        try
        {
            InitializeComponent();
            var pickExtractionFolder = async () =>
            {
                var topLevel = GetTopLevel(this);
                if (topLevel == null) return null;
                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "展開先ディレクトリを選択",
                    AllowMultiple = false
                });
                return folders.Count > 0 && folders[0].TryGetLocalPath() is { } path ? path : null;
            };
            var pickCompressionFolder = async () =>
            {
                var topLevel = GetTopLevel(this);
                if (topLevel == null) return null;
                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "圧縮先ディレクトリを選択",
                    AllowMultiple = false
                });
                return folders.Count > 0 && folders[0].TryGetLocalPath() is { } path ? path : null;
            };
            void ShowProgressWindow(ProgressWindow w)
            {
                w.Show();
                w.Activate();
            }
            DataContext = new MainWindowViewModel(Close, pickExtractionFolder, pickCompressionFolder, ShowProgressWindow);
        }
        catch (Exception ex)
        {
            _ = MessageService.ShowException("アプリケーションの初期化に失敗しました", ex);
            throw;
        }
    }

    /// <summary>
    /// ドロップゾーンのドラッグオーバー時の処理（View の視覚フィードバック）
    /// </summary>
    private void DropZone_DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
            if (DropZoneBorder != null)
            {
                DropZoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 212));
                DropZoneBorder.BorderThickness = new Thickness(3);
                DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(230, 243, 255));
            }
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    /// <summary>
    /// ドロップゾーンのドラッグリーブ時の処理（View の視覚フィードバック）
    /// </summary>
    private void DropZone_DragLeave(object? sender, DragEventArgs e)
    {
        if (DropZoneBorder != null)
        {
            DropZoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
            DropZoneBorder.BorderThickness = new Thickness(2);
            DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(249, 249, 249));
        }
    }

    /// <summary>
    /// ドロップゾーンのドロップ時の処理（パスを ViewModel に渡す）
    /// </summary>
    private async void DropZone_Drop(object? sender, DragEventArgs e)
    {
        if (DropZoneBorder != null)
        {
            DropZoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
            DropZoneBorder.BorderThickness = new Thickness(2);
            DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(249, 249, 249));
        }
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
