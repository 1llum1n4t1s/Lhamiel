using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Lhamiel.Util;
using Lhamiel.View;
using System.Diagnostics;
using System.Globalization;
namespace Lhamiel;

/// <summary>
/// App.xaml の相互作用ロジック
/// </summary>
public class App : Application
{
    /// <summary>
    /// アプリケーションインスタンス管理用の Mutex
    /// </summary>
    private static Mutex? _instanceMutex;

    /// <summary>
    /// IPC サーバーのキャンセル用トークンソース
    /// </summary>
    private CancellationTokenSource? _ipcCts;

    /// <summary>
    /// 現在アクティブなロケール辞書
    /// </summary>
    private IResourceProvider? _activeLocale;

    /// <summary>
    /// サポートされているロケール一覧
    /// </summary>
    public static readonly string[] SupportedLocales =
    [
        "en_US", "ja_JP", "zh_CN", "zh_TW", "de_DE", "fr_FR", "es_ES",
        "it_IT", "pt_BR", "ru_RU", "uk_UA", "id_ID", "fil_PH", "ta_IN", "ko_KR",
        "la_VA", "sa_IN"
    ];

    /// <summary>
    /// ロケール表示名（ネイティブ言語名）
    /// </summary>
    public static readonly Dictionary<string, string> LocaleDisplayNames = new()
    {
        ["en_US"] = "English",
        ["ja_JP"] = "日本語",
        ["zh_CN"] = "简体中文",
        ["zh_TW"] = "繁體中文",
        ["de_DE"] = "Deutsch",
        ["fr_FR"] = "Français",
        ["es_ES"] = "Español",
        ["it_IT"] = "Italiano",
        ["pt_BR"] = "Português (Brasil)",
        ["ru_RU"] = "Русский",
        ["uk_UA"] = "Українська",
        ["id_ID"] = "Bahasa Indonesia",
        ["fil_PH"] = "Tagalog",
        ["ta_IN"] = "தமிழ்",
        ["ko_KR"] = "한국어",
        ["la_VA"] = "Latina",
        ["sa_IN"] = "संस्कृतम्"
    };

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

        // SettingsManager を先に初期化（コンストラクタ内で Logger.Initialize も実行される）
        var settings = SettingsManager.Instance.Current;

        // テーマ設定を適用
        RequestedThemeVariant = GetThemeVariant(settings.Theme);

        // ロケール設定を適用（コンストラクタ内では Application.Current が未設定のため this を直接使用）
        var locale = string.IsNullOrEmpty(settings.Locale) ? DetectDefaultLocale() : settings.Locale;
        ApplyLocale(locale);

        // 7z.dll をプロセスに固定して、アンロード時のクラッシュを防止
        NativeLibraryManager.Initialize();
    }

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
            // メソッド冒頭でコマンドライン引数を一度取得
            var startupArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

            // メインウィンドウの多重起動チェック
            const string mutexName = "Lhamiel_MainWindow_SingleInstance";

            try
            {
                _instanceMutex = new Mutex(true, mutexName, out var createdNew);

                if (!createdNew)
                {
                    // 既に起動しているインスタンスがある場合
                    Logger.Log("アプリケーションは既に起動しています。既存のインスタンスをアクティブ化します。");
                    ActivateExistingInstance();

                    if (startupArgs.Length > 0)
                    {
                        Logger.Log("コマンドライン引数を既存のインスタンスに送信します。");
                        await IpcService.SendArgsToExistingInstanceAsync(startupArgs);
                    }

                    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        // 同期で Shutdown すると StartWithClassicDesktopLifetime と競合するため、
                        // Dispatcher 上で後続に実行して初期化が正常に返るようにする
                        Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                desktop.Shutdown();
                            }
                            catch (InvalidOperationException)
                            {
                            }
                        });
                    }
                    return;
                }
            }
            catch (AbandonedMutexException)
            {
                Logger.Log("前回のアプリケーション終了時に Mutex が正常にリリースされていません。Mutex を再取得しました。");
            }
            catch (Exception ex)
            {
                Logger.Log($"Mutex 初期化エラー: {ex.Message}");
            }

            // 初回起動時は IPC サーバーを開始して後続インスタンスからの引数を待機
            _ipcCts = new CancellationTokenSource();
            _ = IpcService.StartServerAsync(OnArgsReceived, _ipcCts.Token);

            base.OnFrameworkInitializationCompleted();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifecycle)
            {
                desktopLifecycle.Exit += (_, _) => OnApplicationExiting();
            }

            Logger.LogStartup(startupArgs);

            if (startupArgs.Length > 0)
            {
                // 関連付けから起動：更新チェックは行わず、プログレスバー画面のみ表示
                var (compressionFormat, filePaths) = ParseCommandLineArgs(startupArgs);
                await ProcessCommandLineFiles(filePaths, compressionFormat);
            }
            else
            {
                // メイン画面起動
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    try
                    {
                        lifetime.MainWindow = new MainWindow();
                    }
                    catch (Exception windowEx)
                    {
                        Logger.LogException("メインウィンドウの作成に失敗しました（グラフィックス初期化などの可能性）", windowEx);
                        TryShutdownSafely(lifetime);
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lhamiel");
            if (!Directory.Exists(appDataDir))
            {
                Directory.CreateDirectory(appDataDir);
            }
            var logPath = Path.Combine(appDataDir, "error.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] アプリケーション起動エラー: {ex}\n");
            try
            {
                Logger.LogException("OnFrameworkInitializationCompleted でエラー", ex);
            }
            catch
            {
            }

            // Dispatcher が既にシャットダウンしている場合は Shutdown() を呼ばない
            // （呼ぶと二重終了や InvalidOperationException の原因になる）
            var isDispatcherShutDown = ex is InvalidOperationException && ex.Message.Contains("Dispatcher", StringComparison.OrdinalIgnoreCase);
            if (!isDispatcherShutDown && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
            {
                TryShutdownSafely(lifetime);
            }
        }
    }

    /// <summary>
    /// Dispatcher シャットダウン済みなどを考慮して安全に Shutdown を試行する
    /// </summary>
    /// <param name="lifetime">デスクトップライフタイム</param>
    private static void TryShutdownSafely(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        try
        {
            lifetime.Shutdown();
        }
        catch (InvalidOperationException e) when (e.Message.Contains("Dispatcher", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Logger.Log("Shutdown をスキップしました（Dispatcher 終了済み）", LogLevel.Warning);
            }
            catch
            {
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
            var currentProcess = Process.GetCurrentProcess();
            var otherProcess = Process.GetProcessesByName(currentProcess.ProcessName).FirstOrDefault(p => p.Id != currentProcess.Id);

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
    /// コマンドライン引数を解析して圧縮形式とファイルパスのリストを返す
    /// </summary>
    private static (string compressionFormat, string[] filePaths) ParseCommandLineArgs(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--format")
            return (args[1], args[2..]);
        return ("default", args.Where(a => !a.StartsWith("--")).ToArray());
    }

    /// <summary>
    /// コマンドライン経由の複数ファイル処理。まとめ圧縮設定に応じて分岐する。
    /// </summary>
    private async Task ProcessCommandLineFiles(string[] filePaths, string compressionFormat = "default", bool shouldShutdown = true)
    {
        if (filePaths.Length == 0) return;

        // 単一ファイルの場合は従来の処理
        if (filePaths.Length == 1)
        {
            await ProcessCommandLineFile(filePaths[0], compressionFormat, shouldShutdown);
            return;
        }

        // 複数ファイルの場合: すべて圧縮対象（展開/圧縮の自動判定は単一ファイル時のみ）
        var filesToCompress = new List<string>();
        foreach (var path in filePaths)
        {
            if (Directory.Exists(path) || File.Exists(path))
                filesToCompress.Add(path);
            else
                Logger.Log($"指定されたパスが存在しません: {path}");
        }

        if (filesToCompress.Count == 0) return;

        var settings = SettingsManager.Instance.Current;
        var format = compressionFormat == "default" ? settings.CompressionFormat : compressionFormat;

        if (settings.CompressMultipleAsOne && filesToCompress.Count > 1)
        {
            // まとめ圧縮
            await ProcessMergedCompression(filesToCompress.ToArray(), settings, format, shouldShutdown);
        }
        else
        {
            // 個別に圧縮
            for (var i = 0; i < filesToCompress.Count; i++)
            {
                var isLast = i == filesToCompress.Count - 1;
                await ProcessCommandLineFile(filesToCompress[i], compressionFormat, isLast && shouldShutdown);
            }
        }
    }

    /// <summary>
    /// 複数ファイルをまとめて1つのアーカイブに圧縮
    /// </summary>
    private async Task ProcessMergedCompression(string[] sourcePaths, Settings settings, string format, bool shouldShutdown = true)
    {
        ProgressWindow? progressWindow = null;
        try
        {
            Logger.Log($"コマンドラインからまとめ圧縮を開始: {sourcePaths.Length}個の対象、形式={format}");

            var outputDir = settings.CompressionOutputDirectory;
            var outputToSameDirectory = settings.CompressionOutputToSameDirectory;

            (progressWindow, var cancellationTokenSource, var cancelHandler) = SetupProgressWindow(App.Text("Progress.Processing"));

            using (cancellationTokenSource)
            {
                try
                {
                    progressWindow.CancelRequested += cancelHandler;
                    progressWindow.Show();
                    progressWindow.Activate();
                    await Task.Yield();

                    var success = await ArchiveProcessor.CompressMergedAsync(
                        sourcePaths, outputDir, outputToSameDirectory, format,
                        progressWindow, cancellationTokenSource.Token);

                    if (success)
                    {
                        Logger.Log("まとめ圧縮処理が完了しました");
                        if (settings.OpenCompressionOutputFolder)
                        {
                            var baseDir = outputToSameDirectory
                                ? Path.GetDirectoryName(sourcePaths[0]) ?? ""
                                : outputDir;
                            FolderOpener.OpenFolder(baseDir);
                        }
                    }
                    else
                    {
                        Logger.Log("まとめ圧縮処理が失敗しました");
                    }
                }
                finally
                {
                    progressWindow.CancelRequested -= cancelHandler;
                }
            }

            ShutdownIfNeeded(shouldShutdown);
        }
        catch (OperationCanceledException)
        {
            Logger.Log("まとめ圧縮処理がキャンセルされました");
            progressWindow?.CloseSafe();
            ShutdownIfNeeded(shouldShutdown);
        }
        catch (Exception ex)
        {
            Logger.LogException("まとめ圧縮処理でエラーが発生", ex);
            _ = MessageService.ShowError(App.Text("Error.DuringCompression", ex.Message));
            ShutdownIfNeeded(shouldShutdown);
        }
    }

    /// <summary>
    /// コマンドラインで指定されたファイルまたはフォルダの処理を実行（単一ファイル）
    /// </summary>
    /// <param name="path">処理するファイルまたはフォルダのパス</param>
    /// <param name="compressionFormat">圧縮形式（"default"の場合は展開、具体的な形式の場合は圧縮）</param>
    /// <param name="shouldShutdown">処理終了後にアプリケーションを終了するかどうか</param>
    private async Task ProcessCommandLineFile(string path, string compressionFormat = "default", bool shouldShutdown = true)
    {
        try
        {
            Logger.Log($"コマンドラインから処理を開始: {path}, 圧縮形式: {compressionFormat}, 終了フラグ: {shouldShutdown}");

            // パスが存在するかチェック
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                Logger.Log($"指定されたパスが存在しません: {path}");
                _ = MessageService.ShowError(App.Text("Error.FolderNotFound", path));
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
                        await ProcessCompression(path, settings, compressionFormat, shouldShutdown);
                    }
                }
                else
                {
                    // 通常ファイルの場合は圧縮処理を実行
                    Logger.Log($"ファイルを圧縮処理します: {path}");
                    await ProcessCompression(path, settings, compressionFormat, shouldShutdown);
                }
            }
            else if (Directory.Exists(path))
            {
                // フォルダの場合は圧縮処理を実行
                Logger.Log($"フォルダを圧縮処理します: {path}");
                await ProcessCompression(path, settings, compressionFormat, shouldShutdown);
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("コマンドライン処理でエラーが発生", ex);
            _ = MessageService.ShowError(App.Text("Error.DuringProcessing", ex.Message));
            ShutdownIfNeeded(shouldShutdown);
        }
    }

    /// <summary>
    /// ProgressWindow を初期化し、キャンセル処理をセットアップする
    /// </summary>
    /// <param name="operationType">操作タイプ（"展開"、"圧縮"など）</param>
    /// <returns>(progressWindow, cts, cancelHandler)</returns>
    private static (ProgressWindow progressWindow, CancellationTokenSource cts, EventHandler cancelHandler) SetupProgressWindow(string operationType)
    {
        var progressWindow = new ProgressWindow(operationType);
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null && desktop.MainWindow != progressWindow)
            progressWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        else
            progressWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var cts = new CancellationTokenSource();
        EventHandler cancelHandler = (_, _) =>
        {
            try
            {
                if (!cts.IsCancellationRequested)
                    cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // CTSが既に破棄されている場合は無視
            }
        };
        return (progressWindow, cts, cancelHandler);
    }

    /// <summary>
    /// ファイルの展開処理を実行
    /// </summary>
    /// <param name="filePath">展開するファイルのパス</param>
    /// <param name="settings">アプリケーション設定</param>
    /// <param name="shouldShutdown">処理終了後にアプリケーションを終了するかどうか</param>
    private async Task ProcessFileExtraction(string filePath, Settings settings, bool shouldShutdown = true)
    {
        ProgressWindow? progressWindow = null;
        try
        {
            var outputDir = settings.ExtractionOutputDirectory;
            var outputToSameDirectory = settings.ExtractionOutputToSameDirectory;

            (progressWindow, var cancellationTokenSource, var cancelHandler) = SetupProgressWindow(App.Text("Progress.Processing"));

            using (cancellationTokenSource)
            {
                try
                {
                    progressWindow.CancelRequested += cancelHandler;
                    progressWindow.Show();
                    progressWindow.Activate();
                    await Task.Yield();

                    var (finalOutputPath, structureInfo) = await ArchiveProcessor.ExtractArchiveAsync(filePath, outputDir, outputToSameDirectory, progressWindow, cancellationTokenSource.Token);

                    if (finalOutputPath != null)
                    {
                        Logger.Log("ファイル展開処理が完了しました");

                        // 展開後にフォルダを開く設定を確認
                        if (settings.OpenExtractionOutputFolder)
                        {
                            var pathToOpen = finalOutputPath;
                            // 単一ルート要素の場合、そのフォルダを直接開く
                            if (structureInfo != null && structureInfo.HasSingleRootItem && !string.IsNullOrEmpty(structureInfo.SingleRootItemName))
                            {
                                var possibleDir = Path.Combine(finalOutputPath, structureInfo.SingleRootItemName);
                                if (Directory.Exists(possibleDir))
                                {
                                    pathToOpen = possibleDir;
                                }
                            }

                            if (Directory.Exists(pathToOpen))
                            {
                                FolderOpener.OpenFolder(pathToOpen);
                            }
                        }
                    }
                    else
                    {
                        Logger.Log("ファイル展開処理が失敗しました");
                    }
                }
                finally
                {
                    progressWindow.CancelRequested -= cancelHandler;
                }
            }

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
            _ = MessageService.ShowError(App.Text("Error.DuringExtraction", ex.Message));
            ShutdownIfNeeded(shouldShutdown);
        }
    }

    /// <summary>
    /// ファイルまたはフォルダの圧縮処理を実行
    /// </summary>
    /// <param name="sourcePath">圧縮するファイルまたはフォルダのパス</param>
    /// <param name="settings">アプリケーション設定</param>
    /// <param name="compressionFormat">圧縮形式（"default"の場合は設定から取得）</param>
    /// <param name="shouldShutdown">処理終了後にアプリケーションを終了するかどうか</param>
    private async Task ProcessCompression(string sourcePath, Settings settings, string compressionFormat = "default", bool shouldShutdown = true)
    {
        ProgressWindow? progressWindow = null;
        try
        {
            var outputDir = settings.CompressionOutputDirectory;
            var outputToSameDirectory = settings.CompressionOutputToSameDirectory;
            var format = compressionFormat == "default" ? settings.CompressionFormat : compressionFormat;

            (progressWindow, var cancellationTokenSource, var cancelHandler) = SetupProgressWindow(App.Text("Progress.Processing"));

            using (cancellationTokenSource)
            {
                try
                {
                    progressWindow.CancelRequested += cancelHandler;
                    progressWindow.Show();
                    progressWindow.Activate();
                    await Task.Yield();

                    var success = await ArchiveProcessor.CompressItemAsync(sourcePath, outputDir, outputToSameDirectory, format, progressWindow, null, cancellationTokenSource.Token);

                    if (success)
                    {
                        Logger.Log("圧縮処理が完了しました");

                        if (settings.OpenCompressionOutputFolder)
                        {
                            var finalOutputPath = ArchiveCompressor.GetCompressedFileName(sourcePath, format, outputDir, outputToSameDirectory);
                            var directoryToOpen = Path.GetDirectoryName(finalOutputPath);
                            if (directoryToOpen != null)
                            {
                                FolderOpener.OpenFolder(directoryToOpen);
                            }
                        }
                    }
                    else
                    {
                        Logger.Log("圧縮処理が失敗しました");
                    }
                }
                finally
                {
                    progressWindow.CancelRequested -= cancelHandler;
                }
            }

            ShutdownIfNeeded(shouldShutdown);
        }
        catch (OperationCanceledException)
        {
            Logger.Log("圧縮処理がキャンセルされました");
            progressWindow?.CloseSafe();
            ShutdownIfNeeded(shouldShutdown);
        }
        catch (Exception ex)
        {
            Logger.LogException("圧縮処理でエラーが発生", ex);
            _ = MessageService.ShowError(App.Text("Error.DuringCompression", ex.Message));
            ShutdownIfNeeded(shouldShutdown);
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
                    var (compressionFormat, filePaths) = ParseCommandLineArgs(args);

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
                    _ = ProcessCommandLineFiles(filePaths, compressionFormat, false).ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            Logger.LogException("IPC経由の処理でエラーが発生", t.Exception!);
                    }, TaskScheduler.FromCurrentSynchronizationContext());
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

        try
        {
            _ipcCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            _ipcCts?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            _instanceMutex?.ReleaseMutex();
            Logger.Log("Mutex をリリースしました");
        }
        catch (Exception ex) when (ex is ApplicationException or ObjectDisposedException)
        {
        }

        try
        {
            _instanceMutex?.Dispose();
            Logger.Log("Mutex を破棄しました");
        }
        catch (ObjectDisposedException)
        {
        }

        Logger.Dispose();
    }

    /// <summary>
    /// テーマ文字列から ThemeVariant を取得する
    /// </summary>
    /// <param name="theme">テーマ名（"System", "Dark", "Light"）</param>
    private static ThemeVariant GetThemeVariant(string theme) => theme switch
    {
        "Light" => ThemeVariant.Light,
        "Dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default // "System" → OS追従
    };

    /// <summary>
    /// テーマを切り替える
    /// </summary>
    /// <param name="theme">テーマ名（"System", "Dark", "Light"）</param>
    public static void SetTheme(string theme)
    {
        if (Current is not App app)
            return;

        app.RequestedThemeVariant = GetThemeVariant(theme);
    }

    /// <summary>
    /// ロケールを切り替える（静的メソッド。Application.Current 経由でアクセス）
    /// </summary>
    /// <param name="localeKey">ロケールキー（"ja_JP", "en_US" など）</param>
    public static void SetLocale(string localeKey)
    {
        if (Current is App app)
            app.ApplyLocale(localeKey);
    }

    /// <summary>
    /// ロケールを実際に適用する（インスタンスメソッド。コンストラクタからも安全に呼べる）
    /// </summary>
    /// <param name="localeKey">ロケールキー（"ja_JP", "en_US" など）</param>
    private void ApplyLocale(string localeKey)
    {
        if (Resources[localeKey] is not IResourceProvider targetLocale ||
            targetLocale == _activeLocale)
            return;

        if (_activeLocale != null)
            Resources.MergedDictionaries.Remove(_activeLocale);

        Resources.MergedDictionaries.Add(targetLocale);
        _activeLocale = targetLocale;
    }

    /// <summary>
    /// リソースからローカライズ済みテキストを取得する
    /// </summary>
    /// <param name="key">リソースキー（"Text." プレフィックスなし）</param>
    /// <param name="args">フォーマット引数</param>
    /// <returns>ローカライズ済み文字列</returns>
    public static string Text(string key, params object[] args)
    {
        var fullKey = $"Text.{key}";
        string? fmt = null;

        // アクティブロケールから直接検索（MergedDictionaries経由のFindResourceより確実）
        if (Current is App app && app._activeLocale != null)
        {
            app._activeLocale.TryGetResource(fullKey, null, out var value);
            fmt = value as string;
        }

        // フォールバック: Application全体のリソースから検索
        if (string.IsNullOrWhiteSpace(fmt) && Current?.TryFindResource(fullKey, out var fallback) == true)
            fmt = fallback as string;

        if (string.IsNullOrWhiteSpace(fmt))
            return fullKey;

        // リテラルの \n を実際の改行に変換
        fmt = fmt.Replace("\\n", "\n");

        if (args == null || args.Length == 0)
            return fmt;

        return string.Format(fmt, args);
    }

    /// <summary>
    /// システムのカルチャからデフォルトロケールを検出する
    /// </summary>
    public static string DetectDefaultLocale()
    {
        var culture = CultureInfo.CurrentUICulture;
        var name = culture.Name.Replace('-', '_');

        // 完全一致
        if (SupportedLocales.Contains(name))
            return name;

        // 言語部分のみで一致（例: "ja" → "ja_JP"）
        var lang = culture.TwoLetterISOLanguageName;
        var match = SupportedLocales.FirstOrDefault(l => l.StartsWith(lang + "_", StringComparison.OrdinalIgnoreCase));
        return match ?? "en_US";
    }
}

