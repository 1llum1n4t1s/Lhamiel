using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.IO;
using Lhamiel.Util;
using System.Threading;

namespace Lhamiel.View;

/// <summary>
/// MainWindow.xaml の相互作用ロジック
/// </summary>
public partial class MainWindow : Window
{
    private readonly Settings _settings;
    private readonly bool _isInitializing = true;

    /// <summary>
    /// MainWindowのコンストラクタ
    /// </summary>
    public MainWindow()
    {
        try
        {
            InitializeComponent();
            _settings = Settings.Load();
            InitializeUI();
            _isInitializing = false;
        }
        catch (Exception ex)
        {
            Logger.LogException("MainWindow初期化でエラーが発生", ex);
            MessageBox.Show($"アプリケーションの初期化に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    /// <summary>
    /// UIの初期化
    /// </summary>
    private void InitializeUI()
    {
        try
        {
            // 圧縮形式の選択肢を設定
            CompressionFormatComboBox.ItemsSource = Settings.SupportedCompressionFormats;

            // 設定された圧縮形式がサポートされているかチェックし、見つからない場合はデフォルト値を使用
            var selectedFormat = _settings.CompressionFormat?.ToLowerInvariant();
            if (Settings.SupportedCompressionFormats.Contains(selectedFormat))
            {
                CompressionFormatComboBox.SelectedItem = selectedFormat;
            }
            else
            {
                // デフォルト値（zip）を選択
                CompressionFormatComboBox.SelectedItem = "zip";
                _settings.CompressionFormat = "zip";
            }

            // 出力ディレクトリの設定
            ExtractionOutputPathTextBox.Text = _settings.ExtractionOutputDirectory;
            CompressionOutputPathTextBox.Text = _settings.CompressionOutputDirectory;

            // 出力先パターンの設定
            ExtractionOutputToSameDirectoryRadio.IsChecked = _settings.ExtractionOutputToSameDirectory;
            ExtractionOutputToDirectoryRadio.IsChecked = !_settings.ExtractionOutputToSameDirectory;
            CompressionOutputToSameDirectoryRadio.IsChecked = _settings.CompressionOutputToSameDirectory;
            CompressionOutputToDirectoryRadio.IsChecked = !_settings.CompressionOutputToSameDirectory;

            // 関連付け設定の読み込み
            LoadAssociationStatus();

            // イベントハンドラーを追加
            ExtractionOutputToSameDirectoryRadio.Checked += ExtractionOutputPattern_Changed;
            ExtractionOutputToDirectoryRadio.Checked += ExtractionOutputPattern_Changed;
            CompressionOutputToSameDirectoryRadio.Checked += CompressionOutputPattern_Changed;
            CompressionOutputToDirectoryRadio.Checked += CompressionOutputPattern_Changed;
            CompressionFormatComboBox.SelectionChanged += CompressionFormatComboBox_SelectionChanged;
        }
        catch (Exception ex)
        {
            Logger.LogException("UI初期化でエラーが発生", ex);
            MessageBox.Show($"UIの初期化に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    /// <summary>
    /// 関連付け設定の状態を読み込む
    /// </summary>
    private void LoadAssociationStatus()
    {
        try
        {
            // 現在の関連付け状態を取得
            var associationStatus = FileAssociation.GetCurrentAssociationStatus();

            // チェックボックスの状態を設定
            ZipCheckBox.IsChecked = associationStatus.GetValueOrDefault("zip", false);
            SevenZipCheckBox.IsChecked = associationStatus.GetValueOrDefault("7z", false);
            TarCheckBox.IsChecked = associationStatus.GetValueOrDefault("tar", false);
            GzCheckBox.IsChecked = associationStatus.GetValueOrDefault("gz", false);
            Bz2CheckBox.IsChecked = associationStatus.GetValueOrDefault("bz2", false);
            LzmaCheckBox.IsChecked = associationStatus.GetValueOrDefault("lzma", false);
            XzCheckBox.IsChecked = associationStatus.GetValueOrDefault("xz", false);
            RarCheckBox.IsChecked = associationStatus.GetValueOrDefault("rar", false);
            LzhCheckBox.IsChecked = associationStatus.GetValueOrDefault("lzh", false);
            CabCheckBox.IsChecked = associationStatus.GetValueOrDefault("cab", false);
            ArjCheckBox.IsChecked = associationStatus.GetValueOrDefault("arj", false);
            ZCheckBox.IsChecked = associationStatus.GetValueOrDefault("z", false);

            Logger.Log("関連付け設定の読み込みが完了しました");
        }

        catch (Exception ex)
        {
            Logger.LogException("関連付け設定の読み込みでエラーが発生", ex);
            // エラーが発生した場合はすべてのチェックボックスを非選択状態にする
            SetAllCheckBoxesToFalse();
        }
    }

    /// <summary>
    /// すべてのチェックボックスを非選択状態にする
    /// </summary>
    private void SetAllCheckBoxesToFalse()
    {
        try
        {
            ZipCheckBox.IsChecked = false;
            SevenZipCheckBox.IsChecked = false;
            TarCheckBox.IsChecked = false;
            GzCheckBox.IsChecked = false;
            Bz2CheckBox.IsChecked = false;
            LzmaCheckBox.IsChecked = false;
            XzCheckBox.IsChecked = false;
            RarCheckBox.IsChecked = false;
            LzhCheckBox.IsChecked = false;
            CabCheckBox.IsChecked = false;
            ArjCheckBox.IsChecked = false;
            ZCheckBox.IsChecked = false;
        }
        catch (Exception ex)
        {
            Logger.LogException("チェックボックスの状態設定でエラーが発生", ex);
        }
    }

    /// <summary>
    /// 圧縮ボタンクリック時の処理
    /// </summary>
    private async void CompressButton_Click(object sender, RoutedEventArgs e)
    {
        ProgressWindow? progressWindow = null;
        try
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "圧縮するファイルまたはフォルダを選択",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var format = CompressionFormatComboBox.SelectedItem?.ToString() ?? "zip";
                var outputDir = CompressionOutputPathTextBox.Text;
                var outputToSameDirectory = CompressionOutputToSameDirectoryRadio.IsChecked ?? false;

                progressWindow = new ProgressWindow("圧縮");
                var cancellationTokenSource = new CancellationTokenSource();
                progressWindow.CancelRequested += (_, _) => cancellationTokenSource.Cancel();
                progressWindow.Show();

                // ファイルとフォルダを分けて処理
                var files = new List<string>();
                var folders = new List<string>();

                foreach (var path in openFileDialog.FileNames)
                {
                    if (File.Exists(path))
                    {
                        files.Add(path);
                    }
                    else if (Directory.Exists(path))
                    {
                        folders.Add(path);
                    }
                }

                // フォルダの圧縮処理
                if (folders.Count > 0)
                {
                    await ArchiveProcessor.CompressFoldersAsync(folders.ToArray(), outputDir, outputToSameDirectory, format, progressWindow, cancellationTokenSource.Token);
                }

                // ファイルの圧縮処理
                if (files.Count > 0)
                {
                    foreach (var filePath in files)
                    {
                        cancellationTokenSource.Token.ThrowIfCancellationRequested();

                        var outputPath = ArchiveCompressor.GetCompressedFileName(filePath, format, outputDir, outputToSameDirectory);

                        // 出力ファイルが既に存在する場合は上書き確認
                        if (File.Exists(outputPath))
                        {
                            var canOverwrite = FileOverwriteDialog.CanOverwriteFile(filePath, outputPath, this);
                            if (!canOverwrite)
                            {
                                Logger.Log("ユーザーが圧縮処理をキャンセルしました");
                                continue;
                            }

                            // 上書きが許可された場合は既存ファイルを削除
                            File.Delete(outputPath);
                        }

                        progressWindow.SetFileName(outputPath);

                        var progress = new Progress<int>(percentage => {
                            progressWindow.UpdateProgress(percentage, "ファイルを圧縮中...");
                        });

                        await ArchiveCompressor.CompressAsync(filePath, outputPath, format, progress, cancellationTokenSource.Token);
                    }
                }

                progressWindow.SetCompleted("圧縮が完了しました。");
                await Task.Delay(1000);
                progressWindow.Close();
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Log("圧縮処理がキャンセルされました");
            if (progressWindow != null)
            {
                progressWindow.SetCompleted("キャンセルしました。");
                await Task.Delay(500);
                progressWindow.Close();
            }
            MessageBox.Show("圧縮処理をキャンセルしました。", "キャンセル", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Logger.LogException("圧縮処理でエラーが発生", ex);
            MessageBox.Show($"圧縮中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 展開ボタンクリック時の処理
    /// </summary>
    private async void ExtractButton_Click(object sender, RoutedEventArgs e)
    {
        ProgressWindow? progressWindow = null;
        try
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "展開するアーカイブファイルを選択",
                Filter = "アーカイブファイル|*.zip;*.7z;*.tar;*.gz;*.bz2;*.xz;*.rar;*.lzh;*.cab;*.arj;*.z|すべてのファイル|*.*",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var outputDir = ExtractionOutputPathTextBox.Text;
                var outputToSameDirectory = ExtractionOutputToSameDirectoryRadio.IsChecked ?? false;

                progressWindow = new ProgressWindow("展開");
                var cancellationTokenSource = new CancellationTokenSource();
                progressWindow.CancelRequested += (_, _) => cancellationTokenSource.Cancel();
                progressWindow.Show();

                // 共通化された展開処理を実行
                await ArchiveProcessor.ExtractArchivesAsync(openFileDialog.FileNames, outputDir, outputToSameDirectory, progressWindow, cancellationTokenSource.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Log("展開処理がキャンセルされました");
            if (progressWindow != null)
            {
                progressWindow.SetCompleted("キャンセルしました。");
                await Task.Delay(500);
                progressWindow.Close();
            }
            MessageBox.Show("展開処理をキャンセルしました。", "キャンセル", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Logger.LogException("展開処理でエラーが発生", ex);
            MessageBox.Show($"展開中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 設定保存ボタンクリック時の処理
    /// </summary>
    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.CompressionFormat = CompressionFormatComboBox.SelectedItem?.ToString() ?? "zip";
            _settings.ExtractionOutputDirectory = ExtractionOutputPathTextBox.Text;
            _settings.CompressionOutputDirectory = CompressionOutputPathTextBox.Text;
            _settings.ExtractionOutputToSameDirectory = ExtractionOutputToSameDirectoryRadio.IsChecked ?? false;
            _settings.CompressionOutputToSameDirectory = CompressionOutputToSameDirectoryRadio.IsChecked ?? false;

            _settings.Save();

            // 関連付け設定の処理
            ApplyAssociationSettings();

            Close();
        }
        catch (Exception ex)
        {
            Logger.LogException("設定保存でエラーが発生", ex);
            MessageBox.Show($"設定の保存に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 関連付け設定を適用する
    /// </summary>
    private void ApplyAssociationSettings()
    {
        try
        {
            Logger.Log("関連付け設定の適用を開始");

            // チェックボックスの状態に基づいて関連付けを設定/解除
            var associations = new Dictionary<string, bool>
            {
                { "zip", ZipCheckBox.IsChecked ?? false },
                { "7z", SevenZipCheckBox.IsChecked ?? false },
                { "tar", TarCheckBox.IsChecked ?? false },
                { "gz", GzCheckBox.IsChecked ?? false },
                { "bz2", Bz2CheckBox.IsChecked ?? false },
                { "lzma", LzmaCheckBox.IsChecked ?? false },
                { "xz", XzCheckBox.IsChecked ?? false },
                { "rar", RarCheckBox.IsChecked ?? false },
                { "lzh", LzhCheckBox.IsChecked ?? false },
                { "cab", CabCheckBox.IsChecked ?? false },
                { "arj", ArjCheckBox.IsChecked ?? false },
                { "z", ZCheckBox.IsChecked ?? false }
            };

            foreach (var association in associations)
            {
                var extension = association.Key;
                var shouldAssociate = association.Value;
                var isCurrentlyAssociated = FileAssociation.IsFileTypeAssociated(extension);

                if (shouldAssociate && !isCurrentlyAssociated)
                {
                    // 関連付けを設定
                    if (FileAssociation.AssociateFileType(extension))
                    {
                        Logger.Log($"関連付け設定成功: {extension}");
                    }
                    else
                    {
                        Logger.Log($"関連付け設定失敗: {extension}");
                    }
                }
                else if (!shouldAssociate && isCurrentlyAssociated)
                {
                    // 関連付けを解除
                    if (FileAssociation.DisassociateFileType(extension))
                    {
                        Logger.Log($"関連付け解除成功: {extension}");
                    }
                    else
                    {
                        Logger.Log($"関連付け解除失敗: {extension}");
                    }
                }
            }

            Logger.Log("関連付け設定の適用が完了しました");
        }
        catch (Exception ex)
        {
            Logger.LogException("関連付け設定の適用でエラーが発生", ex);
            MessageBox.Show($"関連付け設定の適用に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 展開出力ディレクトリ選択ボタンクリック時の処理
    /// </summary>
    private void ExtractionBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var folderDialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "展開先ディレクトリを選択"
        };

        if (folderDialog.ShowDialog() == true)
        {
            ExtractionOutputPathTextBox.Text = folderDialog.FolderName;
        }
    }

    /// <summary>
    /// 圧縮出力ディレクトリ選択ボタンクリック時の処理
    /// </summary>
    private void CompressionBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var folderDialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "圧縮先ディレクトリを選択"
        };

        if (folderDialog.ShowDialog() == true)
        {
            CompressionOutputPathTextBox.Text = folderDialog.FolderName;
        }
    }

    /// <summary>
    /// 展開出力パターン変更時の処理
    /// </summary>
    private void ExtractionOutputPattern_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_isInitializing && sender is RadioButton radioButton && _settings != null)
            {
                _settings.ExtractionOutputToSameDirectory = radioButton == ExtractionOutputToSameDirectoryRadio;
            }
        }

        catch (Exception ex)
        {
            Logger.LogException("展開出力パターン変更処理でエラーが発生", ex);
        }
    }

    /// <summary>
    /// 圧縮出力パターン変更時の処理
    /// </summary>
    private void CompressionOutputPattern_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_isInitializing && sender is RadioButton radioButton && _settings != null)
            {
                _settings.CompressionOutputToSameDirectory = radioButton == CompressionOutputToSameDirectoryRadio;
            }
        }

        catch (Exception ex)
        {
            Logger.LogException("圧縮出力パターン変更処理でエラーが発生", ex);
        }
    }

    /// <summary>
    /// 圧縮形式選択変更時の処理
    /// </summary>
    private void CompressionFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (!_isInitializing && _settings != null)
            {
                _settings.CompressionFormat = CompressionFormatComboBox.SelectedItem?.ToString() ?? "zip";
            }
        }

        catch (Exception ex)
        {
            Logger.LogException("圧縮形式選択変更処理でエラーが発生", ex);
        }
    }

    /// <summary>
    /// ショートカット作成ボタンクリック時の処理
    /// </summary>
    private void CreateShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        if (ShortcutCreator.CreateDesktopShortcut())
        {
            MessageBox.Show("デスクトップにショートカットを作成しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("ショートカットの作成に失敗しました。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// キャンセルボタンクリック時の処理
    /// </summary>
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 全選択ボタンクリック時の処理
    /// </summary>
    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // すべてのチェックボックスを選択状態にする
            ZipCheckBox.IsChecked = true;
            SevenZipCheckBox.IsChecked = true;
            TarCheckBox.IsChecked = true;
            GzCheckBox.IsChecked = true;
            Bz2CheckBox.IsChecked = true;
            LzmaCheckBox.IsChecked = true;
            XzCheckBox.IsChecked = true;
            RarCheckBox.IsChecked = true;
            LzhCheckBox.IsChecked = true;
            CabCheckBox.IsChecked = true;
            ArjCheckBox.IsChecked = true;
            ZCheckBox.IsChecked = true;
        }
        catch (Exception ex)
        {
            Logger.LogException("全選択処理でエラーが発生", ex);
            MessageBox.Show($"全選択処理でエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 全解除ボタンクリック時の処理
    /// </summary>
    private void DeselectAllButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // すべてのチェックボックスを非選択状態にする
            ZipCheckBox.IsChecked = false;
            SevenZipCheckBox.IsChecked = false;
            TarCheckBox.IsChecked = false;
            GzCheckBox.IsChecked = false;
            Bz2CheckBox.IsChecked = false;
            LzmaCheckBox.IsChecked = false;
            XzCheckBox.IsChecked = false;
            RarCheckBox.IsChecked = false;
            LzhCheckBox.IsChecked = false;
            CabCheckBox.IsChecked = false;
            ArjCheckBox.IsChecked = false;
            ZCheckBox.IsChecked = false;
        }
        catch (Exception ex)
        {
            Logger.LogException("全解除処理でエラーが発生", ex);
            MessageBox.Show($"全解除処理でエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// ドロップゾーンのドラッグエンター時の処理
    /// </summary>
    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    /// <summary>
    /// ドロップゾーンのドラッグリーブ時の処理
    /// </summary>
    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        e.Handled = true;
    }

    /// <summary>
    /// ドロップゾーンのドロップ時の処理
    /// </summary>
    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                var filePath = files[0];
                ProcessDroppedFile(filePath);
            }
        }
        e.Handled = true;
    }

    /// <summary>
    /// ドロップされたファイルを処理する
    /// </summary>
    /// <param name="filePath">ドロップされたファイルのパス</param>
    private void ProcessDroppedFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                if (ArchiveExtractor.IsSupportedArchiveType(filePath))
                {
                    // アーカイブファイルの場合は展開処理を開始
                    ExtractButton_Click(this, new RoutedEventArgs());
                }
                else
                {
                    // 通常ファイルの場合は圧縮処理を開始
                    CompressButton_Click(this, new RoutedEventArgs());
                }
            }
            else if (Directory.Exists(filePath))
            {
                // ディレクトリの場合は圧縮処理を開始
                CompressButton_Click(this, new RoutedEventArgs());
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("ドロップされたファイルの処理に失敗しました", ex);
            MessageBox.Show($"ファイルの処理に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


}
