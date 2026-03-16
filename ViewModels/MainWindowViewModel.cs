using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lhamiel.Models;
using Lhamiel.Util;
using Lhamiel.View;
using System.Collections.ObjectModel;
using System.Reflection;
namespace Lhamiel.ViewModels;

/// <summary>
/// 圧縮レベルの表示用クラス
/// </summary>
public record CompressionLevelItem(int Level, string Name);

/// <summary>
/// テーマ選択肢の表示用クラス（AOT安全）
/// </summary>
public record ThemeItem(string Key, string DisplayName);

/// <summary>
/// MainWindow の ViewModel（MVVM）
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly SettingsManager _settingsManager;
    private readonly Action _closeWindow;
    private readonly Func<Task<string?>> _pickExtractionFolder;
    private readonly Func<Task<string?>> _pickCompressionFolder;
    private readonly Action<ProgressWindow> _showProgressWindow;

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
    private bool _openCompressionOutputFolder = true;

    [ObservableProperty]
    private bool _compressMultipleAsOne;

    [ObservableProperty]
    private int _zipCompressionLevel = 5;

    [ObservableProperty]
    private int _sevenZipCompressionLevel = 5;

    [ObservableProperty]
    private CompressionLevelItem? _selectedZipLevel;

    [ObservableProperty]
    private CompressionLevelItem? _selectedSevenZipLevel;

    partial void OnSelectedThemeChanged(string value)
    {
        // テーマ変更時にリアルタイムプレビュー
        App.SetTheme(value);
    }

    partial void OnZipCompressionLevelChanged(int value)
    {
        SelectedZipLevel = CompressionLevels.FirstOrDefault(l => l.Level == value) ?? CompressionLevels.FirstOrDefault(l => l.Level == 5);
    }

    partial void OnSevenZipCompressionLevelChanged(int value)
    {
        SelectedSevenZipLevel = CompressionLevels.FirstOrDefault(l => l.Level == value) ?? CompressionLevels.FirstOrDefault(l => l.Level == 5);
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
        new CompressionLevelItem(0, "無圧縮"),
        new CompressionLevelItem(1, "最速"),
        new CompressionLevelItem(3, "高速"),
        new CompressionLevelItem(5, "標準"),
        new CompressionLevelItem(7, "最大"),
        new CompressionLevelItem(9, "超圧縮")
    ];

    /// <summary>
    /// ファイル関連付けの一覧（拡張子・表示名・関連付け状態）
    /// </summary>
    public ObservableCollection<FileAssociationItem> Associations { get; } = CreateAssociationItems();

    [ObservableProperty]
    private string _versionText = string.Empty;

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
    public static readonly ThemeItem[] ThemeOptions =
    [
        new("System", "システム（追従）"),
        new("Dark", "ダーク"),
        new("Light", "ライト")
    ];

    /// <summary>
    /// 圧縮形式の選択肢（ComboBox ItemsSource）
    /// </summary>
    public ObservableCollection<string> CompressionFormats { get; } = new(Settings.SupportedCompressionFormats);

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public MainWindowViewModel(
        Action closeWindow,
        Func<Task<string?>> pickExtractionFolder,
        Func<Task<string?>> pickCompressionFolder,
        Action<ProgressWindow> showProgressWindow)
    {
        _settingsManager = SettingsManager.Instance;
        _closeWindow = closeWindow;
        _pickExtractionFolder = pickExtractionFolder;
        _pickCompressionFolder = pickCompressionFolder;
        _showProgressWindow = showProgressWindow;
        LoadFromSettings();
        LoadAssociationStatus();
        LoadVersionInfo();
        
        // 初期選択状態を設定
        OnZipCompressionLevelChanged(ZipCompressionLevel);
        OnSevenZipCompressionLevelChanged(SevenZipCompressionLevel);
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
        if (!string.IsNullOrEmpty(format) && Settings.SupportedCompressionFormats.Any(f => f.Equals(format, StringComparison.OrdinalIgnoreCase)))
            SelectedCompressionFormat = Settings.SupportedCompressionFormats.First(f => f.Equals(format, StringComparison.OrdinalIgnoreCase));
        else
            SelectedCompressionFormat = "ZIP";
        ExtractionOutputToSameDirectory = s.ExtractionOutputToSameDirectory;
        ExtractionOutputToDirectory = !s.ExtractionOutputToSameDirectory;
        CompressionOutputToSameDirectory = s.CompressionOutputToSameDirectory;
        CompressionOutputToDirectory = !s.CompressionOutputToSameDirectory;
        OpenExtractionOutputFolder = s.OpenExtractionOutputFolder;
        OpenCompressionOutputFolder = s.OpenCompressionOutputFolder;
        CompressMultipleAsOne = s.CompressMultipleAsOne;
        ZipCompressionLevel = s.ZipCompressionLevel;
        SevenZipCompressionLevel = s.SevenZipCompressionLevel;
    }

    partial void OnExtractionOutputToSameDirectoryChanged(bool value)
    {
        if (value) ExtractionOutputToDirectory = false;
        _settingsManager.Current.ExtractionOutputToSameDirectory = value;
    }

    partial void OnExtractionOutputToDirectoryChanged(bool value)
    {
        if (value) ExtractionOutputToSameDirectory = false;
        _settingsManager.Current.ExtractionOutputToSameDirectory = !value;
    }

    partial void OnCompressionOutputToSameDirectoryChanged(bool value)
    {
        if (value) CompressionOutputToDirectory = false;
        _settingsManager.Current.CompressionOutputToSameDirectory = value;
    }

    partial void OnCompressionOutputToDirectoryChanged(bool value)
    {
        if (value) CompressionOutputToSameDirectory = false;
        _settingsManager.Current.CompressionOutputToSameDirectory = !value;
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
    private void Save()
    {
        try
        {
            _settingsManager.Current.Theme = SelectedTheme;
            _settingsManager.Current.CompressionFormat = SelectedCompressionFormat ?? "ZIP";
            _settingsManager.Current.ExtractionOutputDirectory = ExtractionOutputDirectory;
            _settingsManager.Current.CompressionOutputDirectory = CompressionOutputDirectory;
            _settingsManager.Current.ExtractionOutputToSameDirectory = ExtractionOutputToSameDirectory;
            _settingsManager.Current.CompressionOutputToSameDirectory = CompressionOutputToSameDirectory;
            _settingsManager.Current.OpenExtractionOutputFolder = OpenExtractionOutputFolder;
            _settingsManager.Current.OpenCompressionOutputFolder = OpenCompressionOutputFolder;
            _settingsManager.Current.CompressMultipleAsOne = CompressMultipleAsOne;
            _settingsManager.Current.ZipCompressionLevel = ZipCompressionLevel;
            _settingsManager.Current.SevenZipCompressionLevel = SevenZipCompressionLevel;
            _settingsManager.Save();
            ApplyAssociationSettings();
            _closeWindow();
        }
        catch (Exception ex)
        {
            _ = MessageService.ShowException("設定の保存に失敗しました", ex);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _closeWindow();
    }

    [RelayCommand]
    private void CreateShortcut()
    {
        try
        {
            if (ShortcutCreator.CreateDesktopShortcut())
                _ = MessageService.ShowSuccess("デスクトップにショートカットを作成しました。");
            else
                _ = MessageService.ShowError("ショートカットの作成に失敗しました。");
        }
        catch (Exception ex)
        {
            _ = MessageService.ShowException("ショートカットの作成中にエラーが発生しました", ex);
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
            _ = MessageService.ShowException("全選択処理でエラーが発生しました", ex);
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
            _ = MessageService.ShowException("全解除処理でエラーが発生しました", ex);
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
            var filesToExtract = new List<string>();
            var filesToCompress = new List<string>();
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                    filesToCompress.Add(path);
                else if (File.Exists(path))
                {
                    if (ArchiveExtractor.IsSupportedArchiveType(path))
                        filesToExtract.Add(path);
                    else
                        filesToCompress.Add(path);
                }
            }
            if (filesToExtract.Count == 0 && filesToCompress.Count == 0) return;
            progressWindow = new ProgressWindow("処理中") { WindowStartupLocation = WindowStartupLocation.CenterOwner };
            _showProgressWindow(progressWindow);
            await Task.Yield();
            var cancellationToken = progressWindow.GetCancellationToken();
            var settings = _settingsManager.Current;
            var hasCompression = filesToCompress.Count > 0;
            var hasExtraction = filesToExtract.Count > 0;
            if (hasCompression)
            {
                if (settings.CompressMultipleAsOne && filesToCompress.Count > 1)
                {
                    // 複数ファイル・フォルダを1つのアーカイブにまとめて圧縮
                    await ArchiveProcessor.CompressMergedAsync(
                        filesToCompress.ToArray(),
                        settings.CompressionOutputDirectory,
                        settings.CompressionOutputToSameDirectory,
                        settings.CompressionFormat,
                        progressWindow,
                        cancellationToken,
                        closeWindowOnCompletion: !hasExtraction);
                }
                else
                {
                    // 個別に圧縮
                    await ArchiveProcessor.CompressItemsAsync(
                        filesToCompress.ToArray(),
                        settings.CompressionOutputDirectory,
                        settings.CompressionOutputToSameDirectory,
                        settings.CompressionFormat,
                        progressWindow,
                        cancellationToken,
                        closeWindowOnCompletion: !hasExtraction);
                }
            }
            if (cancellationToken.IsCancellationRequested) return;
            if (hasExtraction)
            {
                var extractionResults = await ArchiveProcessor.ExtractArchivesAsync(
                    filesToExtract.ToArray(),
                    settings.ExtractionOutputDirectory,
                    settings.ExtractionOutputToSameDirectory,
                    progressWindow,
                    cancellationToken,
                    closeWindowOnCompletion: true);
                if (extractionResults.Count > 0 && settings.OpenExtractionOutputFolder)
                    OpenExtractedFolders(extractionResults);
            }
            else if (hasCompression && settings.OpenCompressionOutputFolder && !settings.CompressionOutputToSameDirectory)
            {
                FolderOpener.OpenFolder(settings.CompressionOutputDirectory);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Log("処理がキャンセルされました");
            progressWindow?.SetCompleted("キャンセルしました。");
            progressWindow?.CloseSafe();
        }
        catch (Exception ex)
        {
            Logger.LogException("ファイルの処理に失敗しました", ex);
            _ = MessageService.ShowException("ファイルの処理に失敗しました", ex);
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
        foreach (var item in Associations)
            item.IsAssociated = isChecked;
    }

    private void ApplyAssociationSettings()
    {
        try
        {
            Logger.Log("関連付け設定の適用を開始");
            foreach (var item in Associations)
            {
                var isCurrentlyAssociated = FileAssociation.IsFileTypeAssociated(item.Extension);
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
            _ = MessageService.ShowException("関連付け設定の適用に失敗しました", ex);
        }
    }

    private void LoadVersionInfo()
    {
        try
        {
            var assembly = typeof(MainWindowViewModel).Assembly;
            var rawVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
            VersionText = rawVersion.Contains('+') ? rawVersion.Split('+')[0] : rawVersion;
            CopyrightText = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "Copyright © 2025-2026 ゆろち";
            LicenseText = @"MIT License

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
            Logger.Log($"バージョン情報を読み込みました: Version {VersionText}");
        }
        catch (Exception ex)
        {
            Logger.LogException("バージョン情報の読み込みでエラーが発生", ex);
            VersionText = "不明";
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
                    UpdateStatusText = "更新を適用中...再起動します。";
                    // ApplyUpdatesAndRestart でアプリを再起動 → 通常起動フローでメイン画面が表示される
                    result.Manager.ApplyUpdatesAndRestart(result.Info);
                    break;

                case UpdateChecker.UpdateResult.NoUpdate:
                    UpdateStatusText = "✅ 最新バージョンです。";
                    break;

                case UpdateChecker.UpdateResult.NotInstalled:
                    UpdateStatusText = "⚠ 開発環境では更新チェックできません。";
                    break;

                case UpdateChecker.UpdateResult.NotConfigured:
                    UpdateStatusText = "⚠ 更新元リポジトリが未設定です。";
                    break;

                default:
                    UpdateStatusText = $"⚠ {result.Message}";
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("更新チェックコマンドでエラーが発生", ex);
            UpdateStatusText = "⚠ 更新チェック中にエラーが発生しました。";
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    private void OpenExtractedFolders(IEnumerable<(string SourcePath, string OutputPath, ArchiveExtractor.ArchiveStructureInfo StructureInfo)> extractionResults)
    {
        var openedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in extractionResults)
        {
            var targetPath = result.OutputPath;
            try
            {
                // アーカイブの構造情報を再利用して、開くべきフォルダを決定
                var structureInfo = result.StructureInfo;
                if (structureInfo.HasSingleRootItem && !string.IsNullOrEmpty(structureInfo.SingleRootItemName))
                {
                    // 単一ルート要素の場合は、そのフォルダを開く
                    var possibleDir = Path.Combine(targetPath, structureInfo.SingleRootItemName);
                    if (Directory.Exists(possibleDir))
                        targetPath = possibleDir;
                }

                if (Directory.Exists(targetPath) && openedPaths.Add(targetPath))
                    FolderOpener.OpenFolder(targetPath);
            }
            catch (Exception ex)
            {
                Logger.LogException($"展開先フォルダを開く処理でエラー: {result.SourcePath}", ex);
            }
        }
    }
}
