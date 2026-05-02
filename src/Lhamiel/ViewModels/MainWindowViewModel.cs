using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lhamiel.Models;
using Lhamiel.Util;
using Lhamiel.View;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
namespace Lhamiel.ViewModels;

/// <summary>
/// 圧縮レベルの表示用クラス（リソースキーから動的に表示名を取得）
/// </summary>
public record CompressionLevelItem(int Level, string ResourceKey)
{
    public string Name => App.Text(ResourceKey);
}

/// <summary>
/// テーマ選択肢の表示用クラス（リソースキーから動的に表示名を取得）
/// </summary>
public record ThemeItem(string Key, string ResourceKey)
{
    public string DisplayName => App.Text(ResourceKey);
}

/// <summary>
/// ロケール選択肢の表示用クラス（固定表示名）
/// </summary>
public record LocaleItem(string Key, string DisplayName);

/// <summary>
/// MainWindow の ViewModel（MVVM）
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private const int DefaultCompressionLevel = 5;

    private readonly SettingsManager _settingsManager;
    private readonly Func<Task<string?>> _pickExtractionFolder;
    private readonly Func<Task<string?>> _pickCompressionFolder;
    private readonly Action<ProgressWindow> _showProgressWindow;
    private bool _isLoading;
    private CancellationTokenSource? _autoSaveCts;

    [ObservableProperty]
    private string _selectedTheme = "System";

    [ObservableProperty]
    private string _extractionOutputDirectory = string.Empty;

    [ObservableProperty]
    private string _compressionOutputDirectory = string.Empty;

    [ObservableProperty]
    private string _selectedCompressionFormat = "ZIP";

    [ObservableProperty]
    private bool _extractionOutputToSameDirectory;

    [ObservableProperty]
    private bool _extractionOutputToDirectory = true;

    [ObservableProperty]
    private bool _compressionOutputToSameDirectory;

    [ObservableProperty]
    private bool _compressionOutputToDirectory = true;

    [ObservableProperty]
    private bool _openExtractionOutputFolder = true;

    [ObservableProperty]
    private bool _createArchiveNameFolder = true;

    [ObservableProperty]
    private bool _openCompressionOutputFolder = true;

    [ObservableProperty]
    private bool _compressMultipleAsOne;

    [ObservableProperty]
    private int _selectedDirectoryStructureMode;

    [ObservableProperty]
    private string _selectedLocale = "";

    [ObservableProperty]
    private int _zipCompressionLevel = 5;

    [ObservableProperty]
    private int _sevenZipCompressionLevel = 5;

    [ObservableProperty]
    private CompressionLevelItem? _selectedZipLevel;

    [ObservableProperty]
    private CompressionLevelItem? _selectedSevenZipLevel;

    /// <summary>
    /// 設定値を 300ms デバウンス後に保存する（ロード中は抑制）。
    /// 連続する UI 操作（スライダー等）のディスク I/O を束ねる。
    /// </summary>
    private void AutoSave()
    {
        if (_isLoading) return;
        _autoSaveCts?.Cancel();
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;
        _ = ExecuteAutoSaveAsync(token);
    }

    private async Task ExecuteAutoSaveAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            _settingsManager.Mutate(s =>
            {
                s.Theme = SelectedTheme;
                s.Locale = SelectedLocale;
                s.CompressionFormat = SelectedCompressionFormat ?? "ZIP";
                s.ExtractionOutputDirectory = ExtractionOutputDirectory;
                s.CompressionOutputDirectory = CompressionOutputDirectory;
                s.ExtractionOutputToSameDirectory = ExtractionOutputToSameDirectory;
                s.CompressionOutputToSameDirectory = CompressionOutputToSameDirectory;
                s.OpenExtractionOutputFolder = OpenExtractionOutputFolder;
                s.CreateArchiveNameFolder = CreateArchiveNameFolder;
                s.OpenCompressionOutputFolder = OpenCompressionOutputFolder;
                s.CompressMultipleAsOne = CompressMultipleAsOne;
                s.DirectoryStructureMode = (DirectoryStructureMode)SelectedDirectoryStructureMode;
                s.ZipCompressionLevel = ZipCompressionLevel;
                s.SevenZipCompressionLevel = SevenZipCompressionLevel;
            });
            _settingsManager.Save();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.LogException("設定の自動保存に失敗", ex);
        }
    }

    partial void OnSelectedThemeChanged(string value)
    {
        App.SetTheme(value);
        AutoSave();
    }

    partial void OnSelectedLocaleChanged(string value)
    {
        App.SetLocale(value);

        // ロケール変更後、App.Text() で動的に表示名を取得するドロップダウンを再描画
        OnPropertyChanged(nameof(ThemeOptions));
        RefreshCompressionLevels();

        AutoSave();
    }

    partial void OnSelectedCompressionFormatChanged(string value) => AutoSave();

    partial void OnExtractionOutputDirectoryChanged(string value) => AutoSave();

    partial void OnCompressionOutputDirectoryChanged(string value) => AutoSave();

    partial void OnOpenExtractionOutputFolderChanged(bool value) => AutoSave();

    partial void OnCreateArchiveNameFolderChanged(bool value) => AutoSave();

    partial void OnOpenCompressionOutputFolderChanged(bool value) => AutoSave();

    partial void OnCompressMultipleAsOneChanged(bool value) => AutoSave();

    partial void OnSelectedDirectoryStructureModeChanged(int value) => AutoSave();

    partial void OnZipCompressionLevelChanged(int value)
    {
        SelectedZipLevel = CompressionLevels.FirstOrDefault(l => l.Level == value) ?? CompressionLevels.FirstOrDefault(l => l.Level == DefaultCompressionLevel);
        AutoSave();
    }

    partial void OnSevenZipCompressionLevelChanged(int value)
    {
        SelectedSevenZipLevel = CompressionLevels.FirstOrDefault(l => l.Level == value) ?? CompressionLevels.FirstOrDefault(l => l.Level == DefaultCompressionLevel);
        AutoSave();
    }

    partial void OnSelectedZipLevelChanged(CompressionLevelItem? value)
    {
        if (value != null) ZipCompressionLevel = value.Level;
    }

    partial void OnSelectedSevenZipLevelChanged(CompressionLevelItem? value)
    {
        if (value != null) SevenZipCompressionLevel = value.Level;
    }

    /// <summary>
    /// 圧縮レベルの選択肢
    /// </summary>
    public ObservableCollection<CompressionLevelItem> CompressionLevels { get; } =
    [
        new CompressionLevelItem(0, "CompressionLevel.None"),
        new CompressionLevelItem(1, "CompressionLevel.Fastest"),
        new CompressionLevelItem(3, "CompressionLevel.Fast"),
        new CompressionLevelItem(5, "CompressionLevel.Normal"),
        new CompressionLevelItem(7, "CompressionLevel.Maximum"),
        new CompressionLevelItem(9, "CompressionLevel.Ultra")
    ];

    /// <summary>
    /// 圧縮レベルのコレクションをリフレッシュ（ロケール変更時に表示名を更新）。
    /// CompressionLevelItem は record なので Name プロパティ自体は値が変わらず（App.Text が動的に解決）、
    /// ロケール切替時はバインドされた ComboBox に「アイテム自体が変わった」と伝えるだけでよい。
    /// Clear + Add で 7 回の CollectionChanged を発火する代わりに、各アイテムを同位置で Replace して
    /// CollectionChanged 発火数を削減する。
    /// </summary>
    private void RefreshCompressionLevels()
    {
        var savedZipLevel = ZipCompressionLevel;
        var savedSevenZipLevel = SevenZipCompressionLevel;

        _isLoading = true;
        for (var i = 0; i < CompressionLevels.Count; i++)
        {
            var old = CompressionLevels[i];
            CompressionLevels[i] = new CompressionLevelItem(old.Level, old.ResourceKey);
        }

        // 選択状態を復元
        SelectedZipLevel = CompressionLevels.FirstOrDefault(l => l.Level == savedZipLevel);
        SelectedSevenZipLevel = CompressionLevels.FirstOrDefault(l => l.Level == savedSevenZipLevel);
        _isLoading = false;
    }

    /// <summary>
    /// ファイル関連付けの一覧（拡張子・表示名・関連付け状態）
    /// </summary>
    public ObservableCollection<FileAssociationItem> Associations { get; } = CreateAssociationItems();

    [ObservableProperty]
    private string _versionText = string.Empty;

    [ObservableProperty]
    private string _sevenZipVersionText = string.Empty;

    [ObservableProperty]
    private string _copyrightText = string.Empty;

    [ObservableProperty]
    private string _licenseText = string.Empty;

    /// <summary>
    /// 更新チェックの状態メッセージ
    /// </summary>
    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    /// <summary>
    /// 更新チェック中かどうか
    /// </summary>
    [ObservableProperty]
    private bool _isCheckingUpdate;

    /// <summary>
    /// テーマの選択肢（キー: 設定値、値: 表示名）
    /// </summary>
    private static readonly ThemeItem[] _themeOptions =
    [
        new("System", "Settings.Theme.System"),
        new("Dark", "Settings.Theme.Dark"),
        new("Light", "Settings.Theme.Light")
    ];
    public ThemeItem[] ThemeOptions => _themeOptions;

    /// <summary>
    /// ロケールの選択肢（キー: ロケールコード、表示名: ネイティブ言語名）
    /// </summary>
    public static readonly LocaleItem[] LocaleOptions = App.SupportedLocales
        .Select(l => new LocaleItem(l, App.LocaleDisplayNames.GetValueOrDefault(l, l)))
        .ToArray();

    /// <summary>
    /// 圧縮形式の選択肢（ComboBox ItemsSource）
    /// </summary>
    public ObservableCollection<string> CompressionFormats { get; } = new(Settings.SupportedCompressionFormats);

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public MainWindowViewModel(
        Func<Task<string?>> pickExtractionFolder,
        Func<Task<string?>> pickCompressionFolder,
        Action<ProgressWindow> showProgressWindow)
    {
        _settingsManager = SettingsManager.Instance;
        _pickExtractionFolder = pickExtractionFolder;
        _pickCompressionFolder = pickCompressionFolder;
        _showProgressWindow = showProgressWindow;
        _isLoading = true;
        LoadFromSettings();
        LoadAssociationStatus();
        SubscribeAssociationChanges();
        LoadVersionInfo();

        // 初期選択状態を設定
        OnZipCompressionLevelChanged(ZipCompressionLevel);
        OnSevenZipCompressionLevelChanged(SevenZipCompressionLevel);
        _isLoading = false;
    }

    /// <summary>
    /// 設定から View に読み込む
    /// </summary>
    public void LoadFromSettings()
    {
        var s = _settingsManager.Current;
        SelectedTheme = ThemeOptions.Any(t => t.Key == s.Theme) ? s.Theme : "System";
        ExtractionOutputDirectory = s.ExtractionOutputDirectory;
        CompressionOutputDirectory = s.CompressionOutputDirectory;
        var format = s.CompressionFormat;
        SelectedCompressionFormat = (!string.IsNullOrEmpty(format)
            ? Settings.SupportedCompressionFormats.FirstOrDefault(f => f.Equals(format, StringComparison.OrdinalIgnoreCase))
            : null) ?? "ZIP";
        ExtractionOutputToSameDirectory = s.ExtractionOutputToSameDirectory;
        ExtractionOutputToDirectory = !s.ExtractionOutputToSameDirectory;
        CompressionOutputToSameDirectory = s.CompressionOutputToSameDirectory;
        CompressionOutputToDirectory = !s.CompressionOutputToSameDirectory;
        OpenExtractionOutputFolder = s.OpenExtractionOutputFolder;
        CreateArchiveNameFolder = s.CreateArchiveNameFolder;
        OpenCompressionOutputFolder = s.OpenCompressionOutputFolder;
        CompressMultipleAsOne = s.CompressMultipleAsOne;
        SelectedDirectoryStructureMode = (int)s.DirectoryStructureMode;
        SelectedLocale = string.IsNullOrEmpty(s.Locale) ? App.DetectDefaultLocale() : s.Locale;
        ZipCompressionLevel = s.ZipCompressionLevel;
        SevenZipCompressionLevel = s.SevenZipCompressionLevel;
    }

    partial void OnExtractionOutputToSameDirectoryChanged(bool value)
    {
        if (value) ExtractionOutputToDirectory = false;
        AutoSave();
    }

    partial void OnExtractionOutputToDirectoryChanged(bool value)
    {
        if (value) ExtractionOutputToSameDirectory = false;
        AutoSave();
    }

    partial void OnCompressionOutputToSameDirectoryChanged(bool value)
    {
        if (value) CompressionOutputToDirectory = false;
        AutoSave();
    }

    partial void OnCompressionOutputToDirectoryChanged(bool value)
    {
        if (value) CompressionOutputToSameDirectory = false;
        AutoSave();
    }

    [RelayCommand]
    private async Task BrowseExtractionAsync()
    {
        var path = await _pickExtractionFolder();
        if (!string.IsNullOrEmpty(path))
            ExtractionOutputDirectory = path;
    }

    [RelayCommand]
    private async Task BrowseCompressionAsync()
    {
        var path = await _pickCompressionFolder();
        if (!string.IsNullOrEmpty(path))
            CompressionOutputDirectory = path;
    }


    [RelayCommand]
    private void CreateShortcut()
    {
        try
        {
            if (ShortcutCreator.CreateDesktopShortcut())
                _ = MessageService.ShowSuccess(App.Text("Shortcut.Created"));
            else
                _ = MessageService.ShowError(App.Text("Shortcut.Failed"));
        }
        catch (Exception ex)
        {
            _ = MessageService.ShowException(App.Text("Shortcut.Error"), ex);
        }
    }

    [RelayCommand]
    private void SelectAllAssociations()
    {
        try
        {
            SetAllAssociations(true);
        }
        catch (Exception ex)
        {
            _ = MessageService.ShowException(App.Text("Error.SelectAll"), ex);
        }
    }

    [RelayCommand]
    private void DeselectAllAssociations()
    {
        try
        {
            SetAllAssociations(false);
        }
        catch (Exception ex)
        {
            _ = MessageService.ShowException(App.Text("Error.DeselectAll"), ex);
        }
    }

    /// <summary>
    /// ドロップされたパスを処理する（View から呼ぶ）
    /// </summary>
    public async Task ProcessDroppedPathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        ProgressWindow? progressWindow = null;
        try
        {
            // 有効なパスのみ収集
            var validPaths = paths.Where(p => Directory.Exists(p) || File.Exists(p)).ToList();
            if (validPaths.Count == 0) return;

            // 展開か圧縮かを事前に判定して操作種別ラベルを決定
            var isExtraction = validPaths.Count == 1
                ? File.Exists(validPaths[0]) && ArchiveExtractor.IsSupportedArchiveType(validPaths[0])
                : ArchiveExtractor.AreAllSupportedArchives(validPaths);
            var operationLabel = isExtraction
                ? App.Text("Progress.Extracting")
                : App.Text("Progress.Compressing");

            progressWindow = new ProgressWindow(operationLabel) { WindowStartupLocation = WindowStartupLocation.CenterOwner };
            _showProgressWindow(progressWindow);
            await Task.Yield();
            var cancellationToken = progressWindow.GetCancellationToken();
            // 並列処理中にUIスレッドが設定を書き換えても影響を受けないよう、処理開始時点で
            // スナップショットを取って以降は固定値として使う（/rere P0 #3 対応）。
            var settings = _settingsManager.CreateSnapshot();

            if (validPaths.Count == 1)
            {
                // 単一ファイル/フォルダ: 展開か圧縮かを自動判定
                var path = validPaths[0];
                if (isExtraction)
                {
                    var extractionResults = await ArchiveProcessor.ExtractArchivesAsync(
                        [path],
                        settings.ExtractionOutputDirectory,
                        settings.ExtractionOutputToSameDirectory,
                        progressWindow,
                        cancellationToken,
                        closeWindowOnCompletion: true);
                    if (extractionResults.Count > 0 && settings.OpenExtractionOutputFolder)
                        OpenExtractedFolders(extractionResults, settings.CreateArchiveNameFolder);
                }
                else
                {
                    await ArchiveProcessor.CompressItemsAsync(
                        [path],
                        settings.CompressionOutputDirectory,
                        settings.CompressionOutputToSameDirectory,
                        settings.CompressionFormat,
                        progressWindow,
                        cancellationToken,
                        closeWindowOnCompletion: true);
                    if (settings.OpenCompressionOutputFolder && !settings.CompressionOutputToSameDirectory)
                        FolderOpener.OpenFolder(settings.CompressionOutputDirectory);
                }
            }
            else if (isExtraction)
            {
                // 複数ファイル: すべてアーカイブなら個別展開
                var extractionResults = await ArchiveProcessor.ExtractArchivesAsync(
                    validPaths.ToArray(),
                    settings.ExtractionOutputDirectory,
                    settings.ExtractionOutputToSameDirectory,
                    progressWindow,
                    cancellationToken,
                    closeWindowOnCompletion: true);
                if (extractionResults.Count > 0 && settings.OpenExtractionOutputFolder)
                    OpenExtractedFolders(extractionResults, settings.CreateArchiveNameFolder);
            }
            else
            {
                // 複数ファイル: アーカイブ以外が混在 or 通常ファイルのみ → 圧縮
                if (settings.CompressMultipleAsOne)
                {
                    await ArchiveProcessor.CompressMergedAsync(
                        validPaths.ToArray(),
                        settings.CompressionOutputDirectory,
                        settings.CompressionOutputToSameDirectory,
                        settings.CompressionFormat,
                        progressWindow,
                        cancellationToken,
                        closeWindowOnCompletion: true);
                }
                else
                {
                    await ArchiveProcessor.CompressItemsAsync(
                        validPaths.ToArray(),
                        settings.CompressionOutputDirectory,
                        settings.CompressionOutputToSameDirectory,
                        settings.CompressionFormat,
                        progressWindow,
                        cancellationToken,
                        closeWindowOnCompletion: true);
                }
                if (settings.OpenCompressionOutputFolder && !settings.CompressionOutputToSameDirectory)
                    FolderOpener.OpenFolder(settings.CompressionOutputDirectory);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Log("処理がキャンセルされました");
            progressWindow?.SetCompleted(App.Text("Progress.Cancelled"));
            progressWindow?.CloseSafe();
        }
        catch (Exception ex)
        {
            Logger.LogException("ファイルの処理に失敗しました", ex);
            _ = MessageService.ShowException(App.Text("Error.ProcessFiles"), ex);
            progressWindow?.CloseSafe();
        }
    }

    /// <summary>
    /// ファイル関連付けの初期一覧を作成する（拡張子・表示名の定義はここで一元管理）
    /// </summary>
    private static ObservableCollection<FileAssociationItem> CreateAssociationItems()
    {
        var pairs = new[]
        {
            ("zip", "ZIP (.zip)"),
            ("7z", "7-Zip (.7z)"),
            ("tar", "TAR (.tar)"),
            ("gz", "GZIP (.gz)"),
            ("bz2", "BZIP2 (.bz2)"),
            ("lzma", "LZMA (.lzma)"),
            ("xz", "XZ (.xz)"),
            ("rar", "RAR (.rar)"),
            ("lzh", "LZH (.lzh)"),
            ("cab", "CAB (.cab)"),
            ("arj", "ARJ (.arj)"),
            ("z", "Z (.z)"),
            ("tgz", "TAR.GZ (.tgz)"),
            ("tbz2", "TAR.BZ2 (.tbz2)"),
            ("tbz", "TAR.BZ (.tbz)"),
            ("tlz", "TAR.LZMA (.tlz)"),
            ("txz", "TAR.XZ (.txz)"),
            ("tz", "TAR.Z (.tz)")
        };
        var list = new ObservableCollection<FileAssociationItem>();
        foreach (var (ext, desc) in pairs)
            list.Add(new FileAssociationItem { Extension = ext, Description = desc });
        return list;
    }

    private void LoadAssociationStatus()
    {
        try
        {
            var status = FileAssociation.GetCurrentAssociationStatus();
            foreach (var item in Associations)
                item.IsAssociated = status.GetValueOrDefault(item.Extension, false);
            Logger.Log("関連付け設定の読み込みが完了しました");
        }
        catch (Exception ex)
        {
            Logger.LogException("関連付け設定の読み込みでエラーが発生", ex);
            SetAllAssociations(false);
        }
    }

    private void SetAllAssociations(bool isChecked)
    {
        _suppressAssociationApply = true;
        foreach (var item in Associations)
            item.IsAssociated = isChecked;
        _suppressAssociationApply = false;
        ApplyAssociationSettings();
    }

    private bool _suppressAssociationApply;

    /// <summary>
    /// 各関連付け項目の変更を購読し、チェックボックス操作時に即座に適用する
    /// </summary>
    private void SubscribeAssociationChanges()
    {
        foreach (var item in Associations)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FileAssociationItem.IsAssociated) && !_isLoading && !_suppressAssociationApply)
                    ApplyAssociationSettings();
            };
        }
    }

    private void ApplyAssociationSettings()
    {
        try
        {
            Logger.Log("関連付け設定の適用を開始");
            var currentStatus = FileAssociation.GetCurrentAssociationStatus();
            foreach (var item in Associations)
            {
                var isCurrentlyAssociated = currentStatus.GetValueOrDefault(item.Extension, false);
                if (item.IsAssociated && !isCurrentlyAssociated)
                {
                    if (FileAssociation.AssociateFileType(item.Extension))
                        Logger.Log($"関連付け設定成功: {item.Extension}");
                    else
                        Logger.Log($"関連付け設定失敗: {item.Extension}", LogLevel.Warning);
                }
                else if (!item.IsAssociated && isCurrentlyAssociated)
                {
                    if (FileAssociation.DisassociateFileType(item.Extension))
                        Logger.Log($"関連付け解除成功: {item.Extension}");
                    else
                        Logger.Log($"関連付け解除失敗: {item.Extension}", LogLevel.Warning);
                }
            }
            FileAssociation.NotifyExplorer();
            Logger.Log("関連付け設定の適用が完了しました");
        }
        catch (Exception ex)
        {
            _ = MessageService.ShowException(App.Text("Error.ApplyAssociation"), ex);
        }
    }

    private void LoadVersionInfo()
    {
        try
        {
            var assembly = typeof(MainWindowViewModel).Assembly;
            var rawVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
            // ビルドメタデータ（'+' 以降）を除去して表示用バージョンを取得
            VersionText = rawVersion.Contains('+') ? rawVersion.Split('+')[0] : rawVersion;
            CopyrightText = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "Copyright © 2025-2026 ゆろち";

            // 7z.dll（7-Zip本家）のバージョンを取得
            var dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7z.dll");
            if (File.Exists(dllPath))
            {
                var fileVersion = FileVersionInfo.GetVersionInfo(dllPath);
                SevenZipVersionText = fileVersion.FileVersion ?? App.Text("Info.Unknown");
            }
            else
            {
                SevenZipVersionText = App.Text("Info.Unknown");
            }
            // LICENSEファイルを埋め込みリソースから読み込み
            using var stream = assembly.GetManifestResourceStream("Lhamiel.LICENSE");
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                LicenseText = reader.ReadToEnd().TrimEnd();
            }
            else
            {
                LicenseText = App.Text("Info.LicenseLoadFailed");
            }
            Logger.Log($"バージョン情報を読み込みました: Version {VersionText}");
        }
        catch (Exception ex)
        {
            Logger.LogException("バージョン情報の読み込みでエラーが発生", ex);
            VersionText = App.Text("Info.Unknown");
            CopyrightText = "Copyright © 2024";
        }
    }

    /// <summary>
    /// 最新バージョンの確認コマンド
    /// </summary>
    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        if (IsCheckingUpdate) return;

        IsCheckingUpdate = true;
        UpdateStatusText = string.Empty;

        try
        {
            var statusProgress = new Progress<string>(message => UpdateStatusText = message);
            var result = await UpdateChecker.CheckAndDownloadAsync(statusProgress);

            switch (result.Result)
            {
                case UpdateChecker.UpdateResult.Downloaded when result.Info != null && result.Manager != null:
                    UpdateStatusText = App.Text("Update.Applying");
                    // ApplyUpdatesAndRestart でアプリを再起動 → 通常起動フローでメイン画面が表示される
                    result.Manager.ApplyUpdatesAndRestart(result.Info);
                    break;

                case UpdateChecker.UpdateResult.NoUpdate:
                    UpdateStatusText = App.Text("Update.UpToDate");
                    break;

                case UpdateChecker.UpdateResult.NotInstalled:
                    UpdateStatusText = App.Text("Update.DevEnvironment");
                    break;

                case UpdateChecker.UpdateResult.NotConfigured:
                    UpdateStatusText = App.Text("Update.NoRepository");
                    break;

                default:
                    UpdateStatusText = $"⚠ {result.Message}";
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("更新チェックコマンドでエラーが発生", ex);
            UpdateStatusText = App.Text("Update.Error");
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private static void OpenExtractedFolders(
        IEnumerable<(string SourcePath, string OutputPath, ArchiveExtractor.ArchiveStructureInfo StructureInfo)> extractionResults,
        bool createArchiveNameFolder)
    {
        foreach (var (_, outputPath, structureInfo) in extractionResults)
            FolderOpener.OpenExtractionResult(outputPath, structureInfo, createArchiveNameFolder);
    }

    /// <summary>
    /// フィードバック用のGitHub Issuesページをブラウザで開くコマンド
    /// </summary>
    [RelayCommand]
    private async Task OpenFeedbackLinkAsync()
    {
        const string url = "https://github.com/1llum1n4t1s/Lhamiel/issues";
        var window = await MessageService.GetActiveWindowAsync();
        var dialog = new View.ConfirmDialog(
            App.Text("Version.Feedback.Confirm"),
            App.Text("Version.Feedback.ConfirmTitle"));
        var confirmed = window != null
            ? await dialog.ShowDialog<bool>(window)
            : false;
        if (!confirmed) return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.LogException("フィードバックリンクを開けませんでした", ex);
        }
    }
}
