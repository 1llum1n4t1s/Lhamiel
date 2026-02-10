using System;
using Avalonia;
using Lhamiel.Util;
using Velopack;
using Velopack.Sources;
namespace Lhamiel;

/// <summary>
/// アプリケーションのエントリーポイント
/// </summary>
internal class Program
{
    /// <summary>
    /// 更新チェックのタイムアウト時間（ミリ秒）
    /// </summary>
    private const int UpdateCheckTimeoutMs = 10000;

    /// <summary>
    /// アプリケーションのエントリーポイント。Velopack のブートストラップを実行後、Avalonia を起動する。
    /// --update-check 引数が指定された場合は UI なしでサイレント更新チェックのみ実行する。
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    [STAThread]
    public static void Main(string[] args)
    {
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
    /// Windows ログイン時のスタートアップから呼び出される
    /// </summary>
    private static void RunSilentUpdateCheck()
    {
        try
        {
            // 設定とロガーを初期化
            var settings = SettingsManager.Instance.Current;

            Logger.Log("サイレント更新チェックを開始します。");

            var repoOwner = settings.UpdateRepoOwner;
            var repoName = settings.UpdateRepoName;
            var channel = string.IsNullOrWhiteSpace(settings.UpdateChannel) ? "release" : settings.UpdateChannel;

            if (string.IsNullOrWhiteSpace(repoOwner) || string.IsNullOrWhiteSpace(repoName))
            {
                Logger.Log("更新元リポジトリが未設定のため更新チェックをスキップします。");
                return;
            }

            var repoUrl = $"https://github.com/{repoOwner}/{repoName}";
            var isPrerelease = channel.Equals("prerelease", StringComparison.OrdinalIgnoreCase);
            var source = new GithubSource(repoUrl, string.Empty, isPrerelease);
            var updateManager = new UpdateManager(source);

            if (!updateManager.IsInstalled)
            {
                Logger.Log("開発実行のため更新チェックをスキップします。");
                return;
            }

            Logger.Log($"更新チェック: リポジトリ: {repoUrl}, チャンネル: {channel}");

            // 同期的に更新チェックを実行
            UpdateInfo? updateInfo;
            try
            {
                using var cts = new CancellationTokenSource(UpdateCheckTimeoutMs);
                updateInfo = updateManager.CheckForUpdatesAsync().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                Logger.Log("更新チェックがタイムアウトしました。");
                return;
            }

            if (updateInfo == null)
            {
                Logger.Log("利用可能な更新はありません。");
                return;
            }

            Logger.Log("新しいバージョンを検出しました。更新をダウンロードしています...");

            try
            {
                using var downloadCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                updateManager.DownloadUpdatesAsync(updateInfo, null, downloadCts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                Logger.Log("ダウンロードがタイムアウトしました。", LogLevel.Warning);
                return;
            }

            Logger.Log("ダウンロード完了。更新を適用します。");
            updateManager.ApplyUpdatesAndExit(updateInfo);
        }
        catch (Exception ex)
        {
            Logger.LogException("サイレント更新チェック中にエラーが発生しました", ex);
        }
        finally
        {
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
