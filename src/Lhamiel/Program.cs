using System;
using Avalonia;
using Lhamiel.Util;
using Velopack;
namespace Lhamiel;

/// <summary>
/// アプリケーションのエントリーポイント
/// </summary>
internal class Program
{
    /// <summary>
    /// アプリケーションのエントリーポイント。Velopack のブートストラップを実行後、Avalonia を起動する。
    /// --update-check 引数が指定された場合は UI なしでサイレント更新チェックのみ実行する。
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    /// <summary>
    /// プロセスに設定する AppUserModelID。
    /// Velopack がショートカット（タスクバーピン含む）へ書き込む AUMID（"velopack.{packId}" 規約）と
    /// 一致させる必要がある。不一致だとタスクバーがピンとウィンドウを exe パスで対応付けるため、
    /// アップデートの current/ 差し替えでアイコン解決が壊れ白紙アイコンになる。
    /// </summary>
    internal const string AppUserModelId = "velopack.Lhamiel";

    [STAThread]
    public static void Main(string[] args)
    {
        CrashHandler.Register();

        // ウィンドウ生成前（タスクバーに現れる前）に AUMID をピンと一致させる。失敗しても起動は継続する。
        if (OperatingSystem.IsWindows())
        {
            try { _ = NativeMethods.SetCurrentProcessExplicitAppUserModelID(AppUserModelId); }
            catch { /* best-effort */ }
        }

        VelopackApp.Build()
            .OnAfterInstallFastCallback(v => StartupRegistration.Register())
            .OnAfterUpdateFastCallback(v =>
            {
                StartupRegistration.Register();
                NotifyShellIconRefresh();
            })
            .OnBeforeUninstallFastCallback(v => StartupRegistration.Unregister())
            .Run();

        // サイレント更新チェックモード
        if (args.Length > 0 && args[0] == "--update-check")
        {
            RunSilentUpdateCheck();
            return;
        }

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
        const string uiMutexName = @"Local\Lhamiel_MainWindow_SingleInstance";
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
