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
public partial class App : Application
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
        var settings = SettingsManager.Instance.CreateSnapshot();

        // テーマ設定を適用
        RequestedThemeVariant = GetThemeVariant(settings.Theme);

        // ロケール設定を適用（コンストラクタ内では Application.Current が未設定のため this を直接使用）
        var locale = string.IsNullOrEmpty(settings.Locale) ? DetectDefaultLocale() : settings.Locale;
        ApplyLocale(locale);

        // 7z.dll をプロセスに固定して、アンロード時のクラッシュを防止
        NativeLibraryManager.Initialize();

        // ライブラリ (1llum1n4t1s.Sevenzip / Cube.*) 内部ログを Lhamiel のログに転送する。
        // 既定では NullLoggerSource で捨てられるため、Configure しないと Open 失敗・圧縮リトライ等の
        // 診断が完全に失われる。Logger.Initialize は上の SettingsManager 初期化で完了済み。RTK レビュー #14 対応。
        Cube.Logger.Configure(new CubeLoggerBridge());
    }

    /// <summary>
    /// アプリケーション起動時の処理
    /// </summary>
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

            // メインウィンドウの多重起動チェック。
            // Local\ プレフィックスでセッションローカルに限定し、別セッション/別ユーザーによる
            // Mutex 先取りでのサービス妨害（DoS）を防ぐ。
            const string mutexName = @"Local\Lhamiel_MainWindow_SingleInstance";

            try
            {
                _instanceMutex = new Mutex(true, mutexName, out var createdNew);

                if (!createdNew)
                {
                    // 既に起動しているインスタンスがある場合
                    Logger.Log("アプリケーションは既に起動しています。既存のインスタンスをアクティブ化します。");
                    ActivateExistingInstance();

                    // 引数の有無に関わらず IPC を送る。空配列 = 「メイン画面を表示して前面化して」という
                    // 活性化要求として既存インスタンスに伝わる。関連付け / アイコンドロップ起動の圧縮中は
                    // 既存インスタンスに MainWindow が存在しないため、この経路だけがメイン画面を生成・表示できる。
                    Logger.Log(startupArgs.Length > 0
                        ? "コマンドライン引数を既存のインスタンスに送信します。"
                        : "活性化要求（引数なし）を既存のインスタンスに送信します。");
                    await IpcService.SendArgsToExistingInstanceAsync(startupArgs);

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
                // 前回プロセスが Release せずに死んだケース。new Mutex(true, ...) は所有権を引き継いで
                // 例外を投げるため _instanceMutex は取得済みになる。新規オーナーとして続行する。
                Logger.Log("前回のアプリケーション終了時に Mutex が正常にリリースされていません。Mutex を再取得しました。", LogLevel.Warning);
            }
            catch (Exception ex)
            {
                // Mutex 作成自体に失敗（ACL 拒否 / リソース枯渇 / AV 干渉等）。
                // 単一インスタンス保証は失われるが、起動を拒否するとユーザーが詰まるためフォールバック起動。
                // ⚠️ _instanceMutex が null のまま fall-through するので、後続経路の `_instanceMutex?.ReleaseMutex()` が
                // no-op になることを許容している（RTK レビュー #B1-006 対応で明示化）。
                _instanceMutex = null;
                Logger.LogException("Mutex 初期化エラー（単一インスタンス保証なしで起動継続）", ex);
            }

            // 初回起動時は IPC サーバーを開始して後続インスタンスからの引数を待機
            _ipcCts = new CancellationTokenSource();
            // fire-and-forget で捨てた Task が予期せぬ例外で黙って停止すると
            // シングルインスタンス引継ぎが機能しなくなるため、ContinueWith でログ出力
            _ = IpcService.StartServerAsync(OnArgsReceived, _ipcCts.Token)
                .ContinueWith(
                    t => Logger.LogException("IPCサーバーが予期せず停止しました", t.Exception!),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);

            // 前回の実行で残存した一時ディレクトリを掃除する（OneDrive 同期フォルダや中断時対策）
            _ = Task.Run(() => Util.TempCleanup.CleanupOrphanedTempDirectories());

            base.OnFrameworkInitializationCompleted();

#if DEBUG
            // デバッグモード: CRDebugger を初期化（ダイアログプレビュー機能付き）
            Util.DebugHelper.InitializeCRDebugger();
#endif

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
                    // 起動直後は Avalonia が desktop.MainWindow を自動表示するため showExplicitly:false。
                    if (!EnsureMainWindowShown(lifetime, showExplicitly: false))
                    {
                        TryShutdownSafely(lifetime);
                        return;
                    }

                    // メイン画面の起動が成功したら、Settings.Check4UpdatesOnStartup が true なら
                    // バックグラウンドで Velopack 自動更新チェックを実行する。
                    // Check4Update 内部で Dispatcher.UIThread.InvokeAsync しているため、
                    // MainWindow.Show が走り終わってからダイアログが乗る順序になる。
                    if (SettingsManager.Instance.Current.Check4UpdatesOnStartup)
                        Check4Update(manually: false);
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

            // Dispatcher がシャットダウン済みなら Shutdown を呼ばない。
            // 判定は Dispatcher の ShutdownStarted 状態を参照し、ロケール依存の
            // メッセージ文字列マッチを使わない。
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
            {
                TryShutdownSafely(lifetime);
            }
        }
    }

    /// <summary>
    /// Dispatcher シャットダウン済みなどを考慮して安全に Shutdown を試行する。
    /// InvalidOperationException を型ベースで catch することで、ロケール依存の
    /// メッセージ文字列マッチ（ex.Message.Contains("Dispatcher")）を回避する。
    /// </summary>
    /// <param name="lifetime">デスクトップライフタイム</param>
    private static void TryShutdownSafely(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        try
        {
            lifetime.Shutdown();
        }
        catch (InvalidOperationException)
        {
            // Dispatcher がシャットダウン中 / 終了済みだった場合の想定内例外。
            // ロケール依存の文字列マッチは信頼できないため、型のみで判定する。
            try { Logger.Log("Shutdown をスキップ（Dispatcher が既にシャットダウン中）", LogLevel.Warning); } catch { }
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
            var currentSessionId = currentProcess.SessionId;

            // 重要: SessionId で同一セッションのプロセスに絞り込む。
            // Mutex / IPC パイプはどちらも `Local\` / SessionId 付きでセッションスコープ化されているため、
            // 「既存インスタンス」も同一セッション内のものに限定しないと、RDP + console で
            // 別セッションの Lhamiel を選んでしまい SetForegroundWindow が空振り→そのまま終了
            // という経路が成立する。
            var otherProcess = Process.GetProcessesByName(currentProcess.ProcessName)
                .FirstOrDefault(p =>
                {
                    if (p.Id == currentProcess.Id) return false;
                    try { return p.SessionId == currentSessionId; }
                    catch { return false; } // 権限不足等で SessionId 取得不能なプロセスは対象外
                });

            if (otherProcess != null)
            {
                Logger.Log($"既存インスタンスを見つけました。PID: {otherProcess.Id} (Session: {currentSessionId})");

                // メインウィンドウをアクティブ化（NativeMethods を使用）
                try
                {
                    // 既存インスタンスに「自分自身を前面化する権利」を付与する。
                    // ユーザー操作（ダブルクリック）直後の本プロセスはフォアグラウンド権を持つため、
                    // ここで付与しておくと、既存インスタンス側が IPC 受信後に行う SetForegroundWindow /
                    // Activate が Win32 フォアグラウンドロックで空振りせず確実に前面化できる。
                    // MainWindowHandle がまだ無い headless 圧縮中インスタンスでも、メイン画面生成後の
                    // 前面化を有効にするため、ハンドルの有無に関わらず先に付与する。
                    NativeMethods.AllowSetForegroundWindow((uint)otherProcess.Id);

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
    /// <remarks>
    /// `--format` 引数値は <see cref="Settings.SupportedCompressionFormats"/> の allow-list で
    /// 検証する。未知の文字列を渡されても下流の Logger.Log にそのまま到達してログインジェクション
    /// （改行混入による SIEM 誤検知等）に繋がる経路を塞ぐ。allow-list 外は "default" にフォールバック。
    /// </remarks>
    private static (string compressionFormat, string[] filePaths) ParseCommandLineArgs(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--format")
        {
            var requestedFormat = args[1];
            // case-insensitive で allow-list と照合し、canonical なケースに正規化
            var canonical = Array.Find(Settings.SupportedCompressionFormats,
                f => string.Equals(f, requestedFormat, StringComparison.OrdinalIgnoreCase));
            return (canonical ?? "default", args[2..]);
        }
        return ("default", args.Where(a => !a.StartsWith("--")).ToArray());
    }

    /// <summary>
    /// コマンドライン経由の複数ファイル処理。まとめ圧縮設定に応じて分岐する。
    /// </summary>
    private async Task ProcessCommandLineFiles(string[] filePaths, string compressionFormat = "default", bool shouldShutdown = true)
    {
        if (filePaths.Length == 0) return;

        // 起動時 CLI と常駐インスタンスの IPC は同じ入口を通る。先行するドロップ操作も含めて
        // トップレベル操作をキュー化し、進捗ウィンドウ・上書き確認・最終移動を重ねない。
        using var operationGate = await ArchiveOperationGate.EnterAsync();

        // codex P2 #3384706125: VM の AutoSave は 300ms デバウンスされるため、設定パネル操作の
        // 直後にシェル/IPC 経由で圧縮・展開が始まると、下の CreateSnapshot が変更前の古い設定を
        // 掴むことがある (ドロップ経路は VM が直接スナップショットを押し下げるのに対し、この経路は
        // 永続層だけを見る)。スナップショット取得前に保留中の AutoSave をフラッシュして揃える。
        // 起動時 CLI・IPC ハンドラ (FromCurrentSynchronizationContext) とも UI スレッドで呼ばれる。
        // 初回起動の CLI 経路では MainWindow 未生成 (Current=null) だが、その場合は
        // デバウンス中の変更も存在しないので no-op で正しい。
        ViewModels.MainWindowViewModel.Current?.FlushPendingAutoSave();

        // 単一ファイルの場合は従来の処理
        if (filePaths.Length == 1)
        {
            await ProcessCommandLineFile(filePaths[0], compressionFormat, shouldShutdown);
            return;
        }

        // 有効なパスのみ収集
        var validPaths = new List<string>();
        foreach (var path in filePaths)
        {
            if (Directory.Exists(path) || File.Exists(path))
                validPaths.Add(path);
            else
                Logger.Log($"指定されたパスが存在しません: {path}");
        }

        if (validPaths.Count == 0) return;

        var settings = SettingsManager.Instance.CreateSnapshot();

        // すべてアーカイブで圧縮形式未指定なら個別展開
        if (compressionFormat == "default" && ArchiveExtractor.AreAllSupportedArchives(validPaths))
        {
            await ProcessMultipleExtractions(validPaths.ToArray(), settings, shouldShutdown);
        }
        else
        {
            // アーカイブ以外が混在 or 通常ファイルのみ → 圧縮
            var format = ResolveCompressionFormat(compressionFormat, settings);

            if (settings.CompressMultipleAsOne && validPaths.Count > 1)
            {
                await ProcessMergedCompression(validPaths.ToArray(), settings, format, shouldShutdown);
            }
            else
            {
                for (var i = 0; i < validPaths.Count; i++)
                {
                    var isLast = i == validPaths.Count - 1;
                    await ProcessCommandLineFile(validPaths[i], compressionFormat, isLast && shouldShutdown);
                }
            }
        }
    }

    /// <summary>
    /// コマンドライン処理の共通テンプレート。ProgressWindow のセットアップ・クリーンアップ・
    /// エラーハンドリング・シャットダウン制御を一元管理する。
    /// </summary>
    private async Task RunWithProgressWindowAsync(
        Func<ProgressWindow, CancellationToken, Task> operation,
        string operationName, string errorResourceKey, bool shouldShutdown)
    {
        ProgressWindow? progressWindow = null;

        // 自己終了する CLI / ファイル関連付け / アイコンドロップ経路 (shouldShutdown=true) では、
        // 操作中だけ自動シャットダウン (ShutdownMode.OnLastWindowClose) を抑止する。
        // ProgressWindow のクローズ (ArchiveProcessor が CloseSafe で Dispatcher に Post) が、
        // 「展開先/圧縮先を開く」の explorer 起動 (await 中に別スレッドで Process.Start) の最中に
        // 処理されると、最後のウィンドウクローズ → 自動シャットダウンが explorer 起動と競合し、
        // 起動し切る前にプロセスが落ちてフォルダが開かない回帰があった (#61 の await 化だけでは
        // 明示 ShutdownIfNeeded 経路しか守れず、暗黙の自動シャットダウンが残っていた)。
        // 操作完了後は finally で元の ShutdownMode に戻し、明示 ShutdownIfNeeded、または
        // 復帰後の OnLastWindowClose (ダイアログ表示でシャットダウンを見送ったケース) で終了する。
        // IPC 経路 (shouldShutdown=false) は常駐 MainWindow が居るため触らない。
        var desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var originalShutdownMode = desktop?.ShutdownMode;
        if (shouldShutdown && desktop != null)
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            (progressWindow, var cancellationTokenSource, var cancelHandler) = SetupProgressWindow(operationName);

            using (cancellationTokenSource)
            {
                try
                {
                    progressWindow.CancelRequested += cancelHandler;
                    progressWindow.Show();
                    progressWindow.Activate();
                    await Task.Yield();

                    await operation(progressWindow, cancellationTokenSource.Token);
                }
                finally
                {
                    progressWindow.CancelRequested -= cancelHandler;
                }
            }

            // explorer 起動 (operation 内で await 済み) が完了してから ProgressWindow を閉じる。
            // OnExplicitShutdown 中なのでこのクローズでは自動シャットダウンしない (ArchiveProcessor が
            // 既に閉じていれば CloseSafe は no-op)。
            progressWindow.CloseSafe();
            ShutdownIfNeeded(shouldShutdown);
        }
        catch (OperationCanceledException)
        {
            Logger.Log($"{operationName}がキャンセルされました");
            progressWindow?.CloseSafe();
            ShutdownIfNeeded(shouldShutdown);
        }
        catch (Exception ex)
        {
            Logger.LogException($"{operationName}でエラーが発生", ex);
            _ = MessageService.ShowError(App.Text(errorResourceKey, ex.Message));
            ShutdownIfNeeded(shouldShutdown);
        }
        finally
        {
            // 自動シャットダウンを元に戻す。ShutdownIfNeeded が (ProgressWindow / エラーダイアログが
            // まだ可視で) シャットダウンを見送った場合でも、最後のウィンドウが閉じた時点で
            // OnLastWindowClose が確実に終了させる安全網になる。
            if (desktop != null && originalShutdownMode.HasValue)
                desktop.ShutdownMode = originalShutdownMode.Value;
        }
    }

    /// <summary>
    /// 複数のアーカイブファイルを個別に展開する
    /// </summary>
    private Task ProcessMultipleExtractions(string[] filePaths, Settings settings, bool shouldShutdown = true)
    {
        Logger.Log($"コマンドラインから複数ファイル展開を開始: {filePaths.Length}個のファイル");
        return RunWithProgressWindowAsync(async (progressWindow, ct) =>
        {
            var extractionResults = await ArchiveProcessor.ExtractArchivesAsync(
                filePaths, settings.ExtractionOutputDirectory, settings.ExtractionOutputToSameDirectory,
                progressWindow, ct);

            if (extractionResults.Count > 0)
            {
                Logger.Log($"複数ファイル展開が完了しました: {extractionResults.Count}/{filePaths.Length}個成功");
                if (settings.OpenExtractionOutputFolder)
                {
                    // explorer 起動を並行で投げて Task.WhenAll でまとめて待つ。await することで
                    // 直後の ShutdownIfNeeded より前に全フォルダの起動が完了する (自己終了する
                    // CLI 経路でのシャットダウン競合を解消、v1.0.171 回帰の修正)。順次 await だと
                    // アーカイブ数ぶん起動が直列化してシャットダウンが遅れるため並行化する (gemini)。
                    var openTasks = new List<Task>();
                    foreach (var (_, outputPath, structureInfo) in extractionResults)
                        openTasks.Add(FolderOpener.OpenExtractionResultAsync(outputPath, structureInfo, settings.CreateArchiveNameFolder));
                    await Task.WhenAll(openTasks);
                }
            }
            else
            {
                Logger.Log("複数ファイル展開処理がすべて失敗しました");
            }
        }, App.Text("Progress.Extracting"), "Error.DuringExtraction", shouldShutdown);
    }

    /// <summary>
    /// 複数ファイルをまとめて1つのアーカイブに圧縮
    /// </summary>
    private Task ProcessMergedCompression(string[] sourcePaths, Settings settings, string format, bool shouldShutdown = true)
    {
        Logger.Log($"コマンドラインからまとめ圧縮を開始: {sourcePaths.Length}個の対象、形式={format}");
        return RunWithProgressWindowAsync(async (progressWindow, ct) =>
        {
            var success = await ArchiveProcessor.CompressMergedAsync(
                sourcePaths, settings.CompressionOutputDirectory, settings.CompressionOutputToSameDirectory,
                format, progressWindow, ct);

            if (success)
            {
                Logger.Log("まとめ圧縮処理が完了しました");
                if (settings.OpenCompressionOutputFolder)
                {
                    var baseDir = settings.CompressionOutputToSameDirectory
                        ? Path.GetDirectoryName(sourcePaths[0]) ?? ""
                        : settings.CompressionOutputDirectory;
                    // await して ShutdownIfNeeded より前に explorer 起動を完了させる (v1.0.171 回帰の修正)。
                    await FolderOpener.OpenFolderAsync(baseDir);
                }
            }
            else
            {
                Logger.Log("まとめ圧縮処理が失敗しました");
            }
        }, App.Text("Progress.Compressing"), "Error.DuringCompression", shouldShutdown);
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
            var settings = SettingsManager.Instance.CreateSnapshot();

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
    /// 圧縮形式を解決する。"default" の場合は設定値を使用する。
    /// </summary>
    private static string ResolveCompressionFormat(string compressionFormat, Settings settings)
    {
        return compressionFormat == "default" ? settings.CompressionFormat : compressionFormat;
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
    private Task ProcessFileExtraction(string filePath, Settings settings, bool shouldShutdown = true)
    {
        return RunWithProgressWindowAsync(async (progressWindow, ct) =>
        {
            var (finalOutputPath, structureInfo) = await ArchiveProcessor.ExtractArchiveAsync(
                filePath, settings.ExtractionOutputDirectory, settings.ExtractionOutputToSameDirectory,
                progressWindow, ct);

            if (finalOutputPath != null)
            {
                Logger.Log("ファイル展開処理が完了しました");
                if (settings.OpenExtractionOutputFolder)
                    // await して ShutdownIfNeeded より前に explorer 起動を完了させる (v1.0.171 回帰の修正)。
                    await FolderOpener.OpenExtractionResultAsync(finalOutputPath, structureInfo, settings.CreateArchiveNameFolder);
            }
            else
            {
                Logger.Log("ファイル展開処理が失敗しました");
            }
        }, App.Text("Progress.Extracting"), "Error.DuringExtraction", shouldShutdown);
    }

    /// <summary>
    /// ファイルまたはフォルダの圧縮処理を実行
    /// </summary>
    private Task ProcessCompression(string sourcePath, Settings settings, string compressionFormat = "default", bool shouldShutdown = true)
    {
        var format = ResolveCompressionFormat(compressionFormat, settings);
        return RunWithProgressWindowAsync(async (progressWindow, ct) =>
        {
            var success = await ArchiveProcessor.CompressItemAsync(
                sourcePath, settings.CompressionOutputDirectory, settings.CompressionOutputToSameDirectory,
                format, progressWindow, null, ct);

            if (success)
            {
                Logger.Log("圧縮処理が完了しました");
                if (settings.OpenCompressionOutputFolder)
                {
                    var finalOutputPath = ArchiveCompressor.GetCompressedFileName(
                        sourcePath, format, settings.CompressionOutputDirectory, settings.CompressionOutputToSameDirectory);
                    var directoryToOpen = Path.GetDirectoryName(finalOutputPath);
                    if (directoryToOpen != null)
                        // await して ShutdownIfNeeded より前に explorer 起動を完了させる (v1.0.171 回帰の修正)。
                        await FolderOpener.OpenFolderAsync(directoryToOpen);
                }
            }
            else
            {
                Logger.Log("圧縮処理が失敗しました");
            }
        }, App.Text("Progress.Compressing"), "Error.DuringCompression", shouldShutdown);
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

                // 引数の有無に関わらず、まずメイン画面を前面化する（存在しなければ生成する）。
                // 関連付け / アイコンドロップ起動の圧縮中は既存インスタンスに MainWindow が無いため、
                // ここで生成しないと「圧縮中にショートカットを再起動してもメイン画面が出ない」状態になる。
                EnsureMainWindowShown(desktop, showExplicitly: true);

                if (args.Length > 0)
                {
                    var (compressionFormat, filePaths) = ParseCommandLineArgs(args);

                    // 受信した引数で処理を実行。
                    // IPC 経由の場合は処理終了後にアプリを終了させないようにする（shouldShutdown:false）。
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
    /// メインウィンドウを確実に表示・前面化する。存在しなければ生成する。
    /// 起動時（引数なし）と、IPC 経由の活性化要求の両方から共用される。
    /// </summary>
    /// <param name="desktop">デスクトップライフタイム</param>
    /// <param name="showExplicitly">
    /// 新規生成した MainWindow を明示的に Show + 前面化するか。
    /// 起動直後（OnFrameworkInitializationCompleted 内）は Avalonia が自動表示するため false、
    /// ライフタイム開始後（IPC 受信時など）は自動表示されないため true を渡す。
    /// </param>
    /// <returns>メインウィンドウが利用可能（生成成功 or 既存）なら true、生成失敗なら false</returns>
    private bool EnsureMainWindowShown(IClassicDesktopStyleApplicationLifetime desktop, bool showExplicitly)
    {
        // メインウィンドウが未生成（headless な関連付け圧縮中インスタンスなど）の場合は生成する。
        if (desktop.MainWindow == null)
        {
            try
            {
                desktop.MainWindow = new MainWindow();
            }
            catch (Exception windowEx)
            {
                Logger.LogException("メインウィンドウの作成に失敗しました（グラフィックス初期化などの可能性）", windowEx);
                return false;
            }

            // 起動直後（OnFrameworkInitializationCompleted 内）は Avalonia が desktop.MainWindow を
            // 自動表示するため、二重 Show を避けて showExplicitly:false で早期 return する。
            // ライフタイム開始後（IPC 受信時など）は自動表示されないため、以降の Show + 前面化を行う。
            if (!showExplicitly)
                return true;
        }

        // 既存 or 生成直後のメインウィンドウを復元・前面化する。
        var window = desktop.MainWindow!;
        if (!window.IsVisible)
            window.Show();
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
        window.Focus();

        // 第 2 インスタンスが AllowSetForegroundWindow で前面化権を付与済みの前提で、
        // 自プロセスのウィンドウハンドルに対して明示的に SetForegroundWindow を撃つ。
        // Avalonia の Activate だけでは環境によってフォアグラウンドロックで空振りするため、
        // ネイティブ呼び出しを併用して確実に前面化する。
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
            NativeMethods.SetForegroundWindow(handle);

        return true;
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

    /// <summary>自動更新チェックのタイムアウト（自動チェックのみ適用、手動チェックは無制限）。</summary>
    /// <remarks>
    /// UI 経路のため <see cref="UpdateChecker.CheckTimeoutMs"/> (10 秒、サイレント CLI 経路) より
    /// 長めに取る。Velopack の半開き TCP / DNS 異常で長時間ロックされるのを防ぐ。
    /// </remarks>
    private static readonly TimeSpan AutomaticUpdateCheckTimeout = TimeSpan.FromSeconds(30);

    /// <summary>更新チェック中かどうかのアトミックフラグ（0=未実行, 1=実行中）。</summary>
    /// <remarks>
    /// 起動時自動チェック (Check4Update(false)) と設定タブの「アップデート確認」ボタン
    /// (Check4Update(true)) が同時に走らないように先勝ち排他する。
    /// Interlocked.CompareExchange で原子的に状態遷移するため lock 不要。
    /// </remarks>
    private static int _isCheckingUpdate;

    /// <summary>
    /// 更新チェックが進行中かどうかを ViewModel / 他ロジックから観測するための public 読み取りプロパティ。
    /// 値は <see cref="_isCheckingUpdate"/> をロックフリーで読む（実行中: true / 未実行: false）。
    /// </summary>
    public static bool IsUpdateCheckInProgress =>
        Interlocked.CompareExchange(ref _isCheckingUpdate, 0, 0) == 1;

    /// <summary>更新チェックの進行状態が変化したときに発火するイベント (true=開始 / false=終了)。</summary>
    /// <remarks>
    /// 起動時自動チェックと手動チェック (アップデート確認ボタン) の両経路から発火する。
    /// MainWindowViewModel はこれを購読して IsCheckingUpdate を駆動し、ボタンの IsEnabled を制御する。
    /// ハンドラはバックグラウンドスレッドから呼ばれる可能性があるため、UI 更新は購読側で
    /// Dispatcher.UIThread に marshal すること。
    /// </remarks>
    public static event Action<bool>? UpdateCheckStateChanged;

    /// <summary>更新チェック開始の試行。0→1 への遷移に成功した場合のみ true を返し、イベントを発火する。</summary>
    private static bool TryBeginUpdateCheck()
    {
        if (Interlocked.CompareExchange(ref _isCheckingUpdate, 1, 0) != 0)
            return false;
        RaiseUpdateCheckStateChanged(true);
        return true;
    }

    /// <summary>更新チェック終了。1→0 への遷移に成功した場合のみイベントを発火する（多重呼出に対して冪等）。</summary>
    private static void EndUpdateCheck()
    {
        if (Interlocked.CompareExchange(ref _isCheckingUpdate, 0, 1) == 1)
            RaiseUpdateCheckStateChanged(false);
    }

    /// <summary>イベント発火時のハンドラ例外を握りつぶしてフラグ管理を巻き戻さないためのラッパー。</summary>
    private static void RaiseUpdateCheckStateChanged(bool inProgress)
    {
        try { UpdateCheckStateChanged?.Invoke(inProgress); }
        catch (Exception ex) { Logger.LogException("UpdateCheckStateChanged ハンドラで例外", ex); }
    }

    /// <summary>
    /// GitHubリリースから最新バージョンを確認し、更新がある場合は VelopackUpdateDialog.Avalonia の
    /// <see cref="VelopackUpdateDialog.UpdateDialogWindow"/> でダウンロード / 適用までを誘導する。
    /// </summary>
    /// <param name="manually">
    /// true: 手動チェック（最新版でも結果ダイアログを残す、無視タグは無視）。
    /// false: 自動チェック（更新がある場合のみ表示、<see cref="Settings.IgnoreUpdateTag"/> と一致したら何も表示しない）。
    /// </param>
    public static void Check4Update(bool manually = false)
    {
        // 進行中フラグの先勝ちで二重起動を防止する（起動時自動チェック / 手動チェック 両経路に共通）。
        // 並走中は UI 側の「アップデート確認」ボタンが UpdateCheckStateChanged 経由で IsEnabled=false に
        // 落ちるため、ユーザーは押せない → サイレント return しても操作不可で違和感が出ない。
        if (!TryBeginUpdateCheck())
        {
            return;
        }

        // InvokeAsync 自体が Dispatcher shutdown 中に同期例外を投げると _isCheckingUpdate が
        // 1 のまま固着するため、try/catch でガード + ContinueWith で fallback リセットを敷く。
        try
        {
            var op = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    // UpdateManager 組み立ては UpdateChecker.TryBuildUpdateManager に集約 (DRY)。
                    // サイレント経路 (Program.cs --update-check) と同一の組み立てロジックを共有する。
                    var settings = SettingsManager.Instance.CreateSnapshot();
                    var built = UpdateChecker.TryBuildUpdateManager(settings);
                    if (built is null)
                    {
                        Logger.Log("更新元リポジトリが未設定のため自動更新チェックをスキップします。", LogLevel.Warning);
                        // 手動チェック時はサイレント return すると「ボタン押しても何も起きない」に見えるため
                        // ユーザーに理由を明示する。
                        if (manually)
                            await MessageService.ShowInfo(App.Text("Update.RepoNotConfigured"));
                        return;
                    }
                    var (mgr, baseUrl, channel) = built.Value;
                    var isPrerelease = channel.Equals("prerelease", StringComparison.OrdinalIgnoreCase);

                    if (!mgr.IsInstalled)
                    {
                        // IsInstalled=false は (1) `dotnet run` 等の開発実行、または
                        // (2) Velopack manifest 破損 / 手動 ZIP 配置 / current/ シンボリックリンク欠損 のいずれか。
                        // サポート時の切り分けのため ProcessPath を併記する。
                        Logger.Log(
                            $"Velopack の IsInstalled=false のため自動更新チェックをスキップ (開発実行 or manifest 破損の可能性): ProcessPath={Environment.ProcessPath ?? "(unknown)"}",
                            LogLevel.Warning);
                        // 手動チェック時はサイレント return すると「ボタン押しても何も起きない」に見えるため
                        // 開発環境スキップである旨を明示する。
                        if (manually)
                            await MessageService.ShowInfo(App.Text("Update.DevSkip"));
                        return;
                    }

                    // VelopackUpdateDialog.Avalonia 1.0.3 の IgnoredTagName を使用する。
                    // パッケージ側の ShowAsync 自動チェック分岐が「Available && tag != IgnoredTagName」のときだけ
                    // Window を開くので、ホスト側で UpToDate / Failed / IgnoredTag 一致を先回り判定する必要はない。
                    // 手動チェック (manualCheck:true) では IgnoredTagName を無視して常に最新を表示する。
                    var options = new VelopackUpdateDialog.UpdateDialogOptions
                    {
                        Strings = Models.LhamielUpdateStrings.Instance,
                        IgnoredTagName = SettingsManager.Instance.Current.IgnoreUpdateTag,
                    };
                    options.VersionIgnored += tag =>
                        Dispatcher.UIThread.Post(() =>
                        {
                            // MutateAndSave で atomic にする (Mutate と Save の間に別 thread が割り込まない)。
                            // Save 失敗時はメモリ状態を巻き戻してから例外を表に出し、ユーザーに通知する
                            // ("スキップしたのに次回再表示" のサイレント失効を防ぐ)。
                            var oldTag = SettingsManager.Instance.Current.IgnoreUpdateTag;
                            try
                            {
                                SettingsManager.Instance.MutateAndSave(s => s.IgnoreUpdateTag = tag);
                                Logger.Log($"IgnoreUpdateTag を保存: {tag}", LogLevel.Warning);
                            }
                            catch (Exception saveEx)
                            {
                                SettingsManager.Instance.Mutate(s => s.IgnoreUpdateTag = oldTag);
                                Logger.LogException("IgnoreUpdateTag の保存に失敗", saveEx);
                                _ = MessageService.ShowError(App.Text("Error.SaveSettingsFailed", saveEx.Message));
                            }
                        });
                    options.ErrorOccurred += ex =>
                        // フルスタックトレースは DiagnosticsCollector 経由で第三者配布される ZIP に含まれる懸念
                        // (内部 URL / プロキシ情報 / 例外チェーンが露出)。要点のみ Warning で記録する。
                        Logger.Log(
                            $"Velopack 更新失敗: {ex.GetType().Name}: {ex.Message}",
                            LogLevel.Warning);

                    var owner = (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

                    Logger.Log(
                        $"Velopack 自動更新チェック開始: manually={manually}, baseUrl={baseUrl}, channel={(isPrerelease ? "prerelease" : "release")}",
                        LogLevel.Warning);

                    // VelopackUpdateDialog.Avalonia 1.0.3 の ShowAsync が自動分岐で
                    // 「Available && tag != IgnoredTagName」のときだけ Window を開く。
                    // UpToDate / Failed / IgnoredTag 一致はパッケージ側で内部処理されダイアログは開かない。
                    // 手動チェック (manualCheck:true) では IgnoredTagName を無視して常に最新を表示する。
                    //
                    // タイムアウトは自動チェックのみ 30 秒で設定する。
                    // Velopack の半開き TCP / DNS 異常で長時間ロックされるのを防ぐ。
                    // 手動チェックはユーザーが待てる前提で無制限（Close で中断可）。
                    CancellationToken cancelToken = CancellationToken.None;
                    CancellationTokenSource? autoCts = null;
                    if (!manually)
                    {
                        autoCts = new CancellationTokenSource(AutomaticUpdateCheckTimeout);
                        cancelToken = autoCts.Token;
                    }

                    try
                    {
                        await VelopackUpdateDialog.UpdateDialogWindow.ShowAsync(
                            owner, mgr, options, manualCheck: manually, cancelToken);
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.Log(
                            $"自動更新チェックがタイムアウトしました（{AutomaticUpdateCheckTimeout.TotalSeconds:F0} 秒）: baseUrl={baseUrl}, channel={(isPrerelease ? "prerelease" : "release")}",
                            LogLevel.Warning);
                        return;
                    }
                    finally
                    {
                        autoCts?.Dispose();
                        // Velopack の UpdateManager が IDisposable を実装している場合の Dispose。
                        // タイムアウト時の HttpClient リーク防止 + 確実なリソース解放のため。
                        (mgr as IDisposable)?.Dispose();
                    }

                    Logger.Log(
                        $"Velopack 自動更新チェック完了: manually={manually}",
                        LogLevel.Warning);
                }
                catch (Exception e)
                {
                    Logger.LogException("更新チェック失敗", e);
                }
                finally
                {
                    EndUpdateCheck();
                }
            });

            // InvokeAsync の DispatcherOperation がキャンセル / 失敗で lambda が走らなかった場合の
            // _isCheckingUpdate フォールバックリセット。Dispatcher shutdown 中の OperationCanceledException
            // でも stuck しないようにする。
            // EndUpdateCheck() 自体が 1→0 への CAS なので、通常 finally と二重で呼ばれても冪等。
            _ = op.ContinueWith(t =>
            {
                if (IsUpdateCheckInProgress)
                {
                    EndUpdateCheck();
                    if (t.IsFaulted)
                        Logger.LogException("Check4Update の DispatcherOperation が異常終了", t.Exception!);
                    else if (t.IsCanceled)
                        Logger.Log("Check4Update の DispatcherOperation がキャンセルされました", LogLevel.Warning);
                }
            }, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            // InvokeAsync 呼び出し自体の同期例外 (Dispatcher shutdown 中など)。
            EndUpdateCheck();
            Logger.LogException("Check4Update の InvokeAsync 呼び出しに失敗", ex);
        }
    }

    /// <summary>
    /// アプリケーション終了時の処理
    /// </summary>
    public void OnApplicationExiting()
    {
        Logger.Log("アプリケーション終了");

        // 進行中の Check4Update があれば最大 2 秒待機する。
        // Logger.Dispose 後に LogException が呼ばれて ObjectDisposedException 二次例外になる経路を回避するため、
        // Logger.Dispose の前に async タスクの完結を待つ。
        if (IsUpdateCheckInProgress)
        {
            Logger.Log("更新チェック進行中のため最大 2 秒待機します", LogLevel.Warning);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (IsUpdateCheckInProgress && sw.ElapsedMilliseconds < 2000)
                Thread.Sleep(50);
            if (IsUpdateCheckInProgress)
                Logger.Log("更新チェックが 2 秒以内に完了しませんでした (継続中のまま終了)", LogLevel.Warning);
        }

        // debounce 中の設定保存をフラッシュ（設定変更直後の終了でもロストしない）
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow }
            && mainWindow.DataContext is ViewModels.MainWindowViewModel vm)
        {
            vm.FlushPendingAutoSave();
        }

        // CTS の安全な破棄（ObjectDisposedException を無視）
        TryCancelAndDispose(_ipcCts);

        // Mutex の安全な解放と破棄
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
    /// CancellationTokenSource を安全にキャンセルして破棄する（ObjectDisposedException を無視）
    /// </summary>
    private static void TryCancelAndDispose(CancellationTokenSource? cts)
    {
        if (cts == null) return;
        try { cts.Cancel(); } catch (ObjectDisposedException) { }
        try { cts.Dispose(); } catch (ObjectDisposedException) { }
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

    /// <summary>現在アクティブなロケールキー。</summary>
    private string? _activeLocaleKey;

    /// <summary>
    /// 選択ロケールのみ <c>Resources.MergedDictionaries</c> に挿入する。
    /// 辞書自体は App.axaml の ResourceInclude で登録し、Native AOT / compiled XAML でも
    /// ビルド成果物に含まれるようにする。
    /// </summary>
    /// <param name="localeKey">ロケールキー（"ja_JP", "en_US" など）</param>
    private void ApplyLocale(string localeKey)
    {
        if (string.IsNullOrEmpty(localeKey)) return;
        if (string.Equals(_activeLocaleKey, localeKey, StringComparison.OrdinalIgnoreCase)) return;

        if (Resources[localeKey] is not IResourceProvider targetLocale)
        {
            Util.Logger.Log($"未登録のロケールが指定されました: {localeKey}", Util.LogLevel.Warning);
            return;
        }

        if (_activeLocale != null)
            Resources.MergedDictionaries.Remove(_activeLocale);

        Resources.MergedDictionaries.Add(targetLocale);
        _activeLocale = targetLocale;
        _activeLocaleKey = localeKey;

        // VelopackUpdateDialog 等の動的 binding 経路に locale 変更を通知する。
        // 既に開いているダイアログが存在する場合に getter を再評価させて即時翻訳反映する目的。
        // 新規ダイアログは次回オープン時に getter が動的解決するので必須ではないが、UX 統一のため呼ぶ。
        Models.LhamielUpdateStrings.Instance.NotifyLocaleChanged();
    }

    /// <summary>
    /// キーからフォーマット文字列を取得する共通ヘルパ（リテラル \n 展開込み）。
    /// </summary>
    private static string GetLocalizedFormat(string key)
    {
        var fullKey = string.Concat("Text.", key);
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

        if (fmt.Contains("\\n", StringComparison.Ordinal))
            fmt = fmt.Replace("\\n", "\n");

        return fmt;
    }

    /// <summary>
    /// 引数なしのローカライズ済みテキスト取得（0 アロケーションの hot path 向け）
    /// </summary>
    public static string Text(string key) => GetLocalizedFormat(key);

    /// <summary>
    /// 引数 1 つのローカライズ済みテキスト取得（params アロケーションを避ける）
    /// </summary>
    public static string Text(string key, object? arg0)
    {
        var fmt = GetLocalizedFormat(key);
        return string.Format(fmt, arg0);
    }

    /// <summary>
    /// 引数 2 つのローカライズ済みテキスト取得（params アロケーションを避ける）
    /// </summary>
    public static string Text(string key, object? arg0, object? arg1)
    {
        var fmt = GetLocalizedFormat(key);
        return string.Format(fmt, arg0, arg1);
    }

    /// <summary>
    /// リソースからローカライズ済みテキストを取得する（可変引数版。3 引数以上や配列経由の呼び出し向け）
    /// </summary>
    /// <param name="key">リソースキー（"Text." プレフィックスなし）</param>
    /// <param name="args">フォーマット引数</param>
    /// <returns>ローカライズ済み文字列</returns>
    public static string Text(string key, params object[] args)
    {
        var fmt = GetLocalizedFormat(key);
        if (args.Length == 0)
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

