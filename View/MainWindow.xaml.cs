using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.IO;
using Lhamiel.Util;
using System.Windows.Media;

namespace Lhamiel.View;

/// <summary>
/// MainWindow.xaml の相互作用ロジック
/// </summary>
public partial class MainWindow : Window
{
    private readonly SettingsManager _settingsManager;
    private bool _isInitializing;
    private readonly Dictionary<string, CheckBox> _associationCheckBoxes;

    /// <summary>
    /// MainWindowのコンストラクタ
    /// </summary>
    public MainWindow()
    {
        try
        {
            _isInitializing = true;
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
            
            // 注: 圧縮形式はZIPと7zのみをサポート（展開は複数形式対応）

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
            var selectedFormat = _settingsManager.Current.CompressionFormat?.ToUpperInvariant();
            if (Settings.SupportedCompressionFormats.Contains(selectedFormat))
            {
                CompressionFormatComboBox.SelectedItem = selectedFormat;
            }
            else
            {
                // デフォルト値（ZIP）を選択
                CompressionFormatComboBox.SelectedItem = "ZIP";
                _settingsManager.Current.CompressionFormat = "ZIP";
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

                var format = CompressionFormatComboBox.SelectedItem?.ToString() ?? "ZIP";
                var outputDir = CompressionOutputPathTextBox.Text;
                var outputToSameDirectory = CompressionOutputToSameDirectoryRadio.IsChecked ?? false;

                progressWindow = new ProgressWindow("圧縮");
                progressWindow.Show();

                var cancellationToken = progressWindow.GetCancellationToken();

                foreach (var filePath in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

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

                    progressWindow.SetFileName(Path.GetFileName(outputPath));

                    var progress = new Progress<ProgressInfo>(info =>
                    {
                        progressWindow.UpdateProgress(info.Percentage, info.Status);
                        if (!string.IsNullOrEmpty(info.CurrentFileName))
                        {
                            progressWindow.SetFileName(Path.GetFileName(info.CurrentFileName));
                        }
                    });

                    await ArchiveCompressor.CompressAsync(filePath, outputPath, format, progress, cancellationToken);
                }

                await Task.Delay(500);
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
                    OpenExtractedFolders(openFileDialog.FileNames, outputDir, outputToSameDirectory);
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
            _settingsManager.Current.CompressionFormat = CompressionFormatComboBox.SelectedItem?.ToString() ?? "ZIP";
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
                _settingsManager.Current.CompressionFormat = CompressionFormatComboBox.SelectedItem?.ToString() ?? "ZIP";
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

            // アーカイブファイルの展開処理（複数対応）
            if (archiveFiles.Count > 0)
            {
                ProcessDroppedArchives(archiveFiles.ToArray());
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
    /// ドロップされた複数のアーカイブファイルを展開する
    /// </summary>
    /// <param name="archivePaths">アーカイブファイルのパス配列</param>
    private async void ProcessDroppedArchives(string[] archivePaths)
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

            var success = await ArchiveProcessor.ExtractArchivesAsync(
                archivePaths,
                outputDir,
                outputToSameDirectory,
                progressWindow,
                cancellationTokenSource.Token);

            if (success)
            {
                if (_settingsManager.Current.OpenExtractionOutputFolder)
                {
                    OpenExtractedFolders(archivePaths, outputDir, outputToSameDirectory);
                }
            }
            else
            {
                MessageService.ShowWarning("一部のファイルの展開に失敗したか、キャンセルされました。");
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
            if (progressWindow?.IsVisible == true)
            {
                progressWindow.Close();
            }
        }
    }

    /// <summary>
    /// ドロップされた複数のファイル/フォルダを圧縮用フォルダに整理する
    /// </summary>
    /// <param name="paths">ドロップされたファイル/フォルダのパス配列</param>
    /// <returns>圧縮対象フォルダの一意なリスト</returns>
    private List<string> ExtractCompressionTargetFolders(string[] paths)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                Logger.Log($"ディレクトリとして検出: {path}");
                folders.Add(path);
            }
            else if (File.Exists(path))
            {
                // ファイルの場合は親ディレクトリを圧縮対象とする
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    Logger.Log($"ファイルとして検出: {path}, 親ディレクトリ: {dir}");
                    folders.Add(dir);
                }
            }
            else
            {
                Logger.Log($"パスが存在しません: {path}");
            }
        }

        return folders.ToList();
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
            Logger.Log($"ProcessDroppedFilesForCompression開始: ドロップされたパス数 = {paths.Length}");

            // ★ 改善: フォルダ整理ロジックを独立したメソッドに分離
            var folders = ExtractCompressionTargetFolders(paths);
            
            if (folders.Count == 0)
            {
                Logger.Log("圧縮対象のフォルダが見つかりません");
                MessageService.ShowWarning("圧縮対象のフォルダが見つかりません。");
                return;
            }

            Logger.Log($"圧縮対象フォルダ数: {folders.Count}");

            var format = CompressionFormatComboBox.SelectedItem?.ToString() ?? "ZIP";
            var outputDir = CompressionOutputPathTextBox.Text;
            var outputToSameDirectory = CompressionOutputToSameDirectoryRadio.IsChecked ?? false;

            progressWindow = new ProgressWindow("圧縮");
            progressWindow.Show();
            var cancellationTokenSource = new CancellationTokenSource();
            progressWindow.CancelRequested += (_, _) => cancellationTokenSource.Cancel();

            // ★ 修正: 複雑な並列処理ロジックを削除し、ArchiveProcessor.CompressFoldersAsync に委譲
            // これにより、コードの重複を避け、進捗管理を一元化する
            var success = await ArchiveProcessor.CompressFoldersAsync(
                folders.ToArray(),
                outputDir,
                outputToSameDirectory,
                format,
                progressWindow,
                cancellationTokenSource.Token
            );

            if (success)
            {
                if (_settingsManager.Current.OpenCompressionOutputFolder)
                {
                    FolderOpener.OpenFolder(CompressionOutputPathTextBox.Text);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Log("圧縮処理がキャンセルされました");
            MessageService.ShowInfo("圧縮処理をキャンセルしました。");
        }
        catch (Exception ex)
        {
            Logger.LogException("圧縮処理でエラーが発生", ex);
            MessageService.ShowException("圧縮中にエラーが発生しました", ex);
        }
        finally
        {
            if (progressWindow != null)
            {
                try
                {
                    // ProgressWindowのDispatcher内で保留中のアクションをフラッシュ
                    progressWindow.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                }
                catch
                {
                    // ウィンドウが既にクローズされている可能性
                }

                try
                {
                    progressWindow.Close();
                }
                catch
                {
                    // ウィンドウのクローズに失敗した場合は無視
                }
            }
        }
    }

    /// <summary>
    /// 展開されたフォルダを開く
    /// </summary>
    private void OpenExtractedFolders(IEnumerable<string> archivePaths, string outputDir, bool outputToSameDirectory)
    {
        var openedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var archivePath in archivePaths)
        {
            // 基準となる出力先（デスクトップなど）
            var baseDir = ArchiveExtractor.GetBaseOutputDirectory(archivePath, outputDir, outputToSameDirectory);
            var targetPath = baseDir; // デフォルトは基準ディレクトリ

            try
            {
                // スマート展開（単一ルート要素）かどうかを確認
                var rootItemName = ArchiveExtractor.GetSingleRootItemName(archivePath);

                if (!string.IsNullOrEmpty(rootItemName))
                {
                    // Case 1: スマート展開（単一ルート要素）の場合
                    // アーカイブの中身（ProjectA）が基準ディレクトリ直下に展開されている
                    var possibleDir = Path.Combine(baseDir, rootItemName);

                    // そのルート要素がフォルダとして存在する場合、そのフォルダを開く
                    // （ファイルだった場合は親であるbaseDirを開くのが自然なので何もしない）
                    if (Directory.Exists(possibleDir))
                    {
                        targetPath = possibleDir;
                    }
                }
                else
                {
                    // Case 2: 通常展開（複数要素）の場合
                    // アーカイブ名のフォルダが作成され、その中に展開されている
                    var fileName = Path.GetFileNameWithoutExtension(archivePath);
                    var possibleDir = Path.Combine(baseDir, fileName);

                    if (Directory.Exists(possibleDir))
                    {
                        targetPath = possibleDir;
                    }
                }

                // 決定したパスが存在し、かつまだ開いていない場合に開く
                if (Directory.Exists(targetPath) && openedPaths.Add(targetPath))
                {
                    FolderOpener.OpenFolder(targetPath);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException($"展開先フォルダを開く処理でエラー: {archivePath}", ex);
            }
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

            // パッケージバージョンをAssemblyInformationalVersionから取得
            var informationalVersionAttribute = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute;
            var rawVersion = informationalVersionAttribute?.InformationalVersion ?? "1.0.0";
            // ハッシュ部分（+ 以降）を削除して整形
            var versionString = rawVersion.Contains('+') ? rawVersion.Split('+')[0] : rawVersion;

            // バージョン情報を設定
            VersionTextBlock.Text = versionString;

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
            CopyrightTextBlock.Text = "Copyright © 2024";
        }
    }

    /// <summary>
    /// アップデート確認ボタンクリック時の処理
    /// </summary>
    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CheckUpdateButton.IsEnabled = false;
            CheckUpdateButton.Content = "⏳ チェック中...";

            // アップデート確認を実行
            var updateInfo = await PerformUpdateCheckAsync();

            if (updateInfo == null)
            {
                // 最新バージョンである - チェック時刻を記録
                var settings = Settings.Load();
                settings.LastUpdateCheckTime = DateTime.Now.ToString("o");
                settings.Save();
                Logger.Log("Velopack: アップデート確認時刻を記録しました");

                MessageService.ShowSuccess("最新バージョンを使用しています。", "アップデート確認");
            }
            else
            {
                // 新しいバージョンが利用可能 - 強制的にアップデート開始
                Logger.Log($"Velopack: 新しいバージョンを検出しました。アップデートを開始します。");
                MessageService.ShowInfo("新しいバージョンが見つかりました。アップデートをダウンロード・インストールして再起動します。", "アップデート");

                try
                {
                    // アップデートマネージャーを取得
                    var settings = Settings.Load();
                    var repoOwner = settings.UpdateRepoOwner;
                    var repoName = settings.UpdateRepoName;
                    var channel = string.IsNullOrWhiteSpace(settings.UpdateChannel) ? "release" : settings.UpdateChannel;

                    if (!string.IsNullOrWhiteSpace(repoOwner) && !string.IsNullOrWhiteSpace(repoName))
                    {
                        var repoUrl = $"https://github.com/{repoOwner}/{repoName}";
                        var isPrerelease = channel.Equals("prerelease", StringComparison.OrdinalIgnoreCase);
                        var source = new Velopack.Sources.GithubSource(repoUrl, string.Empty, isPrerelease);
                        var updateManager = new Velopack.UpdateManager(source);

                        if (updateManager.IsInstalled && updateInfo != null)
                        {
                            Logger.Log("Velopack: 更新をダウンロード中...");
                            await updateManager.DownloadUpdatesAsync(updateInfo);
                            Logger.Log("Velopack: ダウンロード完了。更新を適用して再起動します。");

                            // チェック時刻を記録
                            settings.LastUpdateCheckTime = DateTime.Now.ToString("o");
                            settings.Save();
                            Logger.Log("Velopack: アップデート確認時刻を記録しました");

                            updateManager.ApplyUpdatesAndRestart(updateInfo.TargetFullRelease);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException("Velopack: アップデート処理中にエラーが発生", ex);
                    MessageService.ShowError("アップデートの処理中にエラーが発生しました。" + Environment.NewLine + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("アップデート確認中にエラーが発生", ex);
            MessageService.ShowError("アップデート確認中にエラーが発生しました。" + Environment.NewLine + ex.Message);
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
            CheckUpdateButton.Content = "🔄 アップデート確認";
        }
    }

    /// <summary>
    /// アップデートをチェックして情報を取得する
    /// </summary>
    /// <returns>更新情報オブジェクト。最新の場合はnull</returns>
    private async Task<Velopack.UpdateInfo?> PerformUpdateCheckAsync()
    {
        try
        {
            var settings = Settings.Load();
            var repoOwner = settings.UpdateRepoOwner;
            var repoName = settings.UpdateRepoName;
            var channel = string.IsNullOrWhiteSpace(settings.UpdateChannel) ? "release" : settings.UpdateChannel;

            if (string.IsNullOrWhiteSpace(repoOwner) || string.IsNullOrWhiteSpace(repoName))
            {
                Logger.Log("Velopack: 更新元リポジトリが未設定のため、アップデートチェックをスキップします。");
                MessageService.ShowWarning("更新元リポジトリが設定されていません。");
                return null;
            }

            var repoUrl = $"https://github.com/{repoOwner}/{repoName}";
            var isPrerelease = channel.Equals("prerelease", StringComparison.OrdinalIgnoreCase);
            var source = new Velopack.Sources.GithubSource(repoUrl, string.Empty, isPrerelease);
            var updateManager = new Velopack.UpdateManager(source);

            if (!updateManager.IsInstalled)
            {
                Logger.Log("Velopack: 開発実行のため、アップデートチェックをスキップします。");
                MessageService.ShowInfo("開発実行環境のため、アップデート確認はスキップされました。");
                return null;
            }

            Logger.Log("Velopack: ユーザーが手動でアップデート確認を実行しました。");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var updateInfo = await updateManager.CheckForUpdatesAsync();

            if (updateInfo == null)
            {
                Logger.Log("Velopack: 利用可能な更新はありません。");
                return null;
            }

            Logger.Log($"Velopack: 新しいバージョンを検出しました。");
            return updateInfo;
        }
        catch (OperationCanceledException)
        {
            Logger.Log("Velopack: アップデート確認がタイムアウトしました。");
            MessageService.ShowWarning("アップデート確認がタイムアウトしました。しばらく後に再度お試しください。");
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogException("Velopack: アップデートチェック中にエラーが発生", ex);
            return null;
        }
    }

}
