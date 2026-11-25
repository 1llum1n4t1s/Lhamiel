using Avalonia;
using Lhamiel.Util;
using Velopack;
namespace Lhamiel;

/// <summary>
/// アプリケーションのエントリーポイント
/// </summary>
internal class Program
{
    internal const string CompressionWorkerArgument = "--compression-worker";
    internal const string ExtractionWorkerArgument = "--extraction-worker";
    internal const string SavedPasswordChangedArgument = "--saved-compression-password-changed";
    internal static bool IsCompressionWorker { get; } =
        Environment.GetCommandLineArgs().Contains(CompressionWorkerArgument, StringComparer.Ordinal);
    internal static bool IsExtractionWorker { get; } =
        Environment.GetCommandLineArgs().Contains(ExtractionWorkerArgument, StringComparer.Ordinal);
    internal static bool IsArchiveWorker => IsCompressionWorker || IsExtractionWorker;

    private const string ShortcutIconRestoreMarkerFileName = ".shortcut-icon-restore-pending";

    /// <summary>
    /// 通常 UI モードの単一インスタンス Mutex 名。
    /// <para>
    /// UI 側（<c>App.OnFrameworkInitializationCompleted</c> の多重起動チェック）と、サイレント更新経路
    /// （<see cref="Main"/> の UI 並走検知）の両方が同じ名前で所有権を取る。片方だけ変えると並走検知が
    /// 黙って効かなくなるため、ここに 1 か所で定義する。値は既存のリリースと互換を保つため変更しない。
    /// <c>Local\</c> プレフィックスでセッションローカルに限定し、別セッション/別ユーザーによる
    /// Mutex 先取りでのサービス妨害（DoS）を防ぐ。
    /// </para>
    /// </summary>
    internal const string MainWindowSingleInstanceMutexName = @"Local\Lhamiel_MainWindow_SingleInstance";

    /// <summary>
    /// プロセスとショートカットで共有する、現在の配置先に対応した AppUserModelID。
    /// </summary>
    internal static string AppUserModelId => TaskbarIdentity.Current;

    /// <summary>
    /// アプリケーションのエントリーポイント。Velopack のブートストラップを実行後、Avalonia を起動する。
    /// --update-check 引数が指定された場合は UI なしでサイレント更新チェックのみ実行する。
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        CrashHandler.Register();

        VelopackApp.Build()
            .OnAfterInstallFastCallback(v =>
            {
                StartupRegistration.Register();
                RefreshShellContextMenuRegistration();
            })
            .OnAfterUpdateFastCallback(v =>
            {
                StartupRegistration.Register();
                RefreshShellContextMenuRegistration();
                MarkShortcutIconRestorePending();
                NotifyShellIconRefresh();
            })
            .OnRestarted(v =>
            {
                // 設定を読めず見送ったときは保留マーカーを残し、次回の正常な起動で復元する。
                if (RestoreSelectedApplicationShortcutIcons())
                    ClearShortcutIconRestorePending();
            })
            .OnBeforeUninstallFastCallback(v => CleanupBeforeUninstall())
            .Run();

        // サイレント更新チェックモード
        if (args.Length > 0 && args[0] == "--update-check")
        {
            RunSilentUpdateCheck();
            return;
        }

        // ApplyUpdatesAndExit を使うサイレント更新は OnRestarted を通らないため、
        // 更新フックが残した保留マーカーを次の通常起動で一度だけ回収する。
        RestoreSelectedApplicationShortcutIconsIfPending();

        // インストール／更新フックの登録完了後、最初のウィンドウ生成前に確定する。
        TaskbarIdentity.Apply();
        ShortcutCreator.RefreshKnownApplicationShortcutIdentity();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Logger.LogException("アプリケーション起動エラー", ex);
            throw;
        }
    }

    /// <summary>
    /// インストール／更新で実行ファイルの外部配置先が変わったとき、
    /// Windows 11 の sparse パッケージを現在の場所へ張り直す。
    /// </summary>
    private static void RefreshShellContextMenuRegistration()
    {
        // settings.json を読めなかったときの Current は既定値（両方 OFF）なので、
        // それを根拠に利用者の右クリックメニュー登録を解除・変更しない。
        if (SettingsManager.Instance.StartedWithUnreadableSettings)
        {
            Logger.Log("settings.json を読めなかったため、右クリックメニュー登録の更新を見送りました", LogLevel.Warning);
            return;
        }

        var settings = SettingsManager.Instance.Current;
        _ = ShellContextMenu.SetEnabled(
            settings.AddExtractToContextMenu,
            settings.AddCompressToContextMenu);
    }

    /// <summary>
    /// Velopack のアンインストール前フックで、Lhamiel が追加したシェル統合を解除する。
    /// FastCallback 内で完了できるよう、UIを表示せず同期処理だけを実行する。
    /// </summary>
    internal static void CleanupBeforeUninstall(
        Action? unregisterStartup = null,
        Func<bool>? removeContextMenu = null,
        Func<bool>? disassociateAllFileTypes = null)
    {
        (unregisterStartup ?? StartupRegistration.Unregister)();
        _ = (removeContextMenu ?? ShellContextMenu.RemoveAll)();
        _ = (disassociateAllFileTypes ?? FileAssociation.DisassociateAllFileTypes)();
    }

    /// <summary>
    /// アップデート適用直後（--veloapp-updated フック）にシェルへアイコン再解決を促す。
    /// Velopack は current/ ディレクトリ差し替え後にタスクバーピンの .lnk を書き換えるが、
    /// Windows 11 のタスクバーが再解決に失敗すると白紙アイコンがアイコンキャッシュに残るため、
    /// SHCNE_ASSOCCHANGED 通知でキャッシュ更新を促す（AUMID 一致設定の補助、best-effort）。
    /// </summary>
    private static void NotifyShellIconRefresh()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Velopack が更新後に既存ショートカットを実行ファイルのアイコンへ戻した後、
    /// ユーザーが選択しているアプリアイコンを再適用する。
    /// </summary>
    /// <remarks>
    /// <c>OnAfterUpdateFastCallback</c> の実行後に Velopack 自身が .lnk を保存するため、
    /// この処理はショートカット更新後に発火する <c>OnRestarted</c> から呼び出す。
    /// </remarks>
    /// <returns>
    /// 復元を実行したら true。設定を読めず既定値のアイコンで既存ショートカットを書き換えてしまう場合は、何もせず false。
    /// </returns>
    internal static bool RestoreSelectedApplicationShortcutIcons(
        Action<string?>? refreshShortcutIcons = null,
        string? appIconVariant = null,
        bool? settingsUnreadable = null)
    {
        if (appIconVariant is null && (settingsUnreadable ?? SettingsManager.Instance.StartedWithUnreadableSettings))
        {
            Logger.Log("settings.json を読めなかったため、ショートカットのアイコン復元を見送りました", LogLevel.Warning);
            return false;
        }

        var variant = Settings.NormalizeAppIconVariant(
            appIconVariant ?? SettingsManager.Instance.Current.AppIconVariant);
        (refreshShortcutIcons ?? ShortcutCreator.RefreshKnownApplicationShortcutIcons)(variant);
        return true;
    }

    /// <summary>
    /// Velopack がショートカットを書き戻した後にアイコン復元が必要であることを記録する。
    /// </summary>
    internal static bool MarkShortcutIconRestorePending(string? markerPath = null)
    {
        try
        {
            var path = markerPath ?? GetShortcutIconRestoreMarkerPath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, "pending");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogException("ショートカットアイコン復元マーカーの作成に失敗しました", ex);
            return false;
        }
    }

    /// <summary>
    /// サイレント更新後の次回起動で、保留中のアイコン復元を一度だけ実行する。
    /// </summary>
    internal static bool RestoreSelectedApplicationShortcutIconsIfPending(
        string? markerPath = null,
        Action<string?>? refreshShortcutIcons = null,
        string? appIconVariant = null,
        bool? settingsUnreadable = null)
    {
        var path = markerPath ?? GetShortcutIconRestoreMarkerPath();
        if (!File.Exists(path))
            return false;

        if (!RestoreSelectedApplicationShortcutIcons(refreshShortcutIcons, appIconVariant, settingsUnreadable))
            return false;
        ClearShortcutIconRestorePending(path);
        return true;
    }

    /// <summary>
    /// 復元完了後の保留マーカーを削除する。
    /// </summary>
    internal static void ClearShortcutIconRestorePending(string? markerPath = null)
    {
        try
        {
            var path = markerPath ?? GetShortcutIconRestoreMarkerPath();
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Logger.LogException("ショートカットアイコン復元マーカーの削除に失敗しました", ex);
        }
    }

    private static string GetShortcutIconRestoreMarkerPath() =>
        Path.Combine(Settings.AppDataDirectory, ShortcutIconRestoreMarkerFileName);

    /// <summary>
    /// UI なしでサイレント更新チェックを実行する
    /// Windows ログイン時のスタートアップ (`HKCU\Run` 経由) から呼び出される
    /// </summary>
    /// <remarks>
    /// 通常 UI モードのインスタンスと並走すると <c>ApplyUpdatesAndExit</c> による
    /// ファイル差し替えが UI モード側のファイルロックと衝突する経路があるため、
    /// 通常 UI モードの Mutex (`Local\Lhamiel_MainWindow_SingleInstance`) が掴まれていれば
    /// サイレント更新は諦めて次回ログインに回す。
    /// </remarks>
    private static void RunSilentUpdateCheck()
    {
        // --update-check 経路は App コンストラクタを通らないため Logger.Initialize されておらず、
        // Logger.Log がすべて _logger == null ガードでサイレント握りつぶしされる経路があった。
        // ここで最小限の Logger 初期化を行い、サイレント更新チェックの動作ログを残せるようにする。
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
            System.Diagnostics.Debug.WriteLine($"--update-check モードでの Logger 初期化に失敗: {initEx.Message}");
        }

        // 通常 UI モードのインスタンスとの並走を検知する。UI モード Mutex が掴まれている場合、
        // 自動更新適用 (ApplyUpdatesAndExit) が UI モードのファイルロックと衝突するため、
        // サイレント更新は適用せず次回ログインに回す。
        // ⚠️ RTK レビュー #D-003 対応: UI 側 (App.axaml.cs) と同じ `initiallyOwned: true` で所有権を取り、
        // サイレント実行中に UI モード起動が走るレースを完全に防ぐ。所有権取得は finally 内で
        // ReleaseMutex してから Dispose する。
        const string uiMutexName = MainWindowSingleInstanceMutexName;
        Mutex? guardMutex = null;
        var guardMutexOwned = false;
        var skipSilentUpdate = false;
        try
        {
            guardMutex = new Mutex(initiallyOwned: true, uiMutexName, out var createdNew);
            if (!createdNew)
            {
                Logger.Log("通常 UI モードが既に起動中のためサイレント更新をスキップします (次回ログイン時に再試行)", LogLevel.Warning);
                // CodeRabbit 指摘対応 (#3305115838): 早期 return すると下流の finally を通らないため、
                // フラグを立てて通常パスから抜けて finally で Mutex / Logger を確実に Dispose する。
                skipSilentUpdate = true;
            }
            else
            {
                // createdNew=true なら所有権を獲得済み（initiallyOwned: true による）
                guardMutexOwned = true;
            }
        }
        catch (AbandonedMutexException)
        {
            // 前回プロセスが Release せずに死んだケース。new Mutex は所有権を引き継いで例外を投げる。
            // サイレント更新を続行してよい（前回の UI モードはもう存在しないことが確実）。
            Logger.Log("前回プロセスが Mutex を Release せずに終了していました。所有権を引き継いでサイレント更新を続行します。", LogLevel.Warning);
            guardMutexOwned = true;
        }
        catch (Exception mutexEx)
        {
            Logger.LogException("UI モード Mutex の確認に失敗 (サイレント更新は安全側で中止)", mutexEx);
            skipSilentUpdate = true;
        }

        try
        {
            if (skipSilentUpdate)
                return; // finally で guardMutex / Logger を Dispose してから抜ける
            Logger.Log("サイレント更新チェックを開始します。");

            var result = UpdateChecker.CheckAndDownloadAsync().GetAwaiter().GetResult();

            if (result.Result == UpdateChecker.UpdateResult.Downloaded && result.Info != null && result.Manager != null)
            {
                Logger.Log("ダウンロード完了。更新を適用します。");
                result.Manager.ApplyUpdatesAndExit(result.Info);
            }
            else
            {
                Logger.Log($"サイレント更新チェック完了: {result.Result} - {result.Message}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("サイレント更新チェック中にエラーが発生しました", ex);
        }
        finally
        {
            // Mutex は所有権を取ったスレッドからしか ReleaseMutex できないので、明示的に解放してから Dispose する。
            // (RTK レビュー #D-003 対応: initiallyOwned: true の対称化に伴う後始末)
            try
            {
                if (guardMutexOwned)
                {
                    try { guardMutex?.ReleaseMutex(); }
                    catch (ApplicationException) { /* 別スレッドからの release は無視 */ }
                }
                guardMutex?.Dispose();
            }
            catch { /* best-effort */ }
            Logger.Dispose();
        }
    }

    /// <summary>
    /// Avaloniaアプリケーションをビルドする
    /// </summary>
    /// <returns>アプリケーションビルダー</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
