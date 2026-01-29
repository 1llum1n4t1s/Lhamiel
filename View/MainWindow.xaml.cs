using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using System.IO;
using Lhamiel.Util;

namespace Lhamiel.View;

/// <summary>
/// MainWindow.xaml の相互作用ロジック
/// </summary>
public partial class MainWindow : Window
{
    private readonly SettingsManager _settingsManager;
    private readonly bool _isInitializing;
    private readonly Dictionary<string, CheckBox> _associationCheckBoxes;

    private Border? DropZoneBorder;
    private RadioButton? ExtractionOutputToSameDirectoryRadio;
    private RadioButton? ExtractionOutputToDirectoryRadio;
    private TextBox? ExtractionOutputPathTextBox;
    private Button? ExtractionBrowseButton;
    private CheckBox? OpenExtractionOutputFolderCheckBox;
    private RadioButton? CompressionOutputToSameDirectoryRadio;
    private RadioButton? CompressionOutputToDirectoryRadio;
    private TextBox? CompressionOutputPathTextBox;
    private Button? CompressionBrowseButton;
    private CheckBox? OpenCompressionOutputFolderCheckBox;
    private Button? SelectAllButton;
    private Button? DeselectAllButton;
    private CheckBox? ZipCheckBox;
    private CheckBox? SevenZipCheckBox;
    private CheckBox? TarCheckBox;
    private CheckBox? GzCheckBox;
    private CheckBox? Bz2CheckBox;
    private CheckBox? LzmaCheckBox;
    private CheckBox? XzCheckBox;
    private CheckBox? RarCheckBox;
    private CheckBox? LzhCheckBox;
    private CheckBox? CabCheckBox;
    private CheckBox? ArjCheckBox;
    private CheckBox? ZCheckBox;
    private CheckBox? TgzCheckBox;
    private CheckBox? Tbz2CheckBox;
    private CheckBox? TbzCheckBox;
    private CheckBox? TlzCheckBox;
    private CheckBox? TxzCheckBox;
    private CheckBox? TZCheckBox;
    private TextBlock? VersionTextBlock;
    private TextBlock? CopyrightTextBlock;
    private TextBlock? LicenseTextBlock;
    private ComboBox? CompressionFormatComboBox;
    private Button? CreateShortcutButton;
    private Button? SaveButton;
    private Button? CancelButton;

    /// <summary>
    /// 必須コントロールを取得する（null の場合は例外）
    /// </summary>
    private static T RequireControl<T>(T? control, string name) where T : class =>
        control ?? throw new InvalidOperationException($"Control '{name}' not found.");

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        DropZoneBorder = this.FindControl<Border>("DropZoneBorder");
        ExtractionOutputToSameDirectoryRadio = this.FindControl<RadioButton>("ExtractionOutputToSameDirectoryRadio");
        ExtractionOutputToDirectoryRadio = this.FindControl<RadioButton>("ExtractionOutputToDirectoryRadio");
        ExtractionOutputPathTextBox = this.FindControl<TextBox>("ExtractionOutputPathTextBox");
        ExtractionBrowseButton = this.FindControl<Button>("ExtractionBrowseButton");
        OpenExtractionOutputFolderCheckBox = this.FindControl<CheckBox>("OpenExtractionOutputFolderCheckBox");
        CompressionOutputToSameDirectoryRadio = this.FindControl<RadioButton>("CompressionOutputToSameDirectoryRadio");
        CompressionOutputToDirectoryRadio = this.FindControl<RadioButton>("CompressionOutputToDirectoryRadio");
        CompressionOutputPathTextBox = this.FindControl<TextBox>("CompressionOutputPathTextBox");
        CompressionBrowseButton = this.FindControl<Button>("CompressionBrowseButton");
        OpenCompressionOutputFolderCheckBox = this.FindControl<CheckBox>("OpenCompressionOutputFolderCheckBox");
        SelectAllButton = this.FindControl<Button>("SelectAllButton");
        DeselectAllButton = this.FindControl<Button>("DeselectAllButton");
        ZipCheckBox = this.FindControl<CheckBox>("ZipCheckBox");
        SevenZipCheckBox = this.FindControl<CheckBox>("SevenZipCheckBox");
        TarCheckBox = this.FindControl<CheckBox>("TarCheckBox");
        GzCheckBox = this.FindControl<CheckBox>("GzCheckBox");
        Bz2CheckBox = this.FindControl<CheckBox>("Bz2CheckBox");
        LzmaCheckBox = this.FindControl<CheckBox>("LzmaCheckBox");
        XzCheckBox = this.FindControl<CheckBox>("XzCheckBox");
        RarCheckBox = this.FindControl<CheckBox>("RarCheckBox");
        LzhCheckBox = this.FindControl<CheckBox>("LzhCheckBox");
        CabCheckBox = this.FindControl<CheckBox>("CabCheckBox");
        ArjCheckBox = this.FindControl<CheckBox>("ArjCheckBox");
        ZCheckBox = this.FindControl<CheckBox>("ZCheckBox");
        TgzCheckBox = this.FindControl<CheckBox>("TgzCheckBox");
        Tbz2CheckBox = this.FindControl<CheckBox>("Tbz2CheckBox");
        TbzCheckBox = this.FindControl<CheckBox>("TbzCheckBox");
        TlzCheckBox = this.FindControl<CheckBox>("TlzCheckBox");
        TxzCheckBox = this.FindControl<CheckBox>("TxzCheckBox");
        TZCheckBox = this.FindControl<CheckBox>("TZCheckBox");
        VersionTextBlock = this.FindControl<TextBlock>("VersionTextBlock");
        CopyrightTextBlock = this.FindControl<TextBlock>("CopyrightTextBlock");
        LicenseTextBlock = this.FindControl<TextBlock>("LicenseTextBlock");
        CompressionFormatComboBox = this.FindControl<ComboBox>("CompressionFormatComboBox");
        CreateShortcutButton = this.FindControl<Button>("CreateShortcutButton");
        SaveButton = this.FindControl<Button>("SaveButton");
        CancelButton = this.FindControl<Button>("CancelButton");
        if (ExtractionBrowseButton != null) ExtractionBrowseButton.Click += ExtractionBrowseButton_Click;
        if (CompressionBrowseButton != null) CompressionBrowseButton.Click += CompressionBrowseButton_Click;
        if (ExtractionOutputToSameDirectoryRadio != null) ExtractionOutputToSameDirectoryRadio.IsCheckedChanged += ExtractionOutputPattern_Changed;
        if (ExtractionOutputToDirectoryRadio != null) ExtractionOutputToDirectoryRadio.IsCheckedChanged += ExtractionOutputPattern_Changed;
        if (CompressionOutputToSameDirectoryRadio != null) CompressionOutputToSameDirectoryRadio.IsCheckedChanged += CompressionOutputPattern_Changed;
        if (CompressionOutputToDirectoryRadio != null) CompressionOutputToDirectoryRadio.IsCheckedChanged += CompressionOutputPattern_Changed;
        if (CompressionFormatComboBox != null) CompressionFormatComboBox.SelectionChanged += CompressionFormatComboBox_SelectionChanged;
        if (CreateShortcutButton != null) CreateShortcutButton.Click += CreateShortcutButton_Click;
        if (SaveButton != null) SaveButton.Click += SaveSettingsButton_Click;
        if (CancelButton != null) CancelButton.Click += CancelButton_Click;
        if (SelectAllButton != null) SelectAllButton.Click += SelectAllButton_Click;
        if (DeselectAllButton != null) DeselectAllButton.Click += DeselectAllButton_Click;
    }

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
                { "zip", RequireControl(ZipCheckBox, nameof(ZipCheckBox)) },
                { "7z", RequireControl(SevenZipCheckBox, nameof(SevenZipCheckBox)) },
                { "tar", RequireControl(TarCheckBox, nameof(TarCheckBox)) },
                { "gz", RequireControl(GzCheckBox, nameof(GzCheckBox)) },
                { "bz2", RequireControl(Bz2CheckBox, nameof(Bz2CheckBox)) },
                { "lzma", RequireControl(LzmaCheckBox, nameof(LzmaCheckBox)) },
                { "xz", RequireControl(XzCheckBox, nameof(XzCheckBox)) },
                { "rar", RequireControl(RarCheckBox, nameof(RarCheckBox)) },
                { "lzh", RequireControl(LzhCheckBox, nameof(LzhCheckBox)) },
                { "cab", RequireControl(CabCheckBox, nameof(CabCheckBox)) },
                { "arj", RequireControl(ArjCheckBox, nameof(ArjCheckBox)) },
                { "z", RequireControl(ZCheckBox, nameof(ZCheckBox)) },
                { "tgz", RequireControl(TgzCheckBox, nameof(TgzCheckBox)) },
                { "tbz2", RequireControl(Tbz2CheckBox, nameof(Tbz2CheckBox)) },
                { "tbz", RequireControl(TbzCheckBox, nameof(TbzCheckBox)) },
                { "tlz", RequireControl(TlzCheckBox, nameof(TlzCheckBox)) },
                { "txz", RequireControl(TxzCheckBox, nameof(TxzCheckBox)) },
                { "tz", RequireControl(TZCheckBox, nameof(TZCheckBox)) }
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
            var combo = CompressionFormatComboBox;
            var extractionPath = ExtractionOutputPathTextBox;
            var compressionPath = CompressionOutputPathTextBox;
            var extractionSame = ExtractionOutputToSameDirectoryRadio;
            var extractionDir = ExtractionOutputToDirectoryRadio;
            var compressionSame = CompressionOutputToSameDirectoryRadio;
            var compressionDir = CompressionOutputToDirectoryRadio;
            var openExtraction = OpenExtractionOutputFolderCheckBox;
            var openCompression = OpenCompressionOutputFolderCheckBox;
            if (combo == null || extractionPath == null || compressionPath == null || extractionSame == null || extractionDir == null || compressionSame == null || compressionDir == null || openExtraction == null || openCompression == null)
                return;

            combo.ItemsSource = Settings.SupportedCompressionFormats;
            var selectedFormat = _settingsManager.Current.CompressionFormat;
            if (!string.IsNullOrEmpty(selectedFormat) && Settings.SupportedCompressionFormats.Any(f =>
                f.Equals(selectedFormat, StringComparison.OrdinalIgnoreCase)))
            {
                combo.SelectedItem = Settings.SupportedCompressionFormats.FirstOrDefault(f =>
                    f.Equals(selectedFormat, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                combo.SelectedItem = "ZIP";
                _settingsManager.Current.CompressionFormat = "ZIP";
            }

            extractionPath.Text = _settingsManager.Current.ExtractionOutputDirectory;
            compressionPath.Text = _settingsManager.Current.CompressionOutputDirectory;

            extractionSame.IsChecked = _settingsManager.Current.ExtractionOutputToSameDirectory;
            extractionDir.IsChecked = !_settingsManager.Current.ExtractionOutputToSameDirectory;
            compressionSame.IsChecked = _settingsManager.Current.CompressionOutputToSameDirectory;
            compressionDir.IsChecked = !_settingsManager.Current.CompressionOutputToSameDirectory;

            openExtraction.IsChecked = _settingsManager.Current.OpenExtractionOutputFolder;
            openCompression.IsChecked = _settingsManager.Current.OpenCompressionOutputFolder;

            LoadAssociationStatus();
            LoadVersionInfo();

            extractionSame.IsCheckedChanged += ExtractionOutputPattern_Changed;
            extractionDir.IsCheckedChanged += ExtractionOutputPattern_Changed;
            compressionSame.IsCheckedChanged += CompressionOutputPattern_Changed;
            compressionDir.IsCheckedChanged += CompressionOutputPattern_Changed;
            combo.SelectionChanged += CompressionFormatComboBox_SelectionChanged;
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
    private void SaveSettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var combo = CompressionFormatComboBox;
            var extractionPath = ExtractionOutputPathTextBox;
            var compressionPath = CompressionOutputPathTextBox;
            var extractionSame = ExtractionOutputToSameDirectoryRadio;
            var compressionSame = CompressionOutputToSameDirectoryRadio;
            var openExtraction = OpenExtractionOutputFolderCheckBox;
            var openCompression = OpenCompressionOutputFolderCheckBox;
            if (combo == null || extractionPath == null || compressionPath == null || extractionSame == null || compressionSame == null || openExtraction == null || openCompression == null)
                return;

            _settingsManager.Current.CompressionFormat = combo.SelectedItem?.ToString() ?? "ZIP";
            _settingsManager.Current.ExtractionOutputDirectory = extractionPath.Text ?? string.Empty;
            _settingsManager.Current.CompressionOutputDirectory = compressionPath.Text ?? string.Empty;
            _settingsManager.Current.ExtractionOutputToSameDirectory = extractionSame.IsChecked ?? false;
            _settingsManager.Current.CompressionOutputToSameDirectory = compressionSame.IsChecked ?? false;
            _settingsManager.Current.OpenExtractionOutputFolder = openExtraction.IsChecked ?? false;
            _settingsManager.Current.OpenCompressionOutputFolder = openCompression.IsChecked ?? false;

            _settingsManager.Save();
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
    private async void ExtractionBrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "展開先ディレクトリを選択",
            AllowMultiple = false
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path && ExtractionOutputPathTextBox is { } extractionPath)
        {
            extractionPath.Text = path;
        }
    }

    /// <summary>
    /// 圧縮出力ディレクトリ選択ボタンクリック時の処理
    /// </summary>
    private async void CompressionBrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "圧縮先ディレクトリを選択",
            AllowMultiple = false
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path && CompressionOutputPathTextBox is { } compressionPath)
        {
            compressionPath.Text = path;
        }
    }

    /// <summary>
    /// 展開出力パターン変更時の処理
    /// </summary>
    private void ExtractionOutputPattern_Changed(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!_isInitializing && sender is RadioButton radioButton && radioButton.IsChecked == true && ExtractionOutputToSameDirectoryRadio is { } extractionSame)
            {
                _settingsManager.Current.ExtractionOutputToSameDirectory = radioButton == extractionSame;
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
    private void CompressionOutputPattern_Changed(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!_isInitializing && sender is RadioButton radioButton && radioButton.IsChecked == true && CompressionOutputToSameDirectoryRadio is { } compressionSame)
            {
                _settingsManager.Current.CompressionOutputToSameDirectory = radioButton == compressionSame;
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
    private void CompressionFormatComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (!_isInitializing && CompressionFormatComboBox is { } combo)
            {
                _settingsManager.Current.CompressionFormat = combo.SelectedItem?.ToString() ?? "ZIP";
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
    private void CreateShortcutButton_Click(object? sender, RoutedEventArgs e)
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
    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 全選択ボタンクリック時の処理
    /// </summary>
    private void SelectAllButton_Click(object? sender, RoutedEventArgs e)
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
    private void DeselectAllButton_Click(object? sender, RoutedEventArgs e)
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
    /// ドロップゾーンのドラッグオーバー時の処理
    /// </summary>
    private void DropZone_DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;

            // ドラッグ中の視覚的フィードバックを提供
            if (DropZoneBorder != null)
            {
                DropZoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 212)); // PrimaryColor
                DropZoneBorder.BorderThickness = new Thickness(3);
                DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(230, 243, 255)); // Light blue
            }
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    /// <summary>
    /// ドロップゾーンのドラッグリーブ時の処理
    /// </summary>
    private void DropZone_DragLeave(object? sender, RoutedEventArgs e)
    {
        // ドラッグが離れた時に元の見た目に戻す
        if (DropZoneBorder != null)
        {
            DropZoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
            DropZoneBorder.BorderThickness = new Thickness(2);
            DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(249, 249, 249)); // #F9F9F9
        }
    }

    /// <summary>
    /// ドロップゾーンのドロップ時の処理
    /// </summary>
    private async void DropZone_Drop(object? sender, DragEventArgs e)
    {
        // ドロップ後に元の見た目に戻す
        if (DropZoneBorder != null)
        {
            DropZoneBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
            DropZoneBorder.BorderThickness = new Thickness(2);
            DropZoneBorder.Background = new SolidColorBrush(Color.FromRgb(249, 249, 249)); // #F9F9F9
        }

        if (e.DataTransfer.Contains(DataFormat.File))
        {
            if (e.DataTransfer.TryGetFiles() is { } files)
            {
                var filePaths = new List<string>();
                foreach (var file in files)
                {
                    if (file.TryGetLocalPath() is { } path)
                    {
                        filePaths.Add(path);
                    }
                }
                if (filePaths.Count > 0)
                {
                    await ProcessDroppedFiles(filePaths.ToArray());
                }
            }
        }
    }

    /// <summary>
    /// ドロップされた複数のファイル/フォルダを処理する
    /// </summary>
    /// <param name="paths">ドロップされたファイル/フォルダのパス配列</param>
    private async Task ProcessDroppedFiles(string[] paths)
    {
        // アップデートによる再起動が予定されている場合は、新しい処理を開始しない
        if (Avalonia.Application.Current is App { IsUpdateRestarting: true })
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
                progressWindow.CloseSafe();
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
        var versionBlock = VersionTextBlock;
        var copyrightBlock = CopyrightTextBlock;
        var licenseBlock = LicenseTextBlock;
        if (versionBlock == null || copyrightBlock == null || licenseBlock == null)
            return;

        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var informationalVersionAttribute = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute;
            var rawVersion = informationalVersionAttribute?.InformationalVersion ?? "1.0.0";
            var versionString = rawVersion.Contains('+') ? rawVersion.Split('+')[0] : rawVersion;

            versionBlock.Text = versionString;
            var copyrightAttribute = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyCopyrightAttribute), false)
                .FirstOrDefault() as System.Reflection.AssemblyCopyrightAttribute;
            copyrightBlock.Text = copyrightAttribute?.Copyright ?? "Copyright © 2025-2026 ゆろち";

            licenseBlock.Text = @"MIT License

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
            versionBlock.Text = "不明";
            copyrightBlock.Text = "Copyright © 2024";
        }
    }
}
