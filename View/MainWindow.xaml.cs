using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.IO;
using Lhamiel.Util;
using System.Threading;
using System.Windows.Media;

namespace Lhamiel.View;

/// <summary>
/// MainWindow.xaml の相互作用ロジック
/// </summary>
public partial class MainWindow : Window
{
    private readonly SettingsManager _settingsManager;
    private readonly bool _isInitializing = true;
    private readonly Dictionary<string, CheckBox> _associationCheckBoxes;

    /// <summary>
    /// MainWindowのコンストラクタ
    /// </summary>
    public MainWindow()
    {
        try
        {
            InitializeComponent();
            _settingsManager = SettingsManager.Instance;

            // チェックボックスの辞書を初期化
            _associationCheckBoxes = new Dictionary<string, CheckBox>
            {
                { "zip", ZipCheckBox },
                { "7z", SevenZipCheckBox },
                { "tar", TarCheckBox },
                { "gz", GzCheckBox },
                { "bz2", Bz2CheckBox },
                { "lzma", LzmaCheckBox },
                { "xz", XzCheckBox },
                { "rar", RarCheckBox },
                { "lzh", LzhCheckBox },
                { "cab", CabCheckBox },
                { "arj", ArjCheckBox },
                { "z", ZCheckBox },
                { "tgz", TgzCheckBox },
                { "tbz2", Tbz2CheckBox },
                { "tbz", TbzCheckBox },
                { "tlz", TlzCheckBox },
                { "txz", TxzCheckBox },
                { "tz", TZCheckBox }
            };

            InitializeUI();
            _isInitializing = false;
        }
        catch (Exception ex)
        {
            MessageService.ShowException("アプリケーションの初期化に失敗しました", ex);
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
            // メイン圧縮形式の選択肢を設定
            CompressionFormatComboBox.ItemsSource = Settings.SupportedCompressionFormats;

            // 設定された圧縮形式がサポートされているかチェックし、見つからない場合はデフォルト値を使用
            var selectedFormat = _settingsManager.Current.CompressionFormat?.ToLowerInvariant();
            if (Settings.SupportedCompressionFormats.Contains(selectedFormat))
            {
                CompressionFormatComboBox.SelectedItem = selectedFormat;
            }
            else
            {
                // デフォルト値（zip）を選択
                CompressionFormatComboBox.SelectedItem = "zip";
                _settingsManager.Current.CompressionFormat = "zip";
            }

            // 出力ディレクトリの設定
            ExtractionOutputPathTextBox.Text = _settingsManager.Current.ExtractionOutputDirectory;
            CompressionOutputPathTextBox.Text = _settingsManager.Current.CompressionOutputDirectory;

            // 出力先パターンの設定
            ExtractionOutputToSameDirectoryRadio.IsChecked = _settingsManager.Current.ExtractionOutputToSameDirectory;
            ExtractionOutputToDirectoryRadio.IsChecked = !_settingsManager.Current.ExtractionOutputToSameDirectory;
            CompressionOutputToSameDirectoryRadio.IsChecked = _settingsManager.Current.CompressionOutputToSameDirectory;
            CompressionOutputToDirectoryRadio.IsChecked = !_settingsManager.Current.CompressionOutputToSameDirectory;

            // フォルダを開く設定の読み込み
            OpenExtractionOutputFolderCheckBox.IsChecked = _settingsManager.Current.OpenExtractionOutputFolder;
            OpenCompressionOutputFolderCheckBox.IsChecked = _settingsManager.Current.OpenCompressionOutputFolder;

            // 関連付け設定の読み込み
            LoadAssociationStatus();

            // バージョン情報を設定
            LoadVersionInfo();

            // イベントハンドラーを追加
            ExtractionOutputToSameDirectoryRadio.Checked += ExtractionOutputPattern_Changed;
            ExtractionOutputToDirectoryRadio.Checked += ExtractionOutputPattern_Changed;
            CompressionOutputToSameDirectoryRadio.Checked += CompressionOutputPattern_Changed;
            CompressionOutputToDirectoryRadio.Checked += CompressionOutputPattern_Changed;
            CompressionFormatComboBox.SelectionChanged += CompressionFormatComboBox_SelectionChanged;
        }
        catch (Exception ex)
        {
            MessageService.ShowException("UIの初期化に失敗しました", ex);
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
            foreach (var kvp in _associationCheckBoxes)
            {
                kvp.Value.IsChecked = associationStatus.GetValueOrDefault(kvp.Key, false);
            }

            Logger.Log("関連付け設定の読み込みが完了しました");
        }

        catch (Exception ex)
        {
            Logger.LogException("関連付け設定の読み込みでエラーが発生", ex);
            // エラーが発生した場合はすべてのチェックボックスを非選択状態にする
            SetAllCheckBoxes(false);
        }
    }

    /// <summary>
    /// すべてのチェックボックスを指定した状態にする
    /// </summary>
    /// <param name="isChecked">チェック状態</param>
    private void SetAllCheckBoxes(bool isChecked)
    {
        try
        {
            foreach (var checkBox in _associationCheckBoxes.Values)
            {
                checkBox.IsChecked = isChecked;
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("チェックボックスの状態設定でエラーが発生", ex);
        }
    }

    /// <summary>
    /// 圧縮ボタンクリック時の処理（ファイル選択のみ）
    /// </summary>
    private async void CompressButton_Click(object sender, RoutedEventArgs e)
    {
        ProgressWindow? progressWindow = null;
        try
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "圧縮するファイルを選択",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var files = openFileDialog.FileNames.Where(File.Exists).ToList();
                if (files.Count == 0)
                {
                    MessageService.ShowWarning("選択されたファイルが見つかりません。");
                    return;
                }

                var format = CompressionFormatComboBox.SelectedItem?.ToString() ?? "zip";
                var outputDir = CompressionOutputPathTextBox.Text;
                var outputToSameDirectory = CompressionOutputToSameDirectoryRadio.IsChecked ?? false;

                progressWindow = new ProgressWindow("圧縮");
                var cancellationTokenSource = new CancellationTokenSource();
                progressWindow.CancelRequested += (_, _) => cancellationTokenSource.Cancel();
                progressWindow.Show();

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

                progressWindow.SetCompleted("圧縮が完了しました。");
                await Task.Delay(1000);
                progressWindow.Close();

                // 圧縮後にフォルダを開く設定を確認
                if (_settingsManager.Current.OpenCompressionOutputFolder)
                {
                    FolderOpener.OpenFolder(CompressionOutputPathTextBox.Text);
                }
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
            MessageService.ShowInfo("圧縮処理をキャンセルしました。", "キャンセル");
        }
        catch (Exception ex)
        {
            MessageService.ShowException("圧縮中にエラーが発生しました", ex);
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

                // 展開後にフォルダを開く設定を確認
                if (_settingsManager.Current.OpenExtractionOutputFolder)
                {
                    FolderOpener.OpenFolder(ExtractionOutputPathTextBox.Text);
                }
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
            MessageService.ShowInfo("展開処理をキャンセルしました。", "キャンセル");
        }
        catch (Exception ex)
        {
            MessageService.ShowException("展開中にエラーが発生しました", ex);
        }
    }

    /// <summary>
    /// 設定保存ボタンクリック時の処理
    /// </summary>
    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settingsManager.Current.CompressionFormat = CompressionFormatComboBox.SelectedItem?.ToString() ?? "zip";
            _settingsManager.Current.ExtractionOutputDirectory = ExtractionOutputPathTextBox.Text;
            _settingsManager.Current.CompressionOutputDirectory = CompressionOutputPathTextBox.Text;
            _settingsManager.Current.ExtractionOutputToSameDirectory = ExtractionOutputToSameDirectoryRadio.IsChecked ?? false;
            _settingsManager.Current.CompressionOutputToSameDirectory = CompressionOutputToSameDirectoryRadio.IsChecked ?? false;
            _settingsManager.Current.OpenExtractionOutputFolder = OpenExtractionOutputFolderCheckBox.IsChecked ?? false;
            _settingsManager.Current.OpenCompressionOutputFolder = OpenCompressionOutputFolderCheckBox.IsChecked ?? false;

            _settingsManager.Save();

            // 関連付け設定の処理
            ApplyAssociationSettings();

            Close();
        }
        catch (Exception ex)
        {
            MessageService.ShowException("設定の保存に失敗しました", ex);
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
            foreach (var kvp in _associationCheckBoxes)
            {
                var extension = kvp.Key;
                var shouldAssociate = kvp.Value.IsChecked ?? false;
                var isCurrentlyAssociated = FileAssociation.IsFileTypeAssociated(extension);

                if (shouldAssociate && !isCurrentlyAssociated)
                {
                    // 関連付けを設定
                    if (FileAssociation.AssociateFileType(extension))
                    {
                        Logger.Log($"関連付け設定成功: {extension}", LogLevel.Info);
                    }
                    else
                    {
                        Logger.Log($"関連付け設定失敗: {extension}", LogLevel.Warning);
                    }
                }
                else if (!shouldAssociate && isCurrentlyAssociated)
                {
                    // 関連付けを解除
                    if (FileAssociation.DisassociateFileType(extension))
                    {
                        Logger.Log($"関連付け解除成功: {extension}", LogLevel.Info);
                    }
                    else
                    {
                        Logger.Log($"関連付け解除失敗: {extension}", LogLevel.Warning);
                    }
                }
            }

            Logger.Log("関連付け設定の適用が完了しました", LogLevel.Info);
        }
        catch (Exception ex)
        {
            MessageService.ShowException("関連付け設定の適用に失敗しました", ex);
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
            if (!_isInitializing && sender is RadioButton radioButton)
            {
                _settingsManager.Current.ExtractionOutputToSameDirectory = radioButton == ExtractionOutputToSameDirectoryRadio;
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
            if (!_isInitializing && sender is RadioButton radioButton)
            {
                _settingsManager.Current.CompressionOutputToSameDirectory = radioButton == CompressionOutputToSameDirectoryRadio;
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
            if (!_isInitializing)
            {
                _settingsManager.Current.CompressionFormat = CompressionFormatComboBox.SelectedItem?.ToString() ?? "zip";
                _settingsManager.Save();
            }
        }

        catch (Exception ex)
        {
            Logger.LogException("圧縮形式選択変更処理でエラーが発生", ex);
        }
    }

    /// <summary>
    /// デスクトップにショートカット作成ボタンクリック時の処理
    /// </summary>
    private void CreateShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ShortcutCreator.CreateDesktopShortcut())
            {
                MessageService.ShowSuccess("デスクトップにショートカットを作成しました。");
            }
            else
            {
                MessageService.ShowError("ショートカットの作成に失敗しました。");
            }
        }
        catch (Exception ex)
        {
            MessageService.ShowException("ショートカットの作成中にエラーが発生しました", ex);
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
            SetAllCheckBoxes(true);
        }
        catch (Exception ex)
        {
            MessageService.ShowException("全選択処理でエラーが発生しました", ex);
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
            SetAllCheckBoxes(false);
        }
        catch (Exception ex)
        {
            MessageService.ShowException("全解除処理でエラーが発生しました", ex);
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

            // ドラッグ中の視覚的フィードバックを提供
            DropZoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 212)); // PrimaryColor
            DropZoneBorder.BorderThickness = new Thickness(3);
            DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(230, 243, 255)); // Light blue
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
        // ドラッグが離れた時に元の見た目に戻す
        DropZoneBorder.BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"];
        DropZoneBorder.BorderThickness = new Thickness(2);
        DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(249, 249, 249)); // #F9F9F9

        e.Handled = true;
    }

    /// <summary>
    /// ドロップゾーンのドロップ時の処理
    /// </summary>
    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        // ドロップ後に元の見た目に戻す
        DropZoneBorder.BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"];
        DropZoneBorder.BorderThickness = new Thickness(2);
        DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(249, 249, 249)); // #F9F9F9

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                ProcessDroppedFiles(files);
            }
        }
        e.Handled = true;
    }

    /// <summary>
    /// ドロップされた複数のファイル/フォルダを処理する
    /// </summary>
    /// <param name="paths">ドロップされたファイル/フォルダのパス配列</param>
    private void ProcessDroppedFiles(string[] paths)
    {
        try
        {
            // アーカイブファイルと圧縮対象を分類
            var archiveFiles = new List<string>();
            var compressionTargets = new List<string>();

            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    if (ArchiveExtractor.IsSupportedArchiveType(path))
                    {
                        // アーカイブファイル
                        archiveFiles.Add(path);
                    }
                    else
                    {
                        // 通常ファイル
                        compressionTargets.Add(path);
                    }
                }
                else if (Directory.Exists(path))
                {
                    // ディレクトリ
                    compressionTargets.Add(path);
                }
            }

            // アーカイブファイルの展開処理（最初の1つのみ、従来の動作を維持）
            if (archiveFiles.Count > 0)
            {
                ProcessDroppedArchive(archiveFiles[0]);
            }
            // 圧縮対象の処理（複数を並行処理）
            else if (compressionTargets.Count > 0)
            {
                ProcessDroppedFilesForCompression(compressionTargets.ToArray());
            }
        }
        catch (Exception ex)
        {
            MessageService.ShowException("ファイルの処理に失敗しました", ex);
        }
    }

    /// <summary>
    /// ドロップされたファイルを処理する（単一ファイル用・下位互換性のために残す）
    /// </summary>
    /// <param name="filePath">ドロップされたファイルのパス</param>
    private void ProcessDroppedFile(string filePath)
    {
        ProcessDroppedFiles(new[] { filePath });
    }

    /// <summary>
    /// ドロップされたアーカイブファイルを展開する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    private async void ProcessDroppedArchive(string archivePath)
    {
        ProgressWindow? progressWindow = null;
        try
        {
            var outputDir = ExtractionOutputPathTextBox.Text;
            var outputToSameDirectory = ExtractionOutputToSameDirectoryRadio.IsChecked ?? false;

            progressWindow = new ProgressWindow("展開");
            var cancellationTokenSource = new CancellationTokenSource();
            progressWindow.CancelRequested += (_, _) => cancellationTokenSource.Cancel();
            progressWindow.Show();

            var success = await ArchiveProcessor.ExtractArchiveAsync(archivePath, outputDir, outputToSameDirectory, progressWindow, cancellationTokenSource.Token);

            if (success)
            {
                MessageService.ShowSuccess("展開が完了しました。");

                // 展開後にフォルダを開く設定を確認
                if (_settingsManager.Current.OpenExtractionOutputFolder)
                {
                    FolderOpener.OpenFolder(ExtractionOutputPathTextBox.Text);
                }
            }
            else
            {
                MessageService.ShowError("展開中にエラーが発生しました。");
            }
        }
        catch (OperationCanceledException)
        {
            MessageService.ShowInfo("展開がキャンセルされました。");
        }
        catch (Exception ex)
        {
            MessageService.ShowException("展開中にエラーが発生しました", ex);
        }
        finally
        {
            progressWindow?.Close();
        }
    }

    /// <summary>
    /// ドロップされた複数のファイル/フォルダを並行圧縮する
    /// </summary>
    /// <param name="paths">圧縮するファイル/フォルダのパス配列</param>
    private async void ProcessDroppedFilesForCompression(string[] paths)
    {
        ProgressWindow? progressWindow = null;
        try
        {
            var format = CompressionFormatComboBox.SelectedItem?.ToString() ?? "zip";
            var outputDir = CompressionOutputPathTextBox.Text;
            var outputToSameDirectory = CompressionOutputToSameDirectoryRadio.IsChecked ?? false;

            progressWindow = new ProgressWindow("圧縮");
            var cancellationTokenSource = new CancellationTokenSource();
            progressWindow.CancelRequested += (_, _) => cancellationTokenSource.Cancel();
            progressWindow.Show();

            // ファイルとフォルダを分けて処理
            var folders = new List<string>();
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    folders.Add(path);
                }
                else if (File.Exists(path))
                {
                    // ファイルの場合は親ディレクトリを圧縮対象とする
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && !folders.Contains(dir))
                    {
                        folders.Add(dir);
                    }
                }
            }

            if (folders.Count == 0)
            {
                MessageService.ShowWarning("圧縮対象のフォルダが見つかりません。");
                progressWindow.Close();
                return;
            }

            // 並行処理用の進捗管理
            var totalFolders = folders.Count;
            var completedCount = 0;
            var progressLock = new object();
            var folderProgress = new Dictionary<string, int>();

            // 各フォルダの進捗を初期化
            foreach (var folder in folders)
            {
                folderProgress[folder] = 0;
            }

            // 並行圧縮処理を実行
            var tasks = folders.Select(async folderPath =>
            {
                try
                {
                    cancellationTokenSource.Token.ThrowIfCancellationRequested();

                    var outputPath = ArchiveCompressor.GetCompressedFileName(folderPath, format, outputDir, outputToSameDirectory);

                    // 出力ファイルが既に存在する場合は上書き確認
                    if (File.Exists(outputPath))
                    {
                        var canOverwrite = await progressWindow.Dispatcher.InvokeAsync(() =>
                            FileOverwriteDialog.CanOverwriteFile(folderPath, outputPath, progressWindow));

                        if (!canOverwrite)
                        {
                            Logger.Log($"ユーザーが圧縮処理をキャンセルしました: {folderPath}");
                            return false;
                        }

                        File.Delete(outputPath);
                    }

                    // 個別の進捗を追跡
                    var progress = new Progress<int>(percentage =>
                    {
                        lock (progressLock)
                        {
                            folderProgress[folderPath] = percentage;
                            var totalProgress = folderProgress.Values.Sum() / totalFolders;
                            progressWindow.UpdateProgress(totalProgress, $"圧縮中... ({completedCount + 1}/{totalFolders})");
                        }
                    });

                    await ArchiveCompressor.CompressAsync(folderPath, outputPath, format, progress, cancellationTokenSource.Token);

                    lock (progressLock)
                    {
                        completedCount++;
                        progressWindow.SetFileName($"{Path.GetFileName(outputPath)} 完了 ({completedCount}/{totalFolders})");
                    }

                    Logger.Log($"圧縮完了: {folderPath} -> {outputPath}");
                    return true;
                }
                catch (OperationCanceledException)
                {
                    Logger.Log($"圧縮処理がキャンセルされました: {folderPath}");
                    return false;
                }
                catch (Exception ex)
                {
                    Logger.LogException($"圧縮処理でエラーが発生: {folderPath}", ex);
                    return false;
                }
            }).ToArray();

            // すべての圧縮タスクが完了するまで待機
            var results = await Task.WhenAll(tasks);
            var successCount = results.Count(r => r);

            if (successCount > 0)
            {
                progressWindow.SetCompleted($"圧縮が完了しました。({successCount}/{totalFolders}個成功)");
                await Task.Delay(1000);

                MessageService.ShowSuccess($"{successCount}/{totalFolders}個のフォルダの圧縮が完了しました。");

                // 圧縮後にフォルダを開く設定を確認
                if (_settingsManager.Current.OpenCompressionOutputFolder)
                {
                    FolderOpener.OpenFolder(CompressionOutputPathTextBox.Text);
                }
            }
            else
            {
                MessageService.ShowWarning("圧縮処理が完了しませんでした。");
            }
        }
        catch (OperationCanceledException)
        {
            MessageService.ShowInfo("圧縮がキャンセルされました。");
        }
        catch (Exception ex)
        {
            MessageService.ShowException("圧縮中にエラーが発生しました", ex);
        }
        finally
        {
            progressWindow?.Close();
        }
    }

    /// <summary>
    /// ドロップされたファイル/フォルダを圧縮する
    /// </summary>
    /// <param name="paths">圧縮するファイル/フォルダのパス一覧</param>
    private async void ProcessDroppedFileForCompression(string[] paths)
    {
        ProgressWindow? progressWindow = null;
        try
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

            foreach (var path in paths)
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

            // ファイルの圧縮処理（1つずつ処理）
            if (files.Count > 0)
            {
                // 単一ファイルの場合、同じディレクトリに圧縮する場合は親フォルダを圧縮対象にする
                // 複数ファイルの場合は、各ファイルの親フォルダを圧縮対象にする
                var fileParentDirs = files.Select(f => Path.GetDirectoryName(f)).Distinct().ToList();

                foreach (var dir in fileParentDirs)
                {
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        cancellationTokenSource.Token.ThrowIfCancellationRequested();
                        // ファイルの親ディレクトリを圧縮対象にして処理
                        await ArchiveProcessor.CompressFolderAsync(dir, outputDir, outputToSameDirectory, format, progressWindow, cancellationTokenSource.Token);
                    }
                }
            }

            if (folders.Count > 0 || files.Count > 0)
            {
                MessageService.ShowSuccess("圧縮が完了しました。");

                // 圧縮後にフォルダを開く設定を確認
                if (_settingsManager.Current.OpenCompressionOutputFolder)
                {
                    FolderOpener.OpenFolder(CompressionOutputPathTextBox.Text);
                }
            }
        }
        catch (OperationCanceledException)
        {
            MessageService.ShowInfo("圧縮がキャンセルされました。");
        }
        catch (Exception ex)
        {
            MessageService.ShowException("圧縮中にエラーが発生しました", ex);
        }
        finally
        {
            progressWindow?.Close();
        }
    }

    /// <summary>
    /// バージョン情報を読み込んでUIに設定する
    /// </summary>
    private void LoadVersionInfo()
    {
        try
        {
            // アセンブリからバージョン情報を取得
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            var versionString = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";

            // バージョン情報を設定
            VersionTextBlock.Text = versionString;

            // リリース日を設定（ビルド日時から取得）
            var buildDate = new DateTime(2000, 1, 1).AddDays(version?.Build ?? 0).AddSeconds((version?.Revision ?? 0) * 2);
            ReleaseDateTextBlock.Text = buildDate.ToString("yyyy年MM月dd日");

            // コピーライト情報を取得
            var copyrightAttribute = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyCopyrightAttribute), false)
                .FirstOrDefault() as System.Reflection.AssemblyCopyrightAttribute;
            CopyrightTextBlock.Text = copyrightAttribute?.Copyright ?? "Copyright © 2025-2026 ゆろち";

            // MITライセンステキストを設定
            LicenseTextBlock.Text = @"MIT License

Copyright (c) 2024 Lhamiel

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the ""Software""), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.";

            Logger.Log($"バージョン情報を読み込みました: Version {versionString}");
        }
        catch (Exception ex)
        {
            Logger.LogException("バージョン情報の読み込みでエラーが発生", ex);
            VersionTextBlock.Text = "不明";
            ReleaseDateTextBlock.Text = "不明";
            CopyrightTextBlock.Text = "Copyright © 2024";
        }
    }

}
