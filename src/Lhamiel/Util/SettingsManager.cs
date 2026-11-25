namespace Lhamiel.Util;

/// <summary>
/// 設定管理のシングルトンクラス
/// アプリケーション全体で一つの Settings インスタンスを共有し、
/// 繰り返しファイルを読み込むことを防ぎます。
/// </summary>
public sealed class SettingsManager
{
    /// <summary>
    /// SettingsManager のシングルトンインスタンス
    /// </summary>
    private static readonly Lazy<SettingsManager> _instance = new(() => new SettingsManager());
    private readonly Settings _settings;
    private byte[]? _savedCompressionPassword;

    /// <summary>
    /// 変更・保存・スナップショット作成を直列化するためのロック。
    /// UI スレッド上の AutoSave と、バックグラウンド Task.Run 上の
    /// <see cref="CreateSnapshot"/> が同時に走っても、Settings 内の参照型フィールドの列挙で
    /// <see cref="InvalidOperationException"/> が起きないようにする。
    /// （除外パターンは <see cref="LhaignoreFile"/> = `.lhaignore` ファイルが真の源で、
    /// Settings 上に状態は持たない。）
    /// </summary>
    private readonly Lock _lock = new();

    /// <summary>保存直前に最新の設定ファイルを読む入口（既定は <see cref="Settings.LoadWithStatus"/>。テストで差し替える）。</summary>
    private readonly Func<SettingsLoadResult> _loadLatest;

    /// <summary>設定をファイルへ書く入口（既定は <see cref="Settings.Save"/>。テストで差し替える）。</summary>
    private readonly Action<Settings> _saveSettings;

    private readonly bool _isArchiveWorker;

    /// <summary>
    /// 起動時に settings.json が読めず、メモリ上の設定が既定値のままであることを示す。
    /// この間にメモリの設定を書き出すと、ファイルに残っている全設定・暗号化パスワードを既定値で潰すため、
    /// 保存は常に <see cref="InvalidOperationException"/> で中止する（再起動で正しく読み込み直す）。
    /// 読めるようになった後でも、メモリの設定のどの値が利用者の変更か判別できないため、このセッションでは保存しない。
    /// </summary>
    private readonly bool _startedWithUnreadableSettings;

    /// <summary>
    /// このセッションが settings.json を読めないまま既定値で動いている（保存は常に中止される）ことを示す。
    /// UI は起動時に1回だけ警告し、外部状態（右クリックメニュー・関連付け・ショートカット等）を既定値で書き換える操作を拒否する。
    /// </summary>
    internal bool StartedWithUnreadableSettings => _startedWithUnreadableSettings;

    /// <summary>
    /// SettingsManager のシングルトンインスタンスを取得
    /// </summary>
    public static SettingsManager Instance => _instance.Value;

    /// <summary>
    /// 現在の設定を取得（読み取り専用の参照を返す）。
    /// 並列処理や長時間の処理で使う場合は <see cref="CreateSnapshot"/> を使うこと。
    /// </summary>
    public Settings Current => _settings;

    /// <summary>Unicode NFC 正規化が有効かどうか（Current のショートカット）。</summary>
    public bool NormalizeUnicodeFileNames => _settings.NormalizeUnicodeFileNames;

    /// <summary>
    /// 現在の設定の浅いコピー（スナップショット）を作成する。
    /// 並列処理中に UI スレッドから設定変更が起きても、スナップショットは影響を受けない。
    /// <see cref="Mutate"/> / <see cref="Save"/> と同一ロック下で実行されるため、
    /// コレクション列挙中の <see cref="InvalidOperationException"/> を避けられる。
    /// </summary>
    public Settings CreateSnapshot()
    {
        lock (_lock) return _settings.Snapshot();
    }

    /// <summary>
    /// 設定を同一ロック下で変更する。<c>_settings</c> のプロパティ代入で使用する。
    /// 除外パターンは <see cref="LhaignoreFile"/> 経由で別管理なので、Settings 上の
    /// コレクション差し替えはない。
    /// </summary>
    /// <param name="mutator">変更アクション</param>
    public void Mutate(Action<Settings> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);
        lock (_lock) mutator(_settings);
    }

    /// <summary>
    /// 変更と保存を同一ロック下で連続実行する atomic 版。
    /// <para>
    /// 通常の <see cref="Mutate"/> + <see cref="Save"/> はロックが 2 回別々に取られるため、
    /// その間に別 thread から <c>Mutate</c> + <c>Save</c> が割り込むと「メモリ更新済み・ディスク古い」
    /// あるいはその逆の不整合が起き得る。Velopack の VersionIgnored ハンドラのように
    /// 1 つのユーザー操作が「変更 → 永続化」を 1 アトミック単位で行いたい場合にこちらを使う。
    /// </para>
    /// </summary>
    /// <param name="mutator">変更アクション</param>
    /// <exception cref="InvalidOperationException">保存失敗時にスローされる（呼び出し元で UI 通知すること）。</exception>
    public void MutateAndSave(Action<Settings> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);
        lock (_lock)
        {
            SaveWithProcessLock(mutator);
        }
        Logger.Log("設定を変更し保存しました");
    }

    /// <summary>
    /// プライベートコンストラクタ
    /// </summary>
    private SettingsManager()
    {
        _loadLatest = Settings.LoadWithStatus;
        _saveSettings = static s => s.Save();
        _isArchiveWorker = Program.IsArchiveWorker;
        try
        {
            var requestSettings = CompressionWorkerLauncher.CurrentRequest?.Settings;
            if (requestSettings is not null)
                _settings = requestSettings;
            else
            {
                // 他の圧縮 worker が Remember を保存している途中の設定を読まない。
                using var gate = CrossProcessResourceGate.EnterMetadata(Path.Combine(Settings.AppDataDirectory, "settings.json"));
                var loaded = _loadLatest();
                _settings = loaded.Settings;
                _startedWithUnreadableSettings = loaded.Status == SettingsLoadStatus.Unreadable;
            }
            _savedCompressionPassword = _settings.EncryptedCompressionPassword;
            // Logger が未初期化の場合は設定を渡して初期化（循環参照防止）
            Logger.Initialize(new LoggerConfig
            {
                LogDirectory = Settings.AppDataDirectory,
                FilePrefix = "Lhamiel",
                MaxSizeMB = _settings.LogMaxSizeMB,
                RetentionDays = _settings.LogRetentionDays
            });
            if (_startedWithUnreadableSettings)
                Logger.Log("settings.json を読めなかったため既定値で起動しました。既存の設定を守るため、このセッションでは設定を保存しません", LogLevel.Warning);
            else
                Logger.Log("設定を読み込みました");
        }
        catch (Exception ex)
        {
            // 重要: Settings.Load 失敗時、Logger.Initialize がまだ実行されていない可能性があるため
            // Logger.LogException がサイレントに握りつぶされる経路がある。
            // 起動失敗の詳細をどこにも残せず再現困難になる事態を避けるため、
            // 緊急 Logger 初期化（デフォルト構成）を試みてからログ出力する。
            try
            {
                Logger.Initialize(new LoggerConfig
                {
                    LogDirectory = Settings.AppDataDirectory,
                    FilePrefix = "Lhamiel",
                });
            }
            catch (Exception initEx)
            {
                System.Diagnostics.Debug.WriteLine($"緊急 Logger 初期化にも失敗: {initEx.Message}");
            }
            Logger.LogException("設定の読み込みに失敗しました。デフォルト設定を使用します", ex);
            // Logger も使えない最終フォールバック: Debug.WriteLine だけは残す
            System.Diagnostics.Debug.WriteLine($"Settings.Load 失敗: {ex}");
            _settings = new Settings();
            // 実際の設定内容を確認できていないので、既定値を書き戻して既存設定を潰さない。
            _startedWithUnreadableSettings = true;
        }
    }

    /// <summary>テスト用。読み込み・保存の入口と初期状態を差し替えて、実ユーザーの設定へ触れずに保存の安全性を検証する。</summary>
    internal SettingsManager(
        SettingsLoadResult initial,
        Func<SettingsLoadResult> loadLatest,
        Action<Settings> saveSettings,
        bool isArchiveWorker = false)
    {
        _settings = initial.Settings;
        _savedCompressionPassword = _settings.EncryptedCompressionPassword;
        _startedWithUnreadableSettings = initial.Status == SettingsLoadStatus.Unreadable;
        _loadLatest = loadLatest;
        _saveSettings = saveSettings;
        _isArchiveWorker = isArchiveWorker;
    }

    /// <summary>
    /// 保存中止の例外。メッセージは理由だけ（ロケールキー）で、「設定の保存に失敗しました」の接頭辞は表示側が1回だけ付ける。
    /// 原因例外のメッセージにはユーザーのパスが入り得るため、利用者向けの文言へ埋め込まずログと内部例外に残す。
    /// </summary>
    private static InvalidOperationException CreateSaveAbortedException(string reasonKey, Exception? inner = null)
        => new(App.Text(reasonKey), inner);

    /// <summary>
    /// 設定を保存。変更・Snapshot と同一ロック下で保護する。
    /// </summary>
    public void Save()
    {
        try
        {
            lock (_lock) SaveWithProcessLock();
            Logger.Log("設定を保存しました");
        }
        catch (Exception ex)
        {
            Logger.LogException("設定の保存に失敗しました", ex);
            throw;
        }
    }

    private void SaveWithProcessLock(Action<Settings>? mutator = null)
    {
        // 起動時に読めなかった間のメモリは既定値なので、書き出すと既存の全設定・パスワードを失う。
        if (_startedWithUnreadableSettings)
            throw CreateSaveAbortedException("Error.SaveAbortedStartupUnreadable");

        using var gate = CrossProcessResourceGate.EnterMetadata(Path.Combine(Settings.AppDataDirectory, "settings.json"));
        var loaded = _loadLatest();
        if (loaded.Status == SettingsLoadStatus.Unreadable)
        {
            // ファイルの現在の内容が不明（他プロセスが排他中など）。変更を適用せず、何も書かずに失敗を返す。
            throw CreateSaveAbortedException("Error.SaveAbortedFileUnreadable", loaded.Error);
        }

        var latest = loaded.Settings;
        if (_isArchiveWorker)
        {
            // worker の変更は Remember パスワードだけ。古い UI 設定全体を再保存しない。
            mutator?.Invoke(latest);
            _saveSettings(latest);
            _settings.EncryptedCompressionPassword = latest.EncryptedCompressionPassword;
        }
        else
        {
            // mutator が付く保存（MutateAndSave）は、書込みの失敗・中止でメモリを変更前へ戻す（ロック内で完結させ、
            // 呼び出し側の巻き戻しとの間に他スレッドの変更が割り込まないようにする）。mutator なしの Save は
            // AutoSave が先に Mutate で反映した UI の未保存の変更を持つため、巻き戻さない。
            var before = mutator is null ? null : _settings.Snapshot();
            try
            {
                // UI でパスワードを変更・消去していない場合は、worker が保存した最新値を維持する。
                if (ReferenceEquals(_settings.EncryptedCompressionPassword, _savedCompressionPassword))
                    _settings.EncryptedCompressionPassword = latest.EncryptedCompressionPassword;
                mutator?.Invoke(_settings);
                _saveSettings(_settings);
            }
            catch
            {
                if (before is not null)
                    _settings.CopyFrom(before);
                throw;
            }
        }
        _savedCompressionPassword = _settings.EncryptedCompressionPassword;
    }

    internal void RefreshSavedCompressionPassword()
    {
        lock (_lock)
        {
            using var gate = CrossProcessResourceGate.EnterMetadata(Path.Combine(Settings.AppDataDirectory, "settings.json"));
            var loaded = _loadLatest();
            if (loaded.Status == SettingsLoadStatus.Unreadable)
            {
                // 読めないときは既定値（パスワードなし）で旧値を消さず、現在の値を保つ。
                Logger.Log($"settings.json を読めないため保存済みパスワードの再読込を見送りました: {loaded.Error?.Message}", LogLevel.Warning);
                return;
            }
            _settings.EncryptedCompressionPassword = loaded.Settings.EncryptedCompressionPassword;
            _savedCompressionPassword = _settings.EncryptedCompressionPassword;
        }
    }

}
