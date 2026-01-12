using System.Windows;
using System.IO;
using System.Threading;
using Lhamiel.Util;
using Velopack;
using Velopack.Sources;

namespace Lhamiel;

/// <summary>
/// App.xaml の相互作用ロジック
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 更新チェックのタイムアウト時間（ミリ秒）
    /// </summary>
    private const int UpdateCheckTimeoutMs = 10000;

    private readonly UpdateManager? _updateManager;

    public App()
    {
        try
        {
            // Velopackの初期化：インストール、アンインストール、更新などを処理
            var velopackApp = VelopackApp.Build();
            velopackApp.Run();
            Logger.Log("Velopack: 初期化完了");
        }
        catch (Exception ex)
        {
            Logger.Log($"Velopack: 初期化エラー: {ex.Message}");
        }

        _updateManager = InitializeUpdateManager();
    }

    /// <summary>
    /// アプリケーション起動時の処理
    /// </summary>
    /// <param name="e">起動イベント引数</param>
    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            // 更新チェックと適用を試行
            var updateApplied = await CheckAndApplyUpdatesAsync();
            if (updateApplied)
            {
                return;
            }

            base.OnStartup(e);

            // 起動ログを出力
            Logger.LogStartup(e.Args);

            // コマンドライン引数をチェック
            if (e.Args.Length > 0)
            {
                // ファイルが指定されている場合は展開処理を実行
                ProcessCommandLineFile(e.Args[0]);
            }
            else
            {
                // 引数がない場合はメインウィンドウを表示
                var mainWindow = new View.MainWindow();
                mainWindow.Show();
            }
        }
        catch (Exception ex)
        {
            // ログファイルに直接書き込み（Loggerが使えない場合のため）
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] アプリケーション起動エラー: {ex}\n");
            Shutdown();
            return;
        }
    }

    /// <summary>
    /// 更新をチェックして適用する
    /// </summary>
    /// <returns>更新が適用された場合はtrue、適用されなかった場合はfalse</returns>
    private async Task<bool> CheckAndApplyUpdatesAsync()
    {
        if (_updateManager == null)
        {
            return false;
        }

        try
        {
            using var cts = new CancellationTokenSource(UpdateCheckTimeoutMs);
            Logger.Log("Velopack: 更新チェックを開始します。");

            var updateInfo = await _updateManager.CheckForUpdatesAsync();
            if (updateInfo == null)
            {
                Logger.Log("Velopack: 利用可能な更新はありません。");
                return false;
            }

            Logger.Log("Velopack: 新しいバージョンを検出しました。更新をダウンロードしています...");

            await _updateManager.DownloadUpdatesAsync(updateInfo);

            Logger.Log("Velopack: ダウンロード完了。更新を適用して再起動します。");
            _updateManager.ApplyUpdatesAndRestart(updateInfo);
            return true;
        }
        catch (OperationCanceledException)
        {
            Logger.Log("Velopack: 更新チェックがタイムアウトしました。アプリケーションを続行します。");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Log($"Velopack: 更新チェック中にエラーが発生しました: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 更新マネージャーを初期化する
    /// </summary>
    /// <returns>初期化された更新マネージャー、またはnull</returns>
    private static UpdateManager? InitializeUpdateManager()
    {
        try
        {
            var settings = Settings.Load();
            var repoOwner = settings.UpdateRepoOwner;
            var repoName = settings.UpdateRepoName;
            var channel = string.IsNullOrWhiteSpace(settings.UpdateChannel) ? "release" : settings.UpdateChannel;

            if (string.IsNullOrWhiteSpace(repoOwner) || string.IsNullOrWhiteSpace(repoName))
            {
                Logger.Log("Velopack: 更新元リポジトリが未設定のため更新チェックをスキップします。");
                return null;
            }

            var repoUrl = $"https://github.com/{repoOwner}/{repoName}";
            var isPrerelease = channel.Equals("prerelease", StringComparison.OrdinalIgnoreCase);
            var source = new GithubSource(repoUrl, string.Empty, isPrerelease);
            var updateManager = new UpdateManager(source);

            if (!updateManager.IsInstalled)
            {
                Logger.Log("Velopack: 開発実行のため更新チェックをスキップします。");
                return null;
            }

            Logger.Log($"Velopack: 初期化完了 - リポジトリ: {repoUrl}, チャンネル: {channel}");
            return updateManager;
        }
        catch (Exception ex)
        {
            Logger.Log($"Velopack: 初期化エラー: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// コマンドラインで指定されたファイルまたはフォルダの処理を実行
    /// </summary>
    /// <param name="path">処理するファイルまたはフォルダのパス</param>
    private async void ProcessCommandLineFile(string path)
    {
        try
        {
            Logger.Log($"コマンドラインから処理を開始: {path}");

            // パスが存在するかチェック
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                Logger.Log($"指定されたパスが存在しません: {path}");
                MessageBox.Show($"指定されたファイルまたはフォルダが見つかりません。\n{path}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // 設定を読み込み
            var settings = Settings.Load();

            // ファイルかフォルダかを判定して適切な処理を実行
            if (File.Exists(path))
            {
                // ファイルの場合は展開処理を実行
                Logger.Log($"ファイルを展開処理します: {path}");
                await ProcessFileExtraction(path, settings);
            }
            else if (Directory.Exists(path))
            {
                // フォルダの場合は圧縮処理を実行
                Logger.Log($"フォルダを圧縮処理します: {path}");
                await ProcessFolderCompression(path, settings);
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("コマンドライン処理でエラーが発生", ex);
            MessageBox.Show($"処理中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    /// <summary>
    /// ファイルの展開処理を実行
    /// </summary>
    /// <param name="filePath">展開するファイルのパス</param>
    /// <param name="settings">アプリケーション設定</param>
    private async Task ProcessFileExtraction(string filePath, Settings settings)
    {
        try
        {
            var outputDir = settings.ExtractionOutputDirectory;
            var outputToSameDirectory = settings.ExtractionOutputToSameDirectory;

            // 進行状況ウィンドウを表示
            var progressWindow = new View.ProgressWindow("展開");
            var cancellationTokenSource = new CancellationTokenSource();
            progressWindow.CancelRequested += (_, _) => cancellationTokenSource.Cancel();
            progressWindow.Show();

            // 共通化された展開処理を実行
            var success = await ArchiveProcessor.ExtractArchiveAsync(filePath, outputDir, outputToSameDirectory, progressWindow, cancellationTokenSource.Token);

            if (success)
            {
                Logger.Log("ファイル展開処理が完了しました");
            }
            else
            {
                Logger.Log("ファイル展開処理が失敗しました");
            }

            // アプリケーションを終了
            Shutdown();
        }
        catch (Exception ex)
        {
            Logger.LogException("ファイル展開処理でエラーが発生", ex);
            MessageBox.Show($"展開中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    /// <summary>
    /// フォルダの圧縮処理を実行
    /// </summary>
    /// <param name="folderPath">圧縮するフォルダのパス</param>
    /// <param name="settings">アプリケーション設定</param>
    private async Task ProcessFolderCompression(string folderPath, Settings settings)
    {
        try
        {
            var outputDir = settings.CompressionOutputDirectory;
            var outputToSameDirectory = settings.CompressionOutputToSameDirectory;
            var format = settings.CompressionFormat;

            // 進行状況ウィンドウを表示
            var progressWindow = new View.ProgressWindow("圧縮");
            var cancellationTokenSource = new CancellationTokenSource();
            progressWindow.CancelRequested += (_, _) => cancellationTokenSource.Cancel();
            progressWindow.Show();

            // 共通化された圧縮処理を実行
            var success = await ArchiveProcessor.CompressFolderAsync(folderPath, outputDir, outputToSameDirectory, format, progressWindow, cancellationTokenSource.Token);

            if (success)
            {
                Logger.Log("フォルダ圧縮処理が完了しました");
            }
            else
            {
                Logger.Log("フォルダ圧縮処理が失敗しました");
            }

            // アプリケーションを終了
            Shutdown();
        }
        catch (Exception ex)
        {
            Logger.LogException("フォルダ圧縮処理でエラーが発生", ex);
            MessageBox.Show($"圧縮中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    /// <summary>
    /// アプリケーション終了時の処理
    /// </summary>
    /// <param name="e">終了イベント引数</param>
    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Log($"アプリケーション終了: 終了コード = {e.ApplicationExitCode}");
        base.OnExit(e);
    }
}
