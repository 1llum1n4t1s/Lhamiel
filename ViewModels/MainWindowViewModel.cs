using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lhamiel.Util;
using Lhamiel.View;

namespace Lhamiel.ViewModels;

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
    private bool _zipIsChecked;

    [ObservableProperty]
    private bool _sevenZipIsChecked;

    [ObservableProperty]
    private bool _tarIsChecked;

    [ObservableProperty]
    private bool _gzIsChecked;

    [ObservableProperty]
    private bool _bz2IsChecked;

    [ObservableProperty]
    private bool _lzmaIsChecked;

    [ObservableProperty]
    private bool _xzIsChecked;

    [ObservableProperty]
    private bool _rarIsChecked;

    [ObservableProperty]
    private bool _lzhIsChecked;

    [ObservableProperty]
    private bool _cabIsChecked;

    [ObservableProperty]
    private bool _arjIsChecked;

    [ObservableProperty]
    private bool _zIsChecked;

    [ObservableProperty]
    private bool _tgzIsChecked;

    [ObservableProperty]
    private bool _tbz2IsChecked;

    [ObservableProperty]
    private bool _tbzIsChecked;

    [ObservableProperty]
    private bool _tlzIsChecked;

    [ObservableProperty]
    private bool _txzIsChecked;

    [ObservableProperty]
    private bool _tzIsChecked;

    [ObservableProperty]
    private string _versionText = string.Empty;

    [ObservableProperty]
    private string _copyrightText = string.Empty;

    [ObservableProperty]
    private string _licenseText = string.Empty;

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
    }

    /// <summary>
    /// 設定から View に読み込む
    /// </summary>
    public void LoadFromSettings()
    {
        var s = _settingsManager.Current;
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

    partial void OnSelectedCompressionFormatChanged(string value)
    {
        _settingsManager.Current.CompressionFormat = value ?? "ZIP";
        _settingsManager.Save();
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
            _settingsManager.Current.CompressionFormat = SelectedCompressionFormat ?? "ZIP";
            _settingsManager.Current.ExtractionOutputDirectory = ExtractionOutputDirectory;
            _settingsManager.Current.CompressionOutputDirectory = CompressionOutputDirectory;
            _settingsManager.Current.ExtractionOutputToSameDirectory = ExtractionOutputToSameDirectory;
            _settingsManager.Current.CompressionOutputToSameDirectory = CompressionOutputToSameDirectory;
            _settingsManager.Current.OpenExtractionOutputFolder = OpenExtractionOutputFolder;
            _settingsManager.Current.OpenCompressionOutputFolder = OpenCompressionOutputFolder;
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
        if (Avalonia.Application.Current is App { IsUpdateRestarting: true })
        {
            Logger.Log("アップデートのための再起動が予定されているため、新しい処理をスキップします。");
            _ = MessageService.ShowWarning("アップデートの適用準備が整いました。再起動後に再度お試しください。");
            return;
        }
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
                await ArchiveProcessor.CompressItemsAsync(
                    filesToCompress.ToArray(),
                    settings.CompressionOutputDirectory,
                    settings.CompressionOutputToSameDirectory,
                    settings.CompressionFormat,
                    progressWindow,
                    cancellationToken,
                    closeWindowOnCompletion: !hasExtraction);
            }
            if (cancellationToken.IsCancellationRequested) return;
            if (hasExtraction)
            {
                var success = await ArchiveProcessor.ExtractArchivesAsync(
                    filesToExtract.ToArray(),
                    settings.ExtractionOutputDirectory,
                    settings.ExtractionOutputToSameDirectory,
                    progressWindow,
                    cancellationToken,
                    closeWindowOnCompletion: true);
                if (success && settings.OpenExtractionOutputFolder)
                    OpenExtractedFolders(filesToExtract, settings.ExtractionOutputDirectory, settings.ExtractionOutputToSameDirectory);
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

    private void LoadAssociationStatus()
    {
        try
        {
            var status = FileAssociation.GetCurrentAssociationStatus();
            ZipIsChecked = status.GetValueOrDefault("zip", false);
            SevenZipIsChecked = status.GetValueOrDefault("7z", false);
            TarIsChecked = status.GetValueOrDefault("tar", false);
            GzIsChecked = status.GetValueOrDefault("gz", false);
            Bz2IsChecked = status.GetValueOrDefault("bz2", false);
            LzmaIsChecked = status.GetValueOrDefault("lzma", false);
            XzIsChecked = status.GetValueOrDefault("xz", false);
            RarIsChecked = status.GetValueOrDefault("rar", false);
            LzhIsChecked = status.GetValueOrDefault("lzh", false);
            CabIsChecked = status.GetValueOrDefault("cab", false);
            ArjIsChecked = status.GetValueOrDefault("arj", false);
            ZIsChecked = status.GetValueOrDefault("z", false);
            TgzIsChecked = status.GetValueOrDefault("tgz", false);
            Tbz2IsChecked = status.GetValueOrDefault("tbz2", false);
            TbzIsChecked = status.GetValueOrDefault("tbz", false);
            TlzIsChecked = status.GetValueOrDefault("tlz", false);
            TxzIsChecked = status.GetValueOrDefault("txz", false);
            TzIsChecked = status.GetValueOrDefault("tz", false);
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
        ZipIsChecked = SevenZipIsChecked = TarIsChecked = GzIsChecked = Bz2IsChecked = LzmaIsChecked = XzIsChecked = isChecked;
        RarIsChecked = LzhIsChecked = CabIsChecked = ArjIsChecked = ZIsChecked = isChecked;
        TgzIsChecked = Tbz2IsChecked = TbzIsChecked = TlzIsChecked = TxzIsChecked = TzIsChecked = isChecked;
    }

    private void ApplyAssociationSettings()
    {
        try
        {
            Logger.Log("関連付け設定の適用を開始");
            var keys = new[] { "zip", "7z", "tar", "gz", "bz2", "lzma", "xz", "rar", "lzh", "cab", "arj", "z", "tgz", "tbz2", "tbz", "tlz", "txz", "tz" };
            var shouldAssociate = new[] { ZipIsChecked, SevenZipIsChecked, TarIsChecked, GzIsChecked, Bz2IsChecked, LzmaIsChecked, XzIsChecked, RarIsChecked, LzhIsChecked, CabIsChecked, ArjIsChecked, ZIsChecked, TgzIsChecked, Tbz2IsChecked, TbzIsChecked, TlzIsChecked, TxzIsChecked, TzIsChecked };
            for (var i = 0; i < keys.Length; i++)
            {
                var extension = keys[i];
                var should = shouldAssociate[i];
                var isCurrentlyAssociated = FileAssociation.IsFileTypeAssociated(extension);
                if (should && !isCurrentlyAssociated)
                {
                    if (FileAssociation.AssociateFileType(extension))
                        Logger.Log($"関連付け設定成功: {extension}");
                    else
                        Logger.Log($"関連付け設定失敗: {extension}", LogLevel.Warning);
                }
                else if (!should && isCurrentlyAssociated)
                {
                    if (FileAssociation.DisassociateFileType(extension))
                        Logger.Log($"関連付け解除成功: {extension}");
                    else
                        Logger.Log($"関連付け解除失敗: {extension}", LogLevel.Warning);
                }
            }
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
            var assembly = Assembly.GetExecutingAssembly();
            var informationalVersionAttribute = assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .FirstOrDefault() as AssemblyInformationalVersionAttribute;
            var rawVersion = informationalVersionAttribute?.InformationalVersion ?? "1.0.0";
            VersionText = rawVersion.Contains('+') ? rawVersion.Split('+')[0] : rawVersion;
            var copyrightAttribute = assembly.GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false)
                .FirstOrDefault() as AssemblyCopyrightAttribute;
            CopyrightText = copyrightAttribute?.Copyright ?? "Copyright © 2025-2026 ゆろち";
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

    private void OpenExtractedFolders(IEnumerable<string> archivePaths, string outputDir, bool outputToSameDirectory)
    {
        var openedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var archivePath in archivePaths)
        {
            var baseDir = ArchiveExtractor.GetBaseOutputDirectory(archivePath, outputDir, outputToSameDirectory);
            var targetPath = baseDir;
            try
            {
                var rootItemName = ArchiveExtractor.GetSingleRootItemName(archivePath);
                if (!string.IsNullOrEmpty(rootItemName))
                {
                    var possibleDir = Path.Combine(baseDir, rootItemName);
                    if (Directory.Exists(possibleDir))
                        targetPath = possibleDir;
                }
                else
                {
                    var fileName = Path.GetFileNameWithoutExtension(archivePath);
                    var possibleDir = Path.Combine(baseDir, fileName);
                    if (Directory.Exists(possibleDir))
                        targetPath = possibleDir;
                }
                if (Directory.Exists(targetPath) && openedPaths.Add(targetPath))
                    FolderOpener.OpenFolder(targetPath);
            }
            catch (Exception ex)
            {
                Logger.LogException($"展開先フォルダを開く処理でエラー: {archivePath}", ex);
            }
        }
    }
}
