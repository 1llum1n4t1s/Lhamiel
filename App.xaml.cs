using System.Windows;
using System.IO;
using Lhamiel.Util;
using Velopack;
using Velopack.Sources;

namespace Lhamiel;

/// <summary>
/// App.xaml の相互作用ロジック
/// </summary>
public partial class App
{
    /// <summary>
    /// アプリケーションインスタンス管理用の Mutex
    /// </summary>
    private static Mutex? _instanceMutex;

    /// <summary>
    /// 更新チェックのタイムアウト時間（ミリ秒）
    /// </summary>
    private const int UpdateCheckTimeoutMs = 10000;

    private readonly UpdateManager? _updateManager;

    /// <summary>
    /// IPC サーバーのキャンセル用トークンソース
    /// </summary>
    private CancellationTokenSource? _ipcCts;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public App()
    {
        // プロセス全体の優先度を下げる（低スペックPCでのフリーズ防止、ハイスペックPCでも影響なし）
        try
        {
            System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex)
        {
            // 優先度の設定に失敗しても続行（権限の問題など）
            Logger.Log($"Failed to set process priority: {ex.Message}", LogLevel.Warning);
        }

        // Log4netを早期に初期化
        Logger.Initialize();

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
        // 1. UIスレッド（画面操作など）で発生した未処理の例外をキャッチする
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        // 2. バックグラウンドタスク（Task.Runなど）で発生した例外をキャッチする
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        // 3. それ以外の場所で発生した致命的な例外をキャッチする
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        try
        {
            // メインウィンドウの多重起動チェック
            const string mutexName = "Lhamiel_MainWindow_SingleInstance";
            _instanceMutex = new Mutex(true, mutexName, out var createdNew);

            if (!createdNew)
            {
                // 既に起動しているインスタンスがある場合
                Logger.Log("アプリケーションは既に起動しています。既存のインスタンスをアクティブ化します。");
                ActivateExistingInstance();

                // コマンドライン引数があれば送信
                if (e.Args.Length > 0)
                {
                    Logger.Log("コマンドライン引数を既存のインスタンスに送信します。");
                    await IpcService.SendArgsToExistingInstanceAsync(e.Args);
                }

                Shutdown();
                return;
            }

            // 初回起動時は IPC サーバーを開始して後続インスタンスからの引数を待機
            _ipcCts = new CancellationTokenSource();
            _ = IpcService.StartServerAsync(OnArgsReceived, _ipcCts.Token);

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
                // 引数から圧縮形式とファイルパスを抽出
                var compressionFormat = "default";
                var filePath = e.Args[0];

                // --format オプションがある場合は圧縮形式を取得
                if (e.Args.Length >= 3 && e.Args[0] == "--format")
                {
                    compressionFormat = e.Args[1];
                    filePath = e.Args[2];
                }

                ProcessCommandLineFile(filePath, compressionFormat);
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
    /// 既に起動しているメインウィンドウインスタンスをアクティブ化する
    /// </summary>
    private static void ActivateExistingInstance()
    {
        try
        {
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var otherProcess = System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName).FirstOrDefault(p => p.Id != currentProcess.Id);

            if (otherProcess != null)
            {
                Logger.Log($"既存インスタンスを見つけました。PID: {otherProcess.Id}");

                // メインウィンドウをアクティブ化（NativeMethods を使用）
                try
                {
                    if (otherProcess.MainWindowHandle != IntPtr.Zero)
                    {
                        NativeMethods.SetForegroundWindow(otherProcess.MainWindowHandle);
                        Logger.Log("既存インスタンスをアクティブ化しました。");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"既存インスタンスのアクティブ化に失敗: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"既存インスタンスのアクティブ化処理でエラーが発生: {ex.Message}");
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
            // 前回チェックからの経過時間をチェック
            var settings = Settings.Load();
            if (!string.IsNullOrWhiteSpace(settings.LastUpdateCheckTime) &&
                DateTime.TryParse(settings.LastUpdateCheckTime, out var lastCheckTime))
            {
                var elapsed = DateTime.Now - lastCheckTime;
                if (elapsed.TotalDays < 7)
                {
                    Logger.Log($"Velopack: 前回チェックから{elapsed.TotalDays:F1}日経過しているため、アップデートチェックをスキップします。(次回チェック対象: {lastCheckTime.AddDays(7):yyyy-MM-dd HH:mm:ss})");
                    return false;
                }
            }

            using var cts = new CancellationTokenSource(UpdateCheckTimeoutMs);
            Logger.Log("Velopack: 更新チェックを開始します。");

            var updateInfo = await _updateManager.CheckForUpdatesAsync();
            if (updateInfo == null)
            {
                // チェック時刻を記録
                settings.LastUpdateCheckTime = DateTime.Now.ToString("o");
                settings.Save();
                Logger.Log("Velopack: 利用可能な更新はありません。");
                return false;
            }

            Logger.Log("Velopack: 新しいバージョンを検出しました。更新をダウンロードしています...");

            await _updateManager.DownloadUpdatesAsync(updateInfo);

            // チェック時刻を記録
            settings.LastUpdateCheckTime = DateTime.Now.ToString("o");
            settings.Save();

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
    /// <param name="compressionFormat">圧縮形式（"default"の場合は展開、具体的な形式の場合は圧縮）</param>
    /// <param name="shouldShutdown">処理終了後にアプリケーションを終了するかどうか</param>
    private async void ProcessCommandLineFile(string path, string compressionFormat = "default", bool shouldShutdown = true)
    {
        try
        {
            Logger.Log($"コマンドラインから処理を開始: {path}, 圧縮形式: {compressionFormat}, 終了フラグ: {shouldShutdown}");

            // パスが存在するかチェック
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                Logger.Log($"指定されたパスが存在しません: {path}");
                MessageService.ShowError($"指定されたファイルまたはフォルダが見つかりません。\n{path}");
                if (shouldShutdown)
                {
                    Shutdown();
                }
                return;
            }

            // 設定を読み込み
            var settings = Settings.Load();

            // ファイルかフォルダかを判定して適切な処理を実行
            if (File.Exists(path))
            {
                // アーカイブファイル形式かどうかを判定
                if (ArchiveExtractor.IsSupportedArchiveType(path))
                {
                    // アーカイブファイルの場合
                    if (compressionFormat == "default")
                    {
                        // 圧縮形式が指定されていない場合は展開処理を実行
                        Logger.Log($"アーカイブファイルを展開処理します: {path}");
                        await ProcessFileExtraction(path, settings, shouldShutdown);
                    }
                    else
                    {
                        // 圧縮形式が指定されている場合は、アーカイブファイルそのものを圧縮
                        Logger.Log($"アーカイブファイルを{compressionFormat}で圧縮処理します: {path}");
                        await ProcessFileCompression(path, settings, compressionFormat, shouldShutdown);
                    }
                }
                else
                {
                    // 通常ファイルの場合は圧縮処理を実行
                    Logger.Log($"ファイルを圧縮処理します: {path}");
                    await ProcessFileCompression(path, settings, compressionFormat, shouldShutdown);
                }
            }
            else if (Directory.Exists(path))
            {
                // フォルダの場合は圧縮処理を実行
                Logger.Log($"フォルダを圧縮処理します: {path}");
                await ProcessFolderCompression(path, settings, compressionFormat, shouldShutdown);
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("コマンドライン処理でエラーが発生", ex);
            MessageService.ShowError($"処理中にエラーが発生しました。\n{ex.Message}");
            if (shouldShutdown)
            {
                Shutdown();
            }
        }
    }

    /// <summary>
    /// ファイルの展開処理を実行
    /// </summary>
    /// <param name="filePath">展開するファイルのパス</param>
    /// <param name="settings">アプリケーション設定</param>
    /// <param name="shouldShutdown">処理終了後にアプリケーションを終了するかどうか</param>
    private async Task ProcessFileExtraction(string filePath, Settings settings, bool shouldShutdown = true)
    {
        try
        {
            var outputDir = settings.ExtractionOutputDirectory;
            var outputToSameDirectory = settings.ExtractionOutputToSameDirectory;

            // 進行状況ウィンドウを表示
            var progressWindow = new View.ProgressWindow("展開");
            progressWindow.Owner = MainWindow;
            progressWindow.WindowStartupLocation = progressWindow.Owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            
            var cancellationTokenSource = new CancellationTokenSource();
            progressWindow.CancelRequested += (_, _) => cancellationTokenSource.Cancel();
            progressWindow.Show();
            progressWindow.Activate();

            // UIスレッドに一度制御を戻し、ウィンドウの描画と初期化を完了させる
            await Task.Yield();

            // 共通化された展開処理を実行
            var success = await ArchiveProcessor.ExtractArchiveAsync(filePath, outputDir, outputToSameDirectory, progressWindow, cancellationTokenSource.Token);

            if (success)
            {
                Logger.Log("ファイル展開処理が完了しました");

                // 展開後にフォルダを開く設定を確認
                if (settings.OpenExtractionOutputFolder)
                {
                    OpenExtractedFolder(filePath, outputDir, outputToSameDirectory);
                }
            }
            else
            {
                Logger.Log("ファイル展開処理が失敗しました");
            }

            // 必要に応じてアプリケーションを終了
            if (shouldShutdown)
            {
                Shutdown();
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("ファイル展開処理でエラーが発生", ex);
            MessageService.ShowError($"展開中にエラーが発生しました。\n{ex.Message}");
            if (shouldShutdown)
            {
                Shutdown();
            }
        }
    }

    /// <summary>
    /// ファイルの圧縮処理を実行
    /// </summary>
    /// <param name="filePath">圧縮するファイルのパス</param>
    /// <param name="settings">アプリケーション設定</param>
    /// <param name="compressionFormat">圧縮形式（"default"の場合は設定から取得）</param>
    /// <param name="shouldShutdown">処理終了後にアプリケーションを終了するかどうか</param>
    private async Task ProcessFileCompression(string filePath, Settings settings, string compressionFormat = "default", bool shouldShutdown = true)
    {
        try
        {
            var outputDir = settings.CompressionOutputDirectory;
            var outputToSameDirectory = settings.CompressionOutputToSameDirectory;
            var format = compressionFormat == "default" ? settings.CompressionFormat : compressionFormat;

            // 進行状況ウィンドウを表示
            var progressWindow = new View.ProgressWindow("圧縮");
            progressWindow.Owner = MainWindow;
            progressWindow.WindowStartupLocation = progressWindow.Owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            
            var cancellationTokenSource = new CancellationTokenSource();
            progressWindow.CancelRequested += (_, _) => cancellationTokenSource.Cancel();
            progressWindow.Show();
            progressWindow.Activate();

            // UIスレッドに一度制御を戻し、ウィンドウの描画と初期化を完了させる
            await Task.Yield();

            // 出力パスを取得
            var outputPath = ArchiveCompressor.GetCompressedFileName(filePath, format, outputDir, outputToSameDirectory);

            // 出力ファイルが既に存在する場合は削除（上書き）
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            var progress = new Progress<ProgressInfo>(info =>
            {
                progressWindow.UpdateProgress(info.Percentage);
            });

            await ArchiveCompressor.CompressAsync(filePath, outputPath, format, progress, cancellationTokenSource.Token);

            Logger.Log("ファイル圧縮処理が完了しました");

            // 圧縮後にフォルダを開く設定を確認
            if (settings.OpenCompressionOutputFolder)
            {
                FolderOpener.OpenFolder(outputDir);
            }

            // 必要に応じてアプリケーションを終了
            if (shouldShutdown)
            {
                Shutdown();
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Log("ファイル圧縮処理がキャンセルされました");
            MessageService.ShowInfo("圧縮処理をキャンセルしました。", "キャンセル");
            if (shouldShutdown)
            {
                Shutdown();
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("ファイル圧縮処理でエラーが発生", ex);
            MessageService.ShowError($"圧縮中にエラーが発生しました。\n{ex.Message}");
            if (shouldShutdown)
            {
                Shutdown();
            }
        }
    }

    /// <summary>
    /// 展開されたフォルダを開く
    /// </summary>
    private void OpenExtractedFolder(string archivePath, string outputDir, bool outputToSameDirectory)
    {
        var extractionPath = ArchiveExtractor.GetOutputDirectory(archivePath, outputDir, outputToSameDirectory);
        if (!string.IsNullOrWhiteSpace(extractionPath) && Directory.Exists(extractionPath))
        {
            FolderOpener.OpenFolder(extractionPath);
        }
    }

    /// <summary>
    /// フォルダの圧縮処理を実行
    /// </summary>
    /// <param name="folderPath">圧縮するフォルダのパス</param>
    /// <param name="settings">アプリケーション設定</param>
    /// <param name="compressionFormat">圧縮形式（"default"の場合は設定から取得）</param>
    /// <param name="shouldShutdown">処理終了後にアプリケーションを終了するかどうか</param>
    private async Task ProcessFolderCompression(string folderPath, Settings settings, string compressionFormat = "default", bool shouldShutdown = true)
    {
        try
        {
            var outputDir = settings.CompressionOutputDirectory;
            var outputToSameDirectory = settings.CompressionOutputToSameDirectory;
            var format = compressionFormat == "default" ? settings.CompressionFormat : compressionFormat;

            // 進行状況ウィンドウを表示
            var progressWindow = new View.ProgressWindow("圧縮");
            progressWindow.Owner = MainWindow;
            progressWindow.WindowStartupLocation = progressWindow.Owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            
            var cancellationTokenSource = new CancellationTokenSource();
            progressWindow.CancelRequested += (_, _) => cancellationTokenSource.Cancel();
            progressWindow.Show();
            progressWindow.Activate();

            // UIスレッドに一度制御を戻し、ウィンドウの描画と初期化を完了させる
            await Task.Yield();

            // 共通化された圧縮処理を実行
            var success = await ArchiveProcessor.CompressItemAsync(folderPath, outputDir, outputToSameDirectory, format, progressWindow, null, cancellationTokenSource.Token);

            if (success)
            {
                Logger.Log("フォルダ圧縮処理が完了しました");

                // 圧縮後にフォルダを開く設定を確認
                if (settings.OpenCompressionOutputFolder)
                {
                    FolderOpener.OpenFolder(outputDir);
                }
            }
            else
            {
                Logger.Log("フォルダ圧縮処理が失敗しました");
            }

            // 必要に応じてアプリケーションを終了
            if (shouldShutdown)
            {
                Shutdown();
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("フォルダ圧縮処理でエラーが発生", ex);
            MessageService.ShowError($"圧縮中にエラーが発生しました。\n{ex.Message}");
            if (shouldShutdown)
            {
                Shutdown();
            }
        }
    }

    /// <summary>
    /// IPC 経由で引数を受信したときの処理
    /// </summary>
    /// <param name="args">受信した引数</param>
    private void OnArgsReceived(string[] args)
    {
        Dispatcher.BeginInvoke(() =>
        {
            Logger.Log("IPC経由でコマンドライン引数を受信しました。");
            
            if (args.Length > 0)
            {
                // 引数から圧縮形式とファイルパスを抽出
                var compressionFormat = "default";
                var filePath = args[0];

                if (args.Length >= 3 && args[0] == "--format")
                {
                    compressionFormat = args[1];
                    filePath = args[2];
                }

                // メインウィンドウを前面に出す
                if (MainWindow != null)
                {
                    if (MainWindow.WindowState == WindowState.Minimized)
                    {
                        MainWindow.WindowState = WindowState.Normal;
                    }
                    MainWindow.Activate();
                    MainWindow.Focus();
                }

                // 受信した引数で処理を実行
                // IPC 経由の場合は処理終了後にアプリを終了させないようにする
                ProcessCommandLineFile(filePath, compressionFormat, false);
            }
        });
    }

    /// <summary>
    /// UIスレッドで発生した未処理の例外をハンドル
    /// </summary>
    /// <param name="sender">イベント送信元</param>
    /// <param name="e">ディスパッチャー未処理例外イベント引数</param>
    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        // エラー内容をログに保存（これで本当の原因がわかります）
        Logger.LogException("UIスレッドで未処理の例外が発生しました", e.Exception);

        // ユーザーにエラーを通知
        MessageService.ShowError($"予期しないエラーが発生しました。\n\n詳細: {e.Exception.Message}");

        // これを true にすると、アプリがクラッシュして消えるのを防げます
        e.Handled = true;
    }

    /// <summary>
    /// バックグラウンドタスクで発生した未処理の例外をハンドル
    /// </summary>
    /// <param name="sender">イベント送信元</param>
    /// <param name="e">未観察のタスク例外イベント引数</param>
    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logger.LogException("バックグラウンドタスクで未処理の例外が発生しました", e.Exception);

        // エラーを「確認済み」にすることで、プロセスの強制終了を防ぎます
        e.SetObserved();
    }

    /// <summary>
    /// その他の致命的なエラーをハンドル
    /// </summary>
    /// <param name="sender">イベント送信元</param>
    /// <param name="e">未処理例外イベント引数</param>
    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Logger.LogException("致命的なエラーが発生しました（AppDomain）", ex);
            // このレベルのエラーは回復不能な場合が多いですが、ログには残します
        }
    }

    /// <summary>
    /// アプリケーション終了時の処理
    /// </summary>
    /// <param name="e">終了イベント引数</param>
    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Log($"アプリケーション終了: 終了コード = {e.ApplicationExitCode}");

        // IPC サーバーを停止
        _ipcCts?.Cancel();
        _ipcCts?.Dispose();

        // Mutex をリリース
        _instanceMutex?.Dispose();

        base.OnExit(e);
    }
}

