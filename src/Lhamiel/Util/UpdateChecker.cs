using Velopack;
using Velopack.Sources;
namespace Lhamiel.Util;


/// <summary>
/// アプリケーションの更新チェック・ダウンロードを行う共通クラス
/// </summary>
public static class UpdateChecker
{
    /// <summary>
    /// 更新チェックのタイムアウト時間（ミリ秒）
    /// </summary>
    private const int CheckTimeoutMs = 10000;

    /// <summary>
    /// ダウンロードのタイムアウト時間
    /// </summary>
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 更新チェック結果の種類
    /// </summary>
    public enum UpdateResult
    {
        /// <summary>利用可能な更新なし</summary>
        NoUpdate,
        /// <summary>更新をダウンロード済み（適用可能）</summary>
        Downloaded,
        /// <summary>開発環境のためスキップ</summary>
        NotInstalled,
        /// <summary>リポジトリ未設定</summary>
        NotConfigured,
        /// <summary>エラー発生</summary>
        Error
    }

    /// <summary>
    /// 更新チェック結果
    /// </summary>
    /// <param name="Result">結果の種類</param>
    /// <param name="Info">更新情報（ダウンロード済みの場合のみ）</param>
    /// <param name="Manager">更新マネージャー（適用時に使用）</param>
    /// <param name="Message">ユーザー向けメッセージ</param>
    public record CheckResult(UpdateResult Result, UpdateInfo? Info, UpdateManager? Manager, string Message);

    /// <summary>
    /// <see cref="Settings"/> から <see cref="UpdateManager"/> を組み立てる共通ファクトリ。
    /// サイレント経路（<see cref="CheckAndDownloadAsync"/>）と UI 経路（<c>App.Check4Update</c>）から呼ばれる。
    /// <para>
    /// 戻り値は <c>(mgr, baseUrl, channel)</c> のタプル。<see cref="Settings.UpdateBaseUrl"/> が
    /// 未設定の場合は <c>null</c> を返す（呼び出し元が <c>NotConfigured</c> や早期 return を判断）。
    /// </para>
    /// <para>
    /// <see cref="Settings.UpdateBaseUrl"/> は <c>[JsonIgnore]</c> でハードコード固定されているため、
    /// 現実には null/空にはならないが、防御として allow-list ガードを残す。
    /// </para>
    /// <para>
    /// Cloudflare R2 を更新配信元として使う構成。Velopack の <see cref="SimpleWebSource"/> が
    /// <c>{baseUrl}/releases.{channel}.json</c> を取得し、同ディレクトリの nupkg をダウンロードする。
    /// channel は <c>vpk pack --channel</c> で指定した値（<c>win</c> / <c>win-arm64</c>）と一致する必要があり、
    /// インストール時の channel が Velopack 内部に記憶されるため明示指定は不要。
    /// </para>
    /// </summary>
    /// <param name="settings">Settings インスタンス（呼び出し元で snapshot 推奨）</param>
    /// <returns>UpdateManager と配信元情報。未設定時は null</returns>
    internal static (UpdateManager Manager, string BaseUrl, string Channel)? TryBuildUpdateManager(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var baseUrl = settings.UpdateBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        var channel = string.IsNullOrWhiteSpace(settings.UpdateChannel) ? "release" : settings.UpdateChannel;
        // SimpleWebSource: 静的 HTTP ホスティング (R2) からの取得。
        // base URL + /releases.{channel}.json を取りに行く。channel は installed channel を
        // Velopack が自動で再利用する (win / win-arm64)。Settings.UpdateChannel = "prerelease" を
        // 別 channel として扱う場合は、UpdateOptions.ExplicitChannel で明示指定する必要あり
        // (現状 vpk pack 側で prerelease 用 channel を生成していないため未対応)。
        var source = new SimpleWebSource(baseUrl);
        return (new UpdateManager(source), baseUrl, channel);
    }

    /// <summary>
    /// 更新を確認し、利用可能であればダウンロードまで行う。
    /// 適用は呼び出し元で行う（サイレント版は ApplyUpdatesAndExit、UI版は ApplyUpdatesAndRestart）。
    /// </summary>
    /// <param name="statusProgress">状態メッセージの進捗報告（UI表示用、nullの場合は無視）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>更新チェック結果</returns>
    public static async Task<CheckResult> CheckAndDownloadAsync(IProgress<string>? statusProgress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = SettingsManager.Instance.Current;
            var build = TryBuildUpdateManager(settings);
            if (build is null)
            {
                Logger.Log("更新元リポジトリが未設定のため更新チェックをスキップします。");
                return new CheckResult(UpdateResult.NotConfigured, null, null, App.Text("Update.RepoNotConfigured"));
            }
            var (updateManager, baseUrl, channel) = build.Value;

            if (!updateManager.IsInstalled)
            {
                Logger.Log("開発実行のため更新チェックをスキップします。");
                return new CheckResult(UpdateResult.NotInstalled, null, null, App.Text("Update.DevSkip"));
            }

            Logger.Log($"更新チェック: 配信元: {baseUrl}, チャンネル: {channel}");
            statusProgress?.Report(App.Text("Update.Downloading"));

            // 更新チェック（Velopack の CheckForUpdatesAsync は CancellationToken を受け取らないため、タイムアウトは Task.WhenAny で実装）
            UpdateInfo? updateInfo;
            try
            {
                var checkTask = updateManager.CheckForUpdatesAsync();
                var timeoutTask = Task.Delay(CheckTimeoutMs, cancellationToken);
                var completedTask = await Task.WhenAny(checkTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    Logger.Log(App.Text("Update.Timeout"));
                    return new CheckResult(UpdateResult.Error, null, null, App.Text("Update.Timeout"));
                }

                updateInfo = await checkTask;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Logger.Log(App.Text("Update.Timeout"));
                return new CheckResult(UpdateResult.Error, null, null, App.Text("Update.Timeout"));
            }

            if (updateInfo == null)
            {
                Logger.Log("利用可能な更新はありません。");
                return new CheckResult(UpdateResult.NoUpdate, null, null, App.Text("Update.Latest"));
            }

            Logger.Log("新しいバージョンを検出しました。更新をダウンロードしています...");
            statusProgress?.Report(App.Text("Update.Downloading"));

            // ダウンロード
            try
            {
                using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                downloadCts.CancelAfter(DownloadTimeout);
                await updateManager.DownloadUpdatesAsync(updateInfo, null, downloadCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Logger.Log("ダウンロードがタイムアウトしました。", LogLevel.Warning);
                return new CheckResult(UpdateResult.Error, null, null, App.Text("Update.DownloadTimeout"));
            }

            Logger.Log("ダウンロード完了。更新の適用準備ができました。");
            return new CheckResult(UpdateResult.Downloaded, updateInfo, updateManager, App.Text("Update.Downloaded"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogException("更新チェック中にエラーが発生しました", ex);
            return new CheckResult(UpdateResult.Error, null, null, App.Text("Update.CheckError"));
        }
    }
}
