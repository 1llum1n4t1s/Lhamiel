using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
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
    /// アプリケーションインスタンス管理用の Mutex
    /// </summary>
    private static Mutex? _instanceMutex;

    /// <summary>
    /// 更新チェックのタイムアウト時間（ミリ秒）
    /// </summary>
    private const int UpdateCheckTimeoutMs = 10000;

    /// <summary>
    /// アップデート適用前の進行中処理待機タイムアウト（分）
    /// </summary>
    private const int UpdateProcessingWaitTimeoutMinutes = 5;

    private readonly UpdateManager? _updateManager;

    /// <summary>
    /// IPC サーバーのキャンセル用トークンソース
    /// </summary>
    private CancellationTokenSource? _ipcCts;

    /// <summary>
    /// XAML の初期化
    /// </summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public App()
    {
        InitializeComponent();
        RequestedThemeVariant = null;
        // Log4netを早期に初期化
        Logger.Initialize();

        // 7z.dll をプロセスに固定して、アンロード時のクラッシュを防止
        NativeLibraryManager.Initialize();

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
    /// 処理（圧縮・展開）の完了状態を管理するイベント
    /// </summary>
    public static AsyncManualResetEvent ProcessingCompletionEvent { get; } = new(true);

    /// <summary>
    /// プログレスウィンドウが開かれたときに呼び出されます
    /// </summary>
    public static void NotifyProgressStarted()
    {
        // イベントをリセットして非完了状態にする
        ProcessingCompletionEvent.Reset();
    }

    /// <summary>
    /// プログレスウィンドウが閉じられたときに呼び出されます
    /// </summary>
    public static void NotifyProgressFinished()
    {
        // UIスレッドで現在開いているプログレスウィンドウがないか確認
        // AvaloniaではApplication.Current経由でアクセス
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Dispatcher.UIThread.Post(() =>
            {
                // 他に ProgressWindow が残っていない場合、全ての処理が完了したとみなしてイベントをセットする
                var hasProgressWindow = desktop.Windows.OfType<View.ProgressWindow>().Any();
                if (!hasProgressWindow)
                {
                    ProcessingCompletionEvent.Set();
                }
            });
        }
    }

    /// <summary>
    /// アップデートによる再起動が予定されているかどうかを取得します。
    /// これが true の場合、新しい圧縮・展開処理の開始を抑制します。
    /// </summary>
    public bool IsUpdateRestarting { get; private set; }

    /// <summary>
    /// アプリケーション起動時の処理
    /// </summary>
    /// <param name="e">起動イベント引数</param>
    public override async void OnFrameworkInitializationCompleted()
    {
        // 1. UIスレッド（画面操作など）で発生した未処理の例外をキャッチする
        // Avaloniaでは例外ハンドリングは別の方法で行う

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
                var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
                if (args.Length > 0)
                {
                    Logger.Log("コマンドライン引数を既存のインスタンスに送信します。");
                    await IpcService.SendArgsToExistingInstanceAsync(args);
                }

                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
                return;
            }

            // 初回起動時は IPC サーバーを開始して後続インスタンスからの引数を待機
            _ipcCts = new CancellationTokenSource();
            _ = IpcService.StartServerAsync(OnArgsReceived, _ipcCts.Token);

            base.OnFrameworkInitializationCompleted();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifecycle)
            {
                desktopLifecycle.Exit += (_, _) => OnApplicationExiting();
            }

            // 起動ログを出力
            var startupArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
            Logger.LogStartup(startupArgs);

            // 更新チェックをバックグラウンドで開始（起動を妨げない）
            _ = CheckAndApplyUpdatesAsync();

            // コマンドライン引数をチェック
            if (startupArgs.Length > 0)
            {
                // 引数から圧縮形式とファイルパスを抽出
                var compressionFormat = "default";
                var filePath = startupArgs[0];

                // --format オプションがある場合は圧縮形式を取得
                if (startupArgs.Length >= 3 && startupArgs[0] == "--format")
                {
                    compressionFormat = startupArgs[1];
                    filePath = startupArgs[2];
                }

                _ = ProcessCommandLineFile(filePath, compressionFormat);
            }
            else
            {
                // 引数がない場合はメインウィンドウを表示
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    lifetime.MainWindow = new View.MainWindow();
                }
            }
        }
        catch (Exception ex)
        {
            // ログファイルに直接書き込み（Loggerが使えない場合のため）
            var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lhamiel");
            if (!Directory.Exists(appDataDir))
            {
                Directory.CreateDirectory(appDataDir);
            }
            var logPath = Path.Combine(appDataDir, "error.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] アプリケーション起動エラー: {ex}\n");
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
            {
                lifetime.Shutdown();
            }
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

            Logger.Log("Velopack: ダウンロード完了。更新を適用します。");

            // 再起動フラグを立てて、新しい処理が開始されないようにする
            IsUpdateRestarting = true;

            // 進行中の処理（圧縮・展開）が完了するのを待機します
            Logger.Log("Velopack: 進行中の処理の完了を待機しています...");
            try
            {
                // 定義されたタイムアウト時間を設定
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(UpdateProcessingWaitTimeoutMinutes));

                // イベントがセットされるのを待つ（実行中の処理がなければ即時完了）
                await ProcessingCompletionEvent.WaitAsync(timeoutCts.Token);

                Logger.Log("Velopack: 処理が完了しました。再起動して更新を適用します。");
                _updateManager.ApplyUpdatesAndRestart(updateInfo);
                return true;
            }
            catch (OperationCanceledException)
            {
                // タイムアウトした場合は、更新を中止してユーザーに通知
                Logger.Log("Velopack: 処理完了の待機がタイムアウトしました。今回のアップデート適用は中止します。", LogLevel.Warning);
                MessageService.ShowWarning("進行中の処理が完了しなかったため、アップデートの適用を中止しました。アプリケーションを終了してから、再度お試しください。");
                IsUpdateRestarting = false; // 更新プロセスを中止し、通常の動作に戻す
                return false; // 更新失敗として終了
            }
        }
        catch (OperationCanceledException)
        {
            // 待機中にキャンセルされた場合もフラグをリセットして通常動作を継続可能にする
            IsUpdateRestarting = false;
            Logger.Log("Velopack: 更新チェックがタイムアウトしました。アプリケーションを続行します。");
            return false;
        }
        catch (Exception ex)
        {
            // 更新の適用（再起動の準備）に失敗した場合はフラグをリセットして通常動作を継続可能にする
            IsUpdateRestarting = false;
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
            var settings = SettingsManager.Instance.Current;
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
    private async Task ProcessCommandLineFile(string path, string compressionFormat = "default", bool shouldShutdown = true)
    {
        try
        {
            // 再起動が予定されている場合は、新しい処理を開始しない
            if (IsUpdateRestarting)
            {
                Logger.Log("アップデートのための再起動が予定されているため、新しい処理をスキップします。");
                MessageService.ShowWarning("アップデートの適用準備が整いました。再起動後に再度お試しください。");
                return;
            }

            Logger.Log($"コマンドラインから処理を開始: {path}, 圧縮形式: {compressionFormat}, 終了フラグ: {shouldShutdown}");

            // パスが存在するかチェック
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                Logger.Log($"指定されたパスが存在しません: {path}");
                MessageService.ShowError($"指定されたファイルまたはフォルダが見つかりません。\n{path}");
                ShutdownIfNeeded(shouldShutdown);
                return;
            }

            // 設定を読み込み
            var settings = SettingsManager.Instance.Current;

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
            ShutdownIfNeeded(shouldShutdown);
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
        View.ProgressWindow? progressWindow = null;
        try
        {
            var outputDir = settings.ExtractionOutputDirectory;
            var outputToSameDirectory = settings.ExtractionOutputToSameDirectory;

            // 進行状況ウィンドウを表示
            progressWindow = new View.ProgressWindow("展開");

            // MainWindowが自分自身（progressWindow）でない場合のみOwnerに設定
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null && desktop.MainWindow != progressWindow)
            {
                progressWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                progressWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            using var cancellationTokenSource = new CancellationTokenSource();

            // キャンセル要求時のイベントハンドラを定義
            EventHandler cancelHandler = (_, _) =>
            {
                try
                {
                    // ReSharper disable once AccessToDisposedClosure
                    if (!cancellationTokenSource.IsCancellationRequested)
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        cancellationTokenSource.Cancel();
                    }
                }
                catch (ObjectDisposedException)
                {
                    // CTSが既に破棄されている場合は無視
                }
            };

            try
            {
                progressWindow.CancelRequested += cancelHandler;
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
            }
            finally
            {
                // イベントハンドラを解除（CTSの破棄前に確実に実行）
                progressWindow.CancelRequested -= cancelHandler;
            }

            // 必要に応じてアプリケーションを終了
            ShutdownIfNeeded(shouldShutdown);
        }
        catch (OperationCanceledException)
        {
            Logger.Log("ファイル展開処理がキャンセルされました");
            progressWindow?.CloseSafe();
            ShutdownIfNeeded(shouldShutdown);
        }
        catch (Exception ex)
        {
            Logger.LogException("ファイル展開処理でエラーが発生", ex);
            MessageService.ShowError($"展開中にエラーが発生しました。\n{ex.Message}");
            ShutdownIfNeeded(shouldShutdown);
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
        View.ProgressWindow? progressWindow = null;
        try
        {
            var outputDir = settings.CompressionOutputDirectory;
            var outputToSameDirectory = settings.CompressionOutputToSameDirectory;
            var format = compressionFormat == "default" ? settings.CompressionFormat : compressionFormat;

            // 進行状況ウィンドウを表示
            progressWindow = new View.ProgressWindow("圧縮");

            // MainWindowが自分自身（progressWindow）でない場合のみOwnerに設定
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null && desktop.MainWindow != progressWindow)
            {
                progressWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                progressWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            using var cancellationTokenSource = new CancellationTokenSource();

            // キャンセル要求時のイベントハンドラを定義
            EventHandler cancelHandler = (_, _) =>
            {
                try
                {
                    // ReSharper disable once AccessToDisposedClosure
                    if (!cancellationTokenSource.IsCancellationRequested)
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        cancellationTokenSource.Cancel();
                    }
                }
                catch (ObjectDisposedException)
                {
                    // CTSが既に破棄されている場合は無視
                }
            };

            try
            {
                progressWindow.CancelRequested += cancelHandler;
                progressWindow.Show();
                progressWindow.Activate();

                // UIスレッドに一度制御を戻し、ウィンドウの描画と初期化を完了させる
                await Task.Yield();

                // 共通化された圧縮処理を実行
                var success = await ArchiveProcessor.CompressItemAsync(filePath, outputDir, outputToSameDirectory, format, progressWindow, null, cancellationTokenSource.Token);

                if (success)
                {
                    Logger.Log("ファイル圧縮処理が完了しました");

                    // 圧縮後にフォルダを開く設定を確認
                    if (settings.OpenCompressionOutputFolder)
                    {
                        // 実際にファイルが作成されたフォルダを開く
                        var finalOutputPath = ArchiveCompressor.GetCompressedFileName(filePath, format, outputDir, outputToSameDirectory);
                        var directoryToOpen = Path.GetDirectoryName(finalOutputPath);
                        if (directoryToOpen != null)
                        {
                            FolderOpener.OpenFolder(directoryToOpen);
                        }
                    }
                }
                else
                {
                    Logger.Log("ファイル圧縮処理が失敗しました");
                }
            }
            finally
            {
                // イベントハンドラを解除（CTSの破棄前に確実に実行）
                progressWindow.CancelRequested -= cancelHandler;
            }

            // 必要に応じてアプリケーションを終了
            ShutdownIfNeeded(shouldShutdown);
        }
        catch (OperationCanceledException)
        {
            Logger.Log("ファイル圧縮処理がキャンセルされました");
            progressWindow?.CloseSafe();
            ShutdownIfNeeded(shouldShutdown);
        }
        catch (Exception ex)
        {
            Logger.LogException("ファイル圧縮処理でエラーが発生", ex);
            MessageService.ShowError($"圧縮中にエラーが発生しました。\n{ex.Message}");
            ShutdownIfNeeded(shouldShutdown);
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
    /// 条件に応じてアプリケーションを終了する
    /// </summary>
    /// <param name="shouldShutdown">終了フラグ</param>
    private void ShutdownIfNeeded(bool shouldShutdown)
    {
        if (shouldShutdown && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var hasVisibleWindow = desktop.Windows.OfType<Window>().Any(w => w.IsVisible);
            if (!hasVisibleWindow)
            {
                desktop.Shutdown();
            }
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
        View.ProgressWindow? progressWindow = null;
        try
        {
            var outputDir = settings.CompressionOutputDirectory;
            var outputToSameDirectory = settings.CompressionOutputToSameDirectory;
            var format = compressionFormat == "default" ? settings.CompressionFormat : compressionFormat;

            // 進行状況ウィンドウを表示
            progressWindow = new View.ProgressWindow("圧縮");

            // MainWindowが自分自身（progressWindow）でない場合のみOwnerに設定
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null && desktop.MainWindow != progressWindow)
            {
                progressWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                progressWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            using var cancellationTokenSource = new CancellationTokenSource();

            // キャンセル要求時のイベントハンドラを定義
            EventHandler cancelHandler = (_, _) =>
            {
                try
                {
                    // ReSharper disable once AccessToDisposedClosure
                    if (!cancellationTokenSource.IsCancellationRequested)
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        cancellationTokenSource.Cancel();
                    }
                }
                catch (ObjectDisposedException)
                {
                    // CTSが既に破棄されている場合は無視
                }
            };

            try
            {
                progressWindow.CancelRequested += cancelHandler;
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
                        // 実際にファイルが作成されたフォルダを開く
                        var finalOutputPath = ArchiveCompressor.GetCompressedFileName(folderPath, format, outputDir, outputToSameDirectory);
                        var directoryToOpen = Path.GetDirectoryName(finalOutputPath);
                        if (directoryToOpen != null)
                        {
                            FolderOpener.OpenFolder(directoryToOpen);
                        }
                    }
                }
                else
                {
                    Logger.Log("フォルダ圧縮処理が失敗しました");
                }
            }
            finally
            {
                // イベントハンドラを解除（CTSの破棄前に確実に実行）
                progressWindow.CancelRequested -= cancelHandler;
            }

            // 必要に応じてアプリケーションを終了
            ShutdownIfNeeded(shouldShutdown);
        }
        catch (OperationCanceledException)
        {
            Logger.Log("フォルダ圧縮処理がキャンセルされました");
            progressWindow?.CloseSafe();
            ShutdownIfNeeded(shouldShutdown);
        }
        catch (Exception ex)
        {
            Logger.LogException("フォルダ圧縮処理でエラーが発生", ex);
            MessageService.ShowError($"圧縮中にエラーが発生しました。\n{ex.Message}");
            ShutdownIfNeeded(shouldShutdown);
        }
    }

    /// <summary>
    /// IPC 経由で引数を受信したときの処理
    /// </summary>
    /// <param name="args">受信した引数</param>
    private void OnArgsReceived(string[] args)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Dispatcher.UIThread.Post(() =>
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
                    if (desktop.MainWindow != null)
                    {
                        if (desktop.MainWindow.WindowState == WindowState.Minimized)
                        {
                            desktop.MainWindow.WindowState = WindowState.Normal;
                        }
                        desktop.MainWindow.Activate();
                        desktop.MainWindow.Focus();
                    }

                    // 受信した引数で処理を実行
                    // IPC 経由の場合は処理終了後にアプリを終了させないようにする
                    _ = ProcessCommandLineFile(filePath, compressionFormat, false);
                }
            });
        }
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
    public void OnApplicationExiting()
    {
        Logger.Log("アプリケーション終了");

        // IPC サーバーを停止
        _ipcCts?.Cancel();
        _ipcCts?.Dispose();

        // Mutex をリリース
        _instanceMutex?.Dispose();
    }
}

