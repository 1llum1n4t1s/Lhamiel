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
    [STAThread]
    public static void Main(string[] args)
    {
        CrashHandler.Register();

        VelopackApp.Build()
            .OnAfterInstallFastCallback(v => StartupRegistration.Register())
            .OnAfterUpdateFastCallback(v => StartupRegistration.Register())
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
        const string uiMutexName = @"Local\Lhamiel_MainWindow_SingleInstance";
        Mutex? guardMutex = null;
        try
        {
            guardMutex = new Mutex(initiallyOwned: false, uiMutexName, out var createdNew);
            if (!createdNew)
            {
                Logger.Log("通常 UI モードが既に起動中のためサイレント更新をスキップします (次回ログイン時に再試行)", LogLevel.Warning);
                return;
            }
        }
        catch (Exception mutexEx)
        {
            Logger.LogException("UI モード Mutex の確認に失敗 (サイレント更新は安全側で中止)", mutexEx);
            return;
        }

        try
        {
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
            try { guardMutex?.Dispose(); } catch { }
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
