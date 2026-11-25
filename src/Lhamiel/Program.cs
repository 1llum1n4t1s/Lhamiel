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
    /// （<see cref="RunSilentUpdate"/> の UI 並走検知と適用直前の所有権取得）の両方が同じ名前を使う。
    /// 片方だけ変えると並走検知が黙って効かなくなるため、ここに 1 か所で定義する。
    /// 値は既存のリリースと互換を保つため変更しない。
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

        // ダウンロード済みの更新を起動時に自動適用させない。自動適用すると、worker や IPC 転送の起動でも
        // Update.exe がインストール先の全プロセスを強制終了するため、適用は RunSilentUpdate と更新画面だけが行う。
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
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
        // 更新フックが残した保留マーカーを次の通常起動で一度だけ回収する（worker では行わない）。
        if (!IsArchiveWorker)
            RestoreSelectedApplicationShortcutIconsIfPending();

        // インストール／更新フックの登録完了後、最初のウィンドウ生成前に確定する。
        TaskbarIdentity.Apply();
        if (!IsArchiveWorker)
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
    /// 適用時の <c>ApplyUpdatesAndExit</c> は Update.exe がインストール先の全プロセスを強制終了するため、
    /// 動作中の UI・worker があれば適用を見送る。確認とダウンロード（最大10分）の間は Mutex を持たず、
    /// UI の起動を妨げない。適用の直前にだけ <see cref="RunSilentUpdate"/> が Mutex を取って他プロセスを確認する。
    /// 取得済みの更新は、次回のサイレント更新か更新画面の操作時に適用する
    /// （起動時の自動適用は <c>SetAutoApplyOnStartup(false)</c> で無効にしている）。
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

        try
        {
            Logger.Log("サイレント更新チェックを開始します。");
            RunSilentUpdate(
                MainWindowSingleInstanceMutexName,
                CheckAndDownloadForSilentUpdate,
                IsAnotherInstanceRunning);
        }
        catch (Exception ex)
        {
            // Mutex の作成失敗・確認失敗などは、更新を適用せず安全側で中止する。
            Logger.LogException("サイレント更新チェック中にエラーが発生しました (適用せず中止)", ex);
        }
        finally
        {
            Logger.Dispose();
        }
    }

    private static Action? CheckAndDownloadForSilentUpdate()
    {
        var result = UpdateChecker.CheckAndDownloadAsync().GetAwaiter().GetResult();

        if (result.Result == UpdateChecker.UpdateResult.Downloaded && result.Info != null && result.Manager != null)
        {
            var manager = result.Manager;
            var info = result.Info;
            Logger.Log("ダウンロード完了。適用の可否を確認します。");
            return () => manager.ApplyUpdatesAndExit(info);
        }

        Logger.Log($"サイレント更新チェック完了: {result.Result} - {result.Message}");
        return null;
    }

    /// <summary>
    /// 確認とダウンロードは Mutex なしで行い、適用の直前にだけ UI と同じ Mutex の所有権を取って適用する。
    /// </summary>
    /// <param name="mutexName">UI と共有する単一インスタンス Mutex 名</param>
    /// <param name="checkAndDownload">確認とダウンロード。適用が必要なら適用処理を返し、不要なら null</param>
    /// <param name="isAnotherInstanceRunning">自分以外の Lhamiel プロセス（UI・worker）が動作中か</param>
    internal static void RunSilentUpdate(
        string mutexName,
        Func<Action?> checkAndDownload,
        Func<bool> isAnotherInstanceRunning)
    {
        // UI が動作中なら、ダウンロードも行わず次回に回す。確認だけなのでハンドルは持ち続けない
        // （持つと後から起動する UI の createdNew が false になり、多重起動と誤判定される）。
        if (Mutex.TryOpenExisting(mutexName, out var running))
        {
            running.Dispose();
            Logger.Log("通常 UI モードが既に起動中のためサイレント更新をスキップします (次回ログイン時に再試行)", LogLevel.Warning);
            return;
        }

        var apply = checkAndDownload();
        if (apply is null)
            return;

        // ダウンロード中に UI が起動した場合に備え、適用の直前に所有権を取り直す。
        using var guard = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (!createdNew)
        {
            Logger.Log("ダウンロード中に通常 UI モードが起動したため更新の適用を見送ります (次回のサイレント更新または更新画面で適用)", LogLevel.Warning);
            return;
        }

        try
        {
            // 適用時の Update.exe はインストール先の全プロセスを強制終了するため、UI 以外（worker 等）も見送りの対象にする。
            // ただし ApplyUpdatesAndExit はプロセスを直ちに終了させるので、Mutex が守るのは適用開始の判定まで。
            // 差し替え中に起動した UI・worker は防げず、Update.exe に終了されうる。
            if (isAnotherInstanceRunning())
            {
                Logger.Log("他の Lhamiel プロセスが動作中のため更新の適用を見送ります (次回のサイレント更新または更新画面で適用)", LogLevel.Warning);
                return;
            }

            Logger.Log("更新を適用します。");
            apply();
        }
        finally
        {
            guard.ReleaseMutex();
        }
    }

    /// <summary>
    /// 自プロセス以外に、同じ実行ファイルの Lhamiel プロセスが動作中かを返す。確認できないときは安全側で true。
    /// </summary>
    internal static bool IsAnotherInstanceRunning()
    {
        var currentPath = Environment.ProcessPath;
        if (currentPath is null)
            return true;

        var selfId = Environment.ProcessId;
        var processName = Path.GetFileNameWithoutExtension(currentPath);
        foreach (var process in System.Diagnostics.Process.GetProcessesByName(processName))
        {
            using (process)
            {
                if (process.Id == selfId)
                    continue;
                try
                {
                    var path = process.MainModule?.FileName;
                    if (string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // 権限不足などでパスを確認できない同名プロセスは、安全側で動作中とみなす。
                    return true;
                }
                catch (InvalidOperationException)
                {
                    // 確認中に終了したプロセスは無視する。
                }
            }
        }
        return false;
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
