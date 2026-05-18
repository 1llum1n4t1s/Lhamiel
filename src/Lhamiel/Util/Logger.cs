using SuperLightLogger;

namespace Lhamiel.Util;

/// <summary>
/// ログレベルを表す列挙型
/// </summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

/// <summary>
/// ログ初期化設定
/// </summary>
public sealed class LoggerConfig
{
    /// <summary>ログ出力ディレクトリ</summary>
    public required string LogDirectory { get; init; }

    /// <summary>ログファイル名のプレフィックス（例: "MyApp"）</summary>
    public required string FilePrefix { get; init; }

    /// <summary>ローリングサイズ上限（MB）</summary>
    public int MaxSizeMB { get; init; } = 10;

    /// <summary>アーカイブファイルの最大保持数</summary>
    public int MaxArchiveFiles { get; init; } = 10;

    /// <summary>ログファイルの保持日数（0以下の場合は削除しない）</summary>
    public int RetentionDays { get; init; } = 7;
}

/// <summary>
/// SuperLightLoggerを使用した汎用ログ出力クラス
/// </summary>
public static class Logger
{
    private static ILog? _logger;
    private static bool _isConfigured;
    private static readonly object _initLock = new();
    private static string _appName = "App";

    /// <summary>
    /// 最小ログレベル（これ以上のレベルのログのみ出力）
    /// </summary>
    private static readonly LogLevel MinLogLevel =
#if DEBUG
        LogLevel.Debug;
#else
        LogLevel.Warning;
#endif

    /// <summary>
    /// ロガーを初期化する
    /// </summary>
    /// <param name="config">ログ設定</param>
    public static void Initialize(LoggerConfig config)
    {
        if (_isConfigured) return;

        lock (_initLock)
        {
            // ダブルチェックロッキング: 別スレッドが先に初期化を完了した場合を防止
            if (_isConfigured) return;

            _appName = config.FilePrefix;

            Directory.CreateDirectory(config.LogDirectory);

            // 文字列ベース API を使用（Lhamiel.Util.LogLevel と MEL の LogLevel の名前衝突を回避）
            LogManager.Configure(builder =>
            {
                builder.AddSuperLightFile(opt =>
                {
                    opt.FileName = Path.Combine(config.LogDirectory, $"{config.FilePrefix}_${{date:format=yyyyMMdd}}.log");
                    opt.Layout = "${longdate} [${level:uppercase=true}] ${message}${onexception:inner=${newline}${exception:format=tostring}}";
                    opt.ArchiveAboveSize = (long)config.MaxSizeMB * 1024 * 1024;
                    opt.ArchiveFileName = Path.Combine(config.LogDirectory, $"{config.FilePrefix}_${{date:format=yyyyMMdd}}_{{#}}.log");
                    opt.ArchiveNumbering = ArchiveNumbering.Sequence;
                    opt.MaxArchiveFiles = config.MaxArchiveFiles;
                    opt.Encoding = System.Text.Encoding.UTF8;
                    opt.MinLevelName = "Trace";
                });

                builder.SetMinimumLevel(ToLevelName(MinLogLevel));
            });

            _logger = LogManager.GetLogger(config.FilePrefix);
            _isConfigured = true;
        }

        Log("Logger initialized with SuperLightLogger (File Target)", LogLevel.Debug);

        // ファイル I/O を含むクリーンアップ処理は起動クリティカルパスから外す。
        // Task.Run で非同期化して、%LocalAppData% が OneDrive 同期や
        // Defender スキャンで遅い環境でも起動を止めない。
        var logDirectory = config.LogDirectory;
        var filePrefix = config.FilePrefix;
        var retentionDays = config.RetentionDays;
        _ = Task.Run(() =>
        {
            try
            {
                // 過去のバグで作成された不要な "0" ファイルを削除
                CleanupStaleFile(Path.Combine(logDirectory, "0"));

                // 保持期間を超えた古いログファイルを削除
                CleanupOldLogFiles(logDirectory, filePrefix, retentionDays);
            }
            catch
            {
                // ベストエフォート: 起動パスを絶対に止めない
            }
        });
    }

    /// <summary>
    /// 過去のバグで作成された不要ファイルを削除する
    /// </summary>
    /// <param name="filePath">削除対象のファイルパス</param>
    private static void CleanupStaleFile(string filePath)
    {
        try
        {
            File.Delete(filePath);
            Log($"不要なファイルを削除しました: {Path.GetFileName(filePath)}", LogLevel.Debug);
        }
        catch
        {
            // ファイルが存在しない or 削除できない場合は無視
        }
    }

    /// <summary>
    /// 保持期間を超えた古いログファイルを削除する
    /// </summary>
    /// <param name="logDirectory">ログディレクトリ</param>
    /// <param name="filePrefix">ログファイル名のプレフィックス</param>
    /// <param name="retentionDays">保持日数（0以下の場合は削除しない）</param>
    private static void CleanupOldLogFiles(string logDirectory, string filePrefix, int retentionDays)
    {
        if (retentionDays <= 0) return;

        try
        {
            var cutoffDate = DateTime.Now.Date.AddDays(-retentionDays);
            var logFiles = Directory.EnumerateFiles(logDirectory, $"{filePrefix}_*.log");

            foreach (var file in logFiles)
            {
                try
                {
                    // ファイル名から日付部分を抽出（例: Lhamiel_20260206.log or Lhamiel_20260206_000.log）
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var parts = fileName.Split('_');
                    if (parts.Length >= 2 && parts[1].Length == 8 &&
                        DateTime.TryParseExact(parts[1], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var fileDate))
                    {
                        if (fileDate < cutoffDate)
                        {
                            File.Delete(file);
                            Log($"古いログファイルを削除しました: {Path.GetFileName(file)}", LogLevel.Debug);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 個別ファイルの削除失敗はログに記録して続行
                    Log($"ログファイルの削除に失敗しました: {Path.GetFileName(file)} - {ex.Message}", LogLevel.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            Log($"ログファイルのクリーンアップ中にエラーが発生しました: {ex.Message}", LogLevel.Warning);
        }
    }

    /// <summary>
    /// ログを出力する
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    /// <param name="level">ログレベル（デフォルト: Info）</param>
    public static void Log(string message, LogLevel level = LogLevel.Info)
    {
        if (level < MinLogLevel)
            return;

        WriteToLogger(message, level);
    }

    /// <summary>
    /// 複数行のログを出力する
    /// </summary>
    /// <param name="messages">ログメッセージの配列</param>
    /// <param name="level">ログレベル（デフォルト: Info）</param>
    public static void LogLines(string[] messages, LogLevel level = LogLevel.Info)
    {
        if (messages == null || messages.Length == 0) return;

        foreach (var message in messages)
        {
            Log(message, level);
        }
    }

    /// <summary>
    /// 例外情報を含むログを出力する（常にErrorレベル）
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    /// <param name="exception">例外オブジェクト</param>
    public static void LogException(string message, Exception exception)
    {
        if (_logger != null)
        {
            _logger.Error(message, exception);
            return;
        }

        // Logger 初期化前 / 初期化失敗時の緊急フォールバック。
        // Avalonia 起動失敗・LogManager.Configure 例外などでロガーが
        // 立ち上がらないケースでも例外情報を失わないよう、直接ファイルに追記する。
        WriteEmergencyLog(message, exception);
    }

    /// <summary>
    /// _logger 未初期化時の緊急ログ書き込み。
    /// %LocalAppData%\Lhamiel\Lhamiel_emergency.log に追記する。
    /// </summary>
    private static void WriteEmergencyLog(string message, Exception exception)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                _appName);
            Directory.CreateDirectory(logDir);
            var path = Path.Combine(logDir, $"{_appName}_emergency.log");
            var line =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [ERROR] {message}{Environment.NewLine}{exception}{Environment.NewLine}";
            File.AppendAllText(path, line);
        }
        catch
        {
            // 最終フォールバック失敗時はもう諦める（ディスク満杯・権限不足など）。
        }
    }

    /// <summary>
    /// アプリケーション起動時のログを出力する（Debugレベル）
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    public static void LogStartup(string[] args)
    {
        if (LogLevel.Debug < MinLogLevel) return;

        _logger?.Debug(
            $"""
            === {_appName} 起動ログ ===
            起動時刻: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}
            実行ファイルパス: {Environment.ProcessPath}
            コマンドライン引数の数: {args.Length}
            コマンドライン引数:
            {string.Join(Environment.NewLine, args.Select((a, i) => $"  [{i}]: {a}"))}
            """);
    }

    /// <summary>
    /// ロガーを明示的に終了する（バッファのフラッシュなど）
    /// </summary>
    public static void Dispose()
    {
        LogManager.Shutdown();
        _isConfigured = false;
    }

    /// <summary>
    /// SuperLightLogger の ILog にレベル別メソッドでメッセージを書き出す
    /// </summary>
    private static void WriteToLogger(string message, LogLevel level)
    {
        if (_logger == null) return;

        switch (level)
        {
            case LogLevel.Debug:
                _logger.Debug(message);
                break;
            case LogLevel.Info:
                _logger.Info(message);
                break;
            case LogLevel.Warning:
                _logger.Warn(message);
                break;
            case LogLevel.Error:
                _logger.Error(message);
                break;
        }
    }

    /// <summary>
    /// 独自LogLevelをSuperLightLoggerが受け付けるレベル名に変換
    /// </summary>
    private static string ToLevelName(LogLevel level) => level switch
    {
        LogLevel.Debug => "Debug",
        LogLevel.Info => "Info",
        LogLevel.Warning => "Warn",
        LogLevel.Error => "Error",
        _ => "Info"
    };
}
