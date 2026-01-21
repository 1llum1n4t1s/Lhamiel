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
public partial class MainWindow
{
    private readonly SettingsManager _settingsManager;
    private readonly bool _isInitializing;
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

    private void InitializeUI()
    {
        try
        {
            CompressionFormatComboBox.ItemsSource = Settings.SupportedCompressionFormats;

            var selectedFormat = _settingsManager.Current.CompressionFormat;
            var supportedFormats = Settings.SupportedCompressionFormats.Select(f => f.ToUpperInvariant()).ToList();
            if (!string.IsNullOrEmpty(selectedFormat) && supportedFormats.Contains(selectedFormat.ToUpperInvariant()))
            {
                CompressionFormatComboBox.SelectedItem = Settings.SupportedCompressionFormats.FirstOrDefault(f => 
                    f.Equals(selectedFormat, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                CompressionFormatComboBox.SelectedItem = "ZIP";
                _settingsManager.Current.CompressionFormat = "ZIP";
            }

            ExtractionOutputPathTextBox.Text = _settingsManager.Current.ExtractionOutputDirectory;
            CompressionOutputPathTextBox.Text = _settingsManager.Current.CompressionOutputDirectory;

            ExtractionOutputToSameDirectoryRadio.IsChecked = _settingsManager.Current.ExtractionOutputToSameDirectory;
            ExtractionOutputToDirectoryRadio.IsChecked = !_settingsManager.Current.ExtractionOutputToSameDirectory;
            CompressionOutputToSameDirectoryRadio.IsChecked = _settingsManager.Current.CompressionOutputToSameDirectory;
            CompressionOutputToDirectoryRadio.IsChecked = !_settingsManager.Current.CompressionOutputToSameDirectory;

            OpenExtractionOutputFolderCheckBox.IsChecked = _settingsManager.Current.OpenExtractionOutputFolder;
            OpenCompressionOutputFolderCheckBox.IsChecked = _settingsManager.Current.OpenCompressionOutputFolder;

            LoadAssociationStatus();

            LoadVersionInfo();

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
                        Logger.Log($"関連付け設定成功: {extension}");
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
                        Logger.Log($"関連付け解除成功: {extension}");
                    }
                    else
                    {
                        Logger.Log($"関連付け解除失敗: {extension}", LogLevel.Warning);
                    }
                }
            }

            Logger.Log("関連付け設定の適用が完了しました");
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
        var folderDialog = new OpenFolderDialog
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
        var folderDialog = new OpenFolderDialog
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
    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        // ドロップ後に元の見た目に戻す
        DropZoneBorder.BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"];
        DropZoneBorder.BorderThickness = new Thickness(2);
        DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(249, 249, 249)); // #F9F9F9

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                await ProcessDroppedFiles(files);
            }
        }
        e.Handled = true;
    }

    /// <summary>
    /// ドロップされた複数のファイル/フォルダを処理する
    /// </summary>
    /// <param name="paths">ドロップされたファイル/フォルダのパス配列</param>
    private async Task ProcessDroppedFiles(string[] paths)
    {
        // アップデートによる再起動が予定されている場合は、新しい処理を開始しない
        if (Application.Current is App { IsUpdateRestarting: true })
        {
            Logger.Log("アップデートのための再起動が予定されているため、新しい処理をスキップします。");
            MessageService.ShowWarning("アップデートの適用準備が整いました。再起動後に再度お試しください。");
            return;
        }

        ProgressWindow? progressWindow = null;
        try
        {
            // 1. ファイルを「展開対象」と「圧縮対象」に分別
            var filesToExtract = new List<string>();
            var filesToCompress = new List<string>();

            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    // フォルダは常に圧縮対象
                    filesToCompress.Add(path);
                }
                else if (File.Exists(path))
                {
                    // ファイルはアーカイブ形式なら展開、それ以外は圧縮
                    if (ArchiveExtractor.IsSupportedArchiveType(path))
                    {
                        filesToExtract.Add(path);
                    }
                    else
                    {
                        filesToCompress.Add(path);
                    }
                }
            }

            // 何も処理対象がない場合は終了
            if (filesToExtract.Count == 0 && filesToCompress.Count == 0) return;

            // 進捗ウィンドウを表示
            progressWindow = new ProgressWindow("処理中")
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            progressWindow.Show();
            progressWindow.Activate();

            // UIスレッドに描画を完了させる隙を与える
            await Task.Yield();

            // キャンセルトークンの取得
            var cancellationToken = progressWindow.GetCancellationToken();

            var settings = _settingsManager.Current;
            var hasCompression = filesToCompress.Count > 0;
            var hasExtraction = filesToExtract.Count > 0;

            // 2. 圧縮処理を実行（もしあれば）
            if (hasCompression)
            {
                // 次に展開処理が控えている場合はウィンドウを閉じない
                var closeWindow = !hasExtraction;

                await ArchiveProcessor.CompressItemsAsync(
                    filesToCompress.ToArray(),
                    settings.CompressionOutputDirectory,
                    settings.CompressionOutputToSameDirectory,
                    settings.CompressionFormat,
                    progressWindow,
                    cancellationToken,
                    closeWindowOnCompletion: closeWindow
                );
            }

            // キャンセルされていたら展開処理には進まない
            if (cancellationToken.IsCancellationRequested) return;

            // 3. 展開処理を実行（もしあれば）
            if (hasExtraction)
            {
                // 最後なのでウィンドウを閉じる
                var success = await ArchiveProcessor.ExtractArchivesAsync(
                    filesToExtract.ToArray(),
                    settings.ExtractionOutputDirectory,
                    settings.ExtractionOutputToSameDirectory,
                    progressWindow,
                    cancellationToken,
                    closeWindowOnCompletion: true
                );

                if (success && settings.OpenExtractionOutputFolder)
                {
                    OpenExtractedFolders(filesToExtract, settings.ExtractionOutputDirectory, settings.ExtractionOutputToSameDirectory);
                }
            }
            else if (hasCompression)
            {
                // 圧縮のみで完了した場合の「フォルダを開く」処理
                // 同じディレクトリに出力する場合は混乱を避けるため開かないように修正
                if (settings.OpenCompressionOutputFolder && !settings.CompressionOutputToSameDirectory)
                {
                    FolderOpener.OpenFolder(settings.CompressionOutputDirectory);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Log("処理がキャンセルされました");
            if (progressWindow != null)
            {
                progressWindow.SetCompleted("キャンセルしました。");
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("ファイルの処理に失敗しました", ex);
            MessageService.ShowException("ファイルの処理に失敗しました", ex);
            progressWindow?.CloseSafe();
        }
    }

    /// <summary>
    /// ドロップされた複数のアーカイブファイルを展開する
    /// </summary>
    /// <param name="archivePaths">アーカイブファイルのパス配列</param>
    private async Task ProcessDroppedArchives(string[] archivePaths)
    {
        // ProcessDroppedFiles に統合したため、このメソッドは個別に呼ばれることがなければ削除または委譲可能
        await ProcessDroppedFiles(archivePaths);
    }

    /// <summary>
    /// ドロップされた複数のファイル/フォルダを並行圧縮する
    /// </summary>
    /// <param name="paths">圧縮するファイル/フォルダのパス配列</param>
    private async Task ProcessDroppedFilesForCompression(string[] paths)
    {
        // ProcessDroppedFiles に統合したため、このメソッドは個別に呼ばれることがなければ削除または委譲可能
        await ProcessDroppedFiles(paths);
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
}
