using System;
using System.Threading.Tasks;
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
    /// アプリケーションのエントリーポイント。Velopack のブートストラップを最初に実行し、
    /// メイン画面起動時は最新版があれば強制更新してから Avalonia を起動する。
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    [STAThread]
    public static async Task Main(string[] args)
    {
        VelopackApp.Build().Run();
        if (args.Length == 0)
        {
            await TryForceUpdateAsync(args);
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
    /// メイン画面起動時のみ、GitHub Releases に最新版があればダウンロード・適用・再起動する。
    /// </summary>
    /// <param name="args">再起動時に渡すコマンドライン引数</param>
    private static async Task TryForceUpdateAsync(string[] args)
    {
        try
        {
            var settings = Settings.Load();
            var repoOwner = settings.UpdateRepoOwner;
            var repoName = settings.UpdateRepoName;
            var channel = string.IsNullOrWhiteSpace(settings.UpdateChannel) ? "release" : settings.UpdateChannel;
            if (string.IsNullOrWhiteSpace(repoOwner) || string.IsNullOrWhiteSpace(repoName))
            {
                return;
            }

            var repoUrl = $"https://github.com/{repoOwner}/{repoName}";
            var isPrerelease = channel.Equals("prerelease", StringComparison.OrdinalIgnoreCase);
            var source = new GithubSource(repoUrl, string.Empty, isPrerelease);
            var mgr = new UpdateManager(source);
            if (!mgr.IsInstalled)
            {
                return;
            }

            var newVersion = await mgr.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                return;
            }

            await mgr.DownloadUpdatesAsync(newVersion);
            mgr.ApplyUpdatesAndRestart(newVersion, args);
        }
        catch
        {
            // ネットワークエラーなどで更新チェックに失敗した場合はアプリを起動する
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
