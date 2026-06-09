using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
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

    // PasswordModeRadioSyncCallback は CodeRabbit #3381138457 を受けて廃止し、
    // MainWindow 側で INotifyPropertyChanged を購読する一般化された方式へ移行した。
    private bool _isLoading;
    private CancellationTokenSource? _autoSaveCts;

    [ObservableProperty]
    private string _selectedTheme = "System";

    [ObservableProperty]
    private string _extractionOutputDirectory = string.Empty;

    [ObservableProperty]
    private string _compressionOutputDirectory = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsZipFormat))]
    [NotifyPropertyChangedFor(nameof(IsSevenZipFormat))]
    [NotifyPropertyChangedFor(nameof(IsTarFormat))]
    [NotifyPropertyChangedFor(nameof(IsZipOrSevenZipFormat))]
    [NotifyPropertyChangedFor(nameof(IsPasswordSubPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsZipFormatAndPasswordOn))]
    [NotifyPropertyChangedFor(nameof(ShowZipExplorerWarning))]
    [NotifyPropertyChangedFor(nameof(EncryptFileNamesEnabled))]
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
    private bool _includeHiddenAndSystemEntries = true;

    [ObservableProperty]
    private bool _respectNestedGitignore;

    // ──────────────────────────────────────────────
    // パスワード保護 (v1.0.181+)
    // ──────────────────────────────────────────────

    /// <summary>
    /// パスワード保護を有効化するかどうか。OFF→ON 遷移時に <see cref="EncryptFileNames"/> を
    /// true にリセットする（decision #4: 「パスワード ON のたび強制 true」）。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPasswordSubPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsZipFormatAndPasswordOn))]
    [NotifyPropertyChangedFor(nameof(ShowZipExplorerWarning))]
    [NotifyPropertyChangedFor(nameof(IsRememberModeActive))]
    private bool _isPasswordProtectionEnabled;

    /// <summary>
    /// 7z アーカイブ内のファイル名（ヘッダ）も暗号化するか。
    /// 永続化しない（decision #4: パスワード ON のたびに毎回 true で初期化）。
    /// ZIP では仕様上不可能なので UI で disabled になる。
    /// </summary>
    [ObservableProperty]
    private bool _encryptFileNames = true;

    /// <summary>
    /// パスワード入力モード（"PromptEachTime" or "Remember"）。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRememberModeActive))]
    private string _passwordMode = "PromptEachTime";

    /// <summary>ドロップ中のパスワード入力ダイアログを同時に複数起動しないための排他ガード（0=空き / 1=入力中）。</summary>
    private int _isAwaitingPasswordInput;

    // === 派生プロパティ (XAML compiled binding 用) ===

    public bool IsZipFormat => string.Equals(SelectedCompressionFormat, "ZIP", StringComparison.OrdinalIgnoreCase);
    public bool IsSevenZipFormat => string.Equals(SelectedCompressionFormat, "7z", StringComparison.OrdinalIgnoreCase);
    public bool IsTarFormat => string.Equals(SelectedCompressionFormat, "TAR", StringComparison.OrdinalIgnoreCase);
    public bool IsZipOrSevenZipFormat => IsZipFormat || IsSevenZipFormat;
    public bool IsPasswordSubPanelVisible => IsPasswordProtectionEnabled && IsZipOrSevenZipFormat;
    public bool IsZipFormatAndPasswordOn => IsZipFormat && IsPasswordProtectionEnabled;
    public bool ShowZipExplorerWarning => IsZipFormatAndPasswordOn;
    /// <summary>EncryptFileNames チェックボックスを有効にする条件: format=7z（ZIP では仕様上不可能）。</summary>
    public bool EncryptFileNamesEnabled => IsSevenZipFormat;
    public bool IsRememberModeActive => IsPasswordProtectionEnabled
                                        && string.Equals(PasswordMode, "Remember", StringComparison.Ordinal);

    /// <summary>保存済みパスワード ciphertext があるかどうか（UI に「設定済 / 未設定」を表示するため）。</summary>
    public bool HasSavedPassword =>
        SettingsManager.Instance.Current.EncryptedCompressionPassword is { Length: > 0 };

    /// <summary>「設定済 / 未設定」のローカライズ済み表示テキスト。</summary>
    public string SavedPasswordStatusText => HasSavedPassword
        ? App.Text("Settings.Compression.SavedPasswordStatus.Set")
        : App.Text("Settings.Compression.SavedPasswordStatus.NotSet");

    /// <summary>
    /// MainWindow から drop ハンドリングする際に呼ぶ排他取得。同時 drop を 1 件目だけ処理し、2 件目以降は false で弾く。
    /// 戻り値の <see cref="IDisposable.Dispose"/> でガード解放。
    /// </summary>
    internal IDisposable? TryBeginAwaitingPasswordInput()
    {
        if (System.Threading.Interlocked.CompareExchange(ref _isAwaitingPasswordInput, 1, 0) != 0)
            return null;
        return new AwaitingPasswordInputGuard(this);
    }

    private sealed class AwaitingPasswordInputGuard(MainWindowViewModel vm) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _released, 1) == 0)
                System.Threading.Interlocked.Exchange(ref vm._isAwaitingPasswordInput, 0);
        }
    }

    /// <summary>
    /// メイン画面起動時に Velopack 自動更新チェックを走らせるかどうかの UI バインディング。
    /// 「全般」タブのチェックボックスから ON/OFF を切り替える。
    /// </summary>
    [ObservableProperty]
    private bool _check4UpdatesOnStartup = true;

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

    [ObservableProperty]
    private string _newExcludedFilePattern = string.Empty;

    [ObservableProperty]
    private string? _selectedExcludedFilePattern;

    /// <summary>
    /// 設定値を 300ms デバウンス後に保存する（ロード中は抑制）。
    /// 連続する UI 操作（スライダー等）のディスク I/O を束ねる。
    /// </summary>
    private void AutoSave()
    {
        if (_isLoading) return;
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;
        _ = ExecuteAutoSaveAsync(token);
    }

    /// <summary>
    /// 保留中の debounce 付き自動保存をキャンセルして即時保存する。
    /// アプリ終了時に呼び出して設定ロストを防ぐ。
    /// </summary>
    internal void FlushPendingAutoSave()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = null;
        try
        {
            ApplySettingsToManager();
            _settingsManager.Save();
        }
        catch (Exception ex)
        {
            Logger.LogException("終了時の設定フラッシュに失敗", ex);
        }
    }

    private void ApplySettingsToManager()
    {
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
            s.IncludeHiddenAndSystemEntries = IncludeHiddenAndSystemEntries;
            s.RespectNestedGitignore = RespectNestedGitignore;
            s.Check4UpdatesOnStartup = Check4UpdatesOnStartup;
            s.DirectoryStructureMode = (DirectoryStructureMode)SelectedDirectoryStructureMode;
            s.ZipCompressionLevel = ZipCompressionLevel;
            s.SevenZipCompressionLevel = SevenZipCompressionLevel;
            // パスワード保護: 永続化するのは ON/OFF と Mode のみ。
            // EncryptFileNames は永続化せず（パスワード ON のたびに true 強制リセット、decision #4）。
            // EncryptedCompressionPassword は ArchiveProcessor 側で MutateAndSave 経由で更新する。
            s.IsPasswordProtectionEnabled = IsPasswordProtectionEnabled;
            s.PasswordMode = PasswordMode;
            // 除外パターンは .lhaignore ファイルが真の源なので、settings.json には書き出さない。
        });
    }

    private async Task ExecuteAutoSaveAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token).ConfigureAwait(false);
            ApplySettingsToManager();
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
        // 保存済みパスワード状態ラベル ("設定済み" / "未設定 (次回...)") も App.Text() ベースなので
        // ロケール変更で再評価が必要 (CodeRabbit outside-diff、MainWindowViewModel.cs:292-300)。
        OnPropertyChanged(nameof(SavedPasswordStatusText));

        AutoSave();
    }

    partial void OnSelectedCompressionFormatChanged(string value) => AutoSave();

    partial void OnExtractionOutputDirectoryChanged(string value) => AutoSave();

    partial void OnCompressionOutputDirectoryChanged(string value) => AutoSave();

    partial void OnOpenExtractionOutputFolderChanged(bool value) => AutoSave();

    partial void OnCreateArchiveNameFolderChanged(bool value) => AutoSave();

    partial void OnOpenCompressionOutputFolderChanged(bool value) => AutoSave();

    partial void OnCompressMultipleAsOneChanged(bool value) => AutoSave();

    partial void OnIncludeHiddenAndSystemEntriesChanged(bool value) => AutoSave();
    partial void OnRespectNestedGitignoreChanged(bool value) => AutoSave();

    // ──────────────────────────────────────────────
    // パスワード保護のハンドラ + コマンド
    // ──────────────────────────────────────────────

    private bool _suppressPasswordModeWipe;

    /// <summary>
    /// IsPasswordProtectionEnabled の値変化ハンドラ。
    /// false → true 遷移で EncryptFileNames を true に強制リセット（decision #4）。
    /// </summary>
    partial void OnIsPasswordProtectionEnabledChanged(bool value)
    {
        if (value)
        {
            // パスワード ON にしたら毎回ファイル名暗号化も ON（decision #4）。
            EncryptFileNames = true;
        }
        AutoSave();
    }

    partial void OnEncryptFileNamesChanged(bool value)
    {
        // 永続化しないので AutoSave は呼ばない（実行時のみの選択値）。
        // 派生 UI への通知は [ObservableProperty] が自動発行する。
    }

    partial void OnPasswordModeChanged(string value)
    {
        // 抑制フラグ ON（UI ロールバック中）は無視
        if (_suppressPasswordModeWipe)
        {
            AutoSave();
            return;
        }

        // Remember → PromptEachTime 遷移 + 保存済みパスワードあり: ConfirmDialog で確認
        if (string.Equals(value, "PromptEachTime", StringComparison.Ordinal)
            && SettingsManager.Instance.Current.EncryptedCompressionPassword is { Length: > 0 })
        {
            _ = HandlePromptEachTimeTransitionAsync();
            return;
        }

        AutoSave();
    }

    private async Task HandlePromptEachTimeTransitionAsync()
    {
        var owner = GetMainWindowSafe();
        if (owner is null)
        {
            // MainWindow が無い経路（起動直後の race など）は安全側に倒して AutoSave のみ。
            AutoSave();
            return;
        }
        var confirmed = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new ConfirmDialog(
                App.Text("Confirm.WipeSavedPassword.Message"),
                App.Text("Confirm.WipeSavedPassword.Title"));
            return await dialog.ShowDialog<bool>(owner);
        });

        if (confirmed)
        {
            // 1 トランザクションで mode + ciphertext を更新（中間状態を作らない、critique security #3）
            SettingsManager.Instance.MutateAndSave(s =>
            {
                s.PasswordMode = "PromptEachTime";
                s.EncryptedCompressionPassword = null;
            });
            OnPropertyChanged(nameof(HasSavedPassword));
            OnPropertyChanged(nameof(SavedPasswordStatusText));
        }
        else
        {
            // RadioButton を Remember に戻す（_suppressPasswordModeWipe で再帰防止）
            // PasswordMode セッターは ObservableProperty が PropertyChanged を発火するので、
            // MainWindow 側で購読している InitPasswordModeRadioButtons が UI 上の radio も Remember に戻す
            // (CodeRabbit #3381138457: PropertyChanged 購読方式に統一)。
            _suppressPasswordModeWipe = true;
            try { PasswordMode = "Remember"; }
            finally { _suppressPasswordModeWipe = false; }
        }
    }

    /// <summary>保存済みパスワードを削除する（確認ダイアログあり）。</summary>
    [RelayCommand]
    private async Task ClearSavedPasswordAsync()
    {
        if (!HasSavedPassword) return;
        var owner = GetMainWindowSafe();
        if (owner is null) return;
        var confirmed = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new ConfirmDialog(
                App.Text("Confirm.ClearSavedPassword.Message"),
                App.Text("Confirm.ClearSavedPassword.Title"));
            return await dialog.ShowDialog<bool>(owner);
        });
        if (!confirmed) return;

        SettingsManager.Instance.MutateAndSave(s => s.EncryptedCompressionPassword = null);
        OnPropertyChanged(nameof(HasSavedPassword));
        OnPropertyChanged(nameof(SavedPasswordStatusText));
    }

    /// <summary>保存済みパスワードを変更する（PasswordDialog で新規入力）。</summary>
    [RelayCommand]
    private async Task ChangeSavedPasswordAsync()
    {
        var owner = GetMainWindowSafe();
        var newPassword = await ArchiveProcessor.PasswordDialogImpl.PromptForPasswordAsync(
            archiveName: App.Text("Settings.Compression.ChangeSavedPassword"),
            mode: PasswordDialogMode.CompressNew,
            isRetry: false,
            parentWindow: owner,
            cancellationToken: CancellationToken.None);

        if (newPassword is null) return;

        try
        {
            // 平文を扱う try スコープでログ redaction を掛ける (CodeRabbit #3381138482)。
            // Protect 内部やこの try の catch で平文混入経路があってもマスクされる。
            using var _ = Logger.RegisterRedactionToken(newPassword);
            var ciphertext = CompressionPasswordSession.Protect(newPassword);
            SettingsManager.Instance.MutateAndSave(s => s.EncryptedCompressionPassword = ciphertext);
            OnPropertyChanged(nameof(HasSavedPassword));
            OnPropertyChanged(nameof(SavedPasswordStatusText));
        }
        catch (Exception ex)
        {
            Logger.LogException("圧縮パスワードの保存に失敗", ex);
        }
    }

    /// <summary>アクティブな MainWindow を取得する（取れない場合は null）。</summary>
    private static Window? GetMainWindowSafe()
    {
        try
        {
            return (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        }
        catch { return null; }
    }

    partial void OnCheck4UpdatesOnStartupChanged(bool value) => AutoSave();

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
    /// 更新チェック中かどうか（ボタン無効化バインド用）
    /// </summary>
    [ObservableProperty]
    private bool _isCheckingUpdate;

    /// <summary>
    /// スキップ中の更新タグ（"v1.0.166" 等）。空文字列はスキップなしを示す。
    /// バージョンタブの「スキップを取り消す」UI バインド用。Settings.IgnoreUpdateTag のミラー。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIgnoredUpdateTag))]
    [NotifyPropertyChangedFor(nameof(IgnoredUpdateTagDisplay))]
    private string _ignoredUpdateTag = string.Empty;

    /// <summary>「スキップ取り消し」UI 表示の可否（バインド用 derived プロパティ）。</summary>
    public bool HasIgnoredUpdateTag => !string.IsNullOrEmpty(IgnoredUpdateTag);

    /// <summary>「現在 vX.Y.Z をスキップ中」の表示文字列（ローカライズフォーマット適用済み）。</summary>
    public string IgnoredUpdateTagDisplay =>
        string.IsNullOrEmpty(IgnoredUpdateTag)
            ? string.Empty
            : App.Text("Settings.Version.SkippedVersion", IgnoredUpdateTag);

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
    /// 圧縮時に除外するファイル・フォルダ名の一覧
    /// </summary>
    public ObservableCollection<string> CompressionExcludedFilePatterns { get; } = [];

    // .lhaignore ファイルを外部エディタで編集した場合に UI に反映するための監視。
    // VM はアプリ全体で 1 インスタンスのため Dispose せず leak させる前提。
    private FileSystemWatcher? _lhaignoreWatcher;
    private System.Threading.Timer? _lhaignoreReloadDebounce;

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
        InitializeLhaignoreWatcher();

        // 更新チェックの進行状態を購読してアップデート確認ボタンの IsEnabled を駆動する。
        // 起動時自動チェックも反映されるため、auto check 中はボタンが押せない（並走実行を未然に防止）。
        // ※ MainWindowViewModel はアプリ全体で 1 インスタンスなので unsubscribe しなくてもリークしない。
        App.UpdateCheckStateChanged += OnAppUpdateCheckStateChanged;
        IsCheckingUpdate = App.IsUpdateCheckInProgress;

        // 初期選択状態を設定
        OnZipCompressionLevelChanged(ZipCompressionLevel);
        OnSevenZipCompressionLevelChanged(SevenZipCompressionLevel);
        _isLoading = false;
    }

    /// <summary>
    /// .lhaignore ファイルが外部エディタで編集された場合に UI を再ロードするための watcher を起動する。
    /// テキストエディタは保存時に複数 Change イベントを発火しがちなので 250ms デバウンスする。
    /// </summary>
    private void InitializeLhaignoreWatcher()
    {
        try
        {
            var dir = Path.GetDirectoryName(LhaignoreFile.FilePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return;

            _lhaignoreWatcher = new FileSystemWatcher(dir, Path.GetFileName(LhaignoreFile.FilePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true,
                // RTK レビュー #C2-004 対応: 既定 8KB だと %LocalAppData%\Lhamiel 直下の他ファイル
                // (settings.json AutoSave / Lhamiel_yyyyMMdd.log ローテーション / dumps/) の
                // 書込みイベントでバッファ溢れて InternalBufferOverflowException → .lhaignore 変更を
                // silent に取りこぼす経路がある。64KB に拡大して non-paged pool 消費は許容範囲に収める。
                InternalBufferSize = 64 * 1024,
            };
            _lhaignoreWatcher.Changed += OnLhaignoreChanged;
            _lhaignoreWatcher.Created += OnLhaignoreChanged;
            _lhaignoreWatcher.Renamed += OnLhaignoreChanged;
            // バッファ overflow など Watcher 内部エラーをサイレントに握り潰さずログに残す。
            // CodeRabbit 指摘対応 (#3305116091): InternalBufferOverflowException 等で
            // イベント取りこぼしが発生すると CompressionExcludedFilePatterns が stale になるため、
            // 再読み込み debounce を発火して resync を予約する（ログ出力のみだと UI が古いまま残る）。
            _lhaignoreWatcher.Error += (_, e) =>
            {
                try { Logger.LogException(".lhaignore 監視で内部エラー発生 (Watcher を再初期化推奨)", e.GetException()); }
                catch { /* Logger 未初期化のケース */ }
                // イベント取りこぼし時の再同期を debounce 経由でスケジュール
                try { _lhaignoreReloadDebounce?.Change(250, System.Threading.Timeout.Infinite); }
                catch { /* timer disposed */ }
            };
            _lhaignoreReloadDebounce = new System.Threading.Timer(_ =>
            {
                Dispatcher.UIThread.Post(ReloadExcludedFilePatternsFromFile);
            }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        }
        catch (Exception ex)
        {
            Logger.Log($".lhaignore 監視の初期化に失敗: {ex.Message}", LogLevel.Warning);
        }
    }

    private void OnLhaignoreChanged(object sender, FileSystemEventArgs e)
    {
        // 250ms デバウンス（エディタが保存中に複数イベントを撃つので）
        _lhaignoreReloadDebounce?.Change(250, System.Threading.Timeout.Infinite);
    }

    /// <summary>App._isCheckingUpdate 遷移を UI スレッドに marshal して IsCheckingUpdate に反映する。</summary>
    private void OnAppUpdateCheckStateChanged(bool inProgress)
    {
        Dispatcher.UIThread.Post(() => IsCheckingUpdate = inProgress);
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
        IncludeHiddenAndSystemEntries = s.IncludeHiddenAndSystemEntries;
        RespectNestedGitignore = s.RespectNestedGitignore;
        Check4UpdatesOnStartup = s.Check4UpdatesOnStartup;
        IgnoredUpdateTag = s.IgnoreUpdateTag ?? string.Empty;
        SelectedDirectoryStructureMode = (int)s.DirectoryStructureMode;
        SelectedLocale = string.IsNullOrEmpty(s.Locale) ? App.DetectDefaultLocale() : s.Locale;
        ZipCompressionLevel = s.ZipCompressionLevel;
        SevenZipCompressionLevel = s.SevenZipCompressionLevel;
        // パスワード保護: 永続値から復元。EncryptFileNames は毎回 true で初期化（decision #4）。
        IsPasswordProtectionEnabled = s.IsPasswordProtectionEnabled;
        PasswordMode = string.Equals(s.PasswordMode, "Remember", StringComparison.Ordinal)
            ? "Remember" : "PromptEachTime";
        EncryptFileNames = true;
        // 派生プロパティ通知（HasSavedPassword は SettingsManager 直読みのため AutoSave に反応しない手動 OnPropertyChanged）
        OnPropertyChanged(nameof(HasSavedPassword));
        OnPropertyChanged(nameof(SavedPasswordStatusText));
        // 除外パターンは settings.json ではなく .lhaignore から読み込む。
        ReloadExcludedFilePatternsFromFile();
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
    private void AddExcludedPattern()
    {
        var pattern = NewExcludedFilePattern.Trim();
        if (pattern.Length == 0)
            return;

        LhaignoreFile.AppendPattern(pattern);
        ReloadExcludedFilePatternsFromFile();
        SelectedExcludedFilePattern = CompressionExcludedFilePatterns
            .FirstOrDefault(p => string.Equals(p, pattern, StringComparison.OrdinalIgnoreCase));
        NewExcludedFilePattern = string.Empty;
    }

    [RelayCommand]
    private void RemoveExcludedPattern()
    {
        if (SelectedExcludedFilePattern is null)
            return;

        LhaignoreFile.RemovePattern(SelectedExcludedFilePattern);
        ReloadExcludedFilePatternsFromFile();
        SelectedExcludedFilePattern = null;
    }

    [RelayCommand]
    private void ResetExcludedPatterns()
    {
        LhaignoreFile.ResetToDefaults();
        ReloadExcludedFilePatternsFromFile();
    }


    /// <summary>
    /// .lhaignore ファイルを既定のテキストエディタで開く。
    /// 関連付けが無い場合は notepad.exe にフォールバックする。
    /// 編集後の変更は <see cref="_lhaignoreWatcher"/> がピックアップして UI を更新する。
    /// </summary>
    [RelayCommand]
    private async Task OpenExcludedPatternsFile()
    {
        try
        {
            // 開いた瞬間にファイルが存在することを保証する（初回起動でユーザーが先に押した場合の保険）
            LhaignoreFile.EnsureExists();
            var path = LhaignoreFile.FilePath;

            // Issue #54 対策: Process.Start(UseShellExecute=true) を UI スレッドから直接呼ぶと、
            // ShellExecuteEx の内部処理 (シェル拡張初期化・関連付け解決等) が UI スレッドを
            // blocking して操作不能に見える経路がある。Task.Run で別スレッドへ逃がす。
            await Task.Run(() =>
            {
                try
                {
                    using var _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch
                {
                    // .lhaignore に関連付けが無い環境では notepad で開く
                    // ⚠️ セキュリティ: "notepad.exe" 単独だと PATH 環境変数経由で悪意あるバイナリを掴むリスクがある
                    // (例: %LocalAppData%\Microsoft\WindowsApps はユーザー書込可で PATH に入っている)
                    // System32 のフルパス + ArgumentList で防御深度を確保する。
                    var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
                    var notepadPath = Path.Combine(systemDir, "notepad.exe");
                    var fallbackInfo = new ProcessStartInfo
                    {
                        FileName = notepadPath,
                        UseShellExecute = false,
                    };
                    fallbackInfo.ArgumentList.Add(path);
                    using var _ = Process.Start(fallbackInfo);
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Log($".lhaignore のオープンに失敗: {ex.Message}", LogLevel.Warning);
        }
    }

    /// <summary>
    /// .lhaignore からパターンを読み直して ObservableCollection を更新する。
    /// </summary>
    /// <summary>
    /// .lhaignore からパターンを読み直して ObservableCollection を更新する。
    /// FileSystemWatcher 経由で UI スレッドから呼ばれるため、読込失敗で UI 例外にならないよう
    /// 一旦テンポラリに読んでから差し替える（失敗時は現在のリストを温存してログに残す）。
    /// </summary>
    internal void ReloadExcludedFilePatternsFromFile()
    {
        List<string> latest;
        try
        {
            latest = LhaignoreFile.ReadPatterns();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { Logger.Log($".lhaignore の再読込に失敗: {ex.Message}", LogLevel.Warning); }
            catch { /* Logger 未初期化のケース */ }
            return;
        }

        CompressionExcludedFilePatterns.Clear();
        foreach (var pattern in latest)
            CompressionExcludedFilePatterns.Add(pattern);
        SelectedExcludedFilePattern = null;
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
            // パスワード保護関連 (IsPasswordProtectionEnabled / PasswordMode / EncryptFileNames) は
            // 300ms debounce の AutoSave に依存していると、ON にして即ドロップしたとき
            // Snapshot が古い値を見て「パスワード無しのアーカイブ」が作られる事故が起きる。
            // ここで全部 VM の現在値を Settings に同期してから Snapshot を取る (codex P1 #3381085181)。
            //
            // TAR 形式は仕様上パスワード保護を持たない。UI は checkbox を disable するだけで
            // VM の IsPasswordProtectionEnabled 自体は ZIP/7z の設定を保持する設計だが、
            // そのまま Snapshot に流すと「TAR なのにパスワード入力ダイアログが出て CreateArchiveWriter
            // で InvalidOperationException」になる。Snapshot 段で TAR なら強制 false に押し下げる
            // (VM 側の値は保持されるので ZIP/7z に戻せば自動復活、codex P2 #3381085177)。
            var isTar = string.Equals(SelectedCompressionFormat, "TAR", StringComparison.OrdinalIgnoreCase);
            _settingsManager.Mutate(s =>
            {
                // codex P2 #3381582652: 同じ mutation で CompressionFormat も UI 選択値で上書きする。
                // debounced AutoSave 前に drop された場合、settings.CompressionFormat が古い値のまま
                // isTar の計算結果と矛盾するスナップショットを作ると「TAR なのにパスワード保護 OFF が
                // 効くが、フォーマットは ZIP/7z」という誤った非保護アーカイブを生成しうる。
                s.CompressionFormat = SelectedCompressionFormat;
                s.IsPasswordProtectionEnabled = IsPasswordProtectionEnabled && !isTar;
                s.PasswordMode = PasswordMode;
                s.EncryptFileNames = EncryptFileNames;
            });
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
    /// 最新バージョンの確認コマンド。
    /// VelopackUpdateDialog.Avalonia の手動チェック経路を <see cref="App.Check4Update(bool)"/> 経由で起動する。
    /// 結果表示 (UpToDate / Available / Error / Checking) はダイアログ側で完結するため、
    /// ViewModel 側でテキスト更新は不要。<see cref="IsCheckingUpdate"/> は App.UpdateCheckStateChanged
    /// イベントで駆動されるため、起動時自動チェック中もボタンが無効化される（並走実行を未然に防止）。
    /// </summary>
    [RelayCommand]
    private Task CheckForUpdateAsync()
    {
        try
        {
            App.Check4Update(manually: true);
        }
        catch (Exception ex)
        {
            Logger.LogException("更新チェックコマンドでエラーが発生", ex);
            _ = MessageService.ShowError(App.Text("Update.Error"));
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 「このバージョンをスキップ」で保存された <see cref="Settings.IgnoreUpdateTag"/> を取り消すコマンド。
    /// バージョンタブの「取り消し」ボタンから呼ばれる。誤クリックの復旧導線。
    /// </summary>
    [RelayCommand]
    private void ClearIgnoredUpdateTag()
    {
        if (string.IsNullOrEmpty(IgnoredUpdateTag)) return;
        try
        {
            _settingsManager.Mutate(s => s.IgnoreUpdateTag = "");
            _settingsManager.Save();
            IgnoredUpdateTag = "";
            Logger.Log("IgnoreUpdateTag をユーザー操作によりクリアしました", LogLevel.Warning);
        }
        catch (Exception ex)
        {
            Logger.LogException("IgnoreUpdateTag のクリアに失敗", ex);
            _ = MessageService.ShowError(App.Text("Error.SaveSettingsFailed", ex.Message));
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
            // Issue #54 対策: Process.Start(UseShellExecute=true) を UI スレッドから直接呼ぶと、
            // 環境次第で ShellExecuteEx の内部処理 (SmartScreen URL reputation, AV URL scanning,
            // シェル拡張初期化等) が UI スレッドを blocking し、メッセージポンプが停止して
            // 「アプリ全体が操作不能」に見える経路がある。ShellOpener が Task.Run で別スレッドへ
            // 逃がすため、UI スレッドはすぐ next frame に戻れる。
            await ShellOpener.OpenWithDefaultHandlerAsync(url);
        }
        catch (Exception ex)
        {
            Logger.LogException("フィードバックリンクを開けませんでした", ex);
        }
    }
}
