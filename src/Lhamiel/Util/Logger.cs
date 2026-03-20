using NLog;
using NLog.Config;
using NLog.Targets;

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
/// NLogを使用した汎用ログ出力クラス
/// </summary>
public static class Logger
{
    private static NLog.Logger? _logger;
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

            if (!Directory.Exists(config.LogDirectory))
            {
                Directory.CreateDirectory(config.LogDirectory);
            }

            var nlogConfig = new LoggingConfiguration();

            var fileTarget = new FileTarget("file")
            {
                FileName = Path.Combine(config.LogDirectory, $"{config.FilePrefix}_${{date:format=yyyyMMdd}}.log"),
                ArchiveAboveSize = config.MaxSizeMB * 1024 * 1024,
                ArchiveFileName = Path.Combine(config.LogDirectory, $"{config.FilePrefix}_${{date:format=yyyyMMdd}}_{{##}}.log"),
                ArchiveSuffixFormat = "_{##}",
                MaxArchiveFiles = config.MaxArchiveFiles,
                Layout = "${longdate} [${uppercase:${level}}] ${message}${onexception:inner=${newline}${exception:format=tostring}}",
                Encoding = System.Text.Encoding.UTF8
            };

            var consoleTarget = new ConsoleTarget("console")
            {
                Layout = "${longdate} [${uppercase:${level}}] ${message}${onexception:inner=${newline}${exception:format=tostring}}"
            };

            nlogConfig.AddTarget(fileTarget);
            nlogConfig.AddTarget(consoleTarget);

            nlogConfig.AddRule(NLog.LogLevel.Trace, NLog.LogLevel.Fatal, fileTarget);
            nlogConfig.AddRule(NLog.LogLevel.Trace, NLog.LogLevel.Fatal, consoleTarget);

            LogManager.Configuration = nlogConfig;
            _logger = LogManager.GetLogger(config.FilePrefix);
            _isConfigured = true;
        }

        Log("Logger initialized with NLog (RollingFile)", LogLevel.Debug);

        // 過去のバグで作成された不要な "0" ファイルを削除
        CleanupStaleFile(Path.Combine(config.LogDirectory, "0"));

        // 保持期間を超えた古いログファイルを削除
        CleanupOldLogFiles(config.LogDirectory, config.FilePrefix, config.RetentionDays);
    }

    /// <summary>
    /// 過去のバグで作成された不要ファイルを削除する
    /// </summary>
    /// <param name="filePath">削除対象のファイルパス</param>
    private static void CleanupStaleFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Log($"不要なファイルを削除しました: {Path.GetFileName(filePath)}", LogLevel.Debug);
            }
        }
        catch (Exception ex)
        {
            Log($"不要ファイルの削除に失敗しました: {filePath} - {ex.Message}", LogLevel.Warning);
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
            var logFiles = Directory.GetFiles(logDirectory, $"{filePrefix}_*.log");

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

        _logger?.Log(ToNLogLevel(level), message);
    }

    /// <summary>
    /// 複数行のログを出力する
    /// </summary>
    /// <param name="messages">ログメッセージの配列</param>
    /// <param name="level">ログレベル（デフォルト: Info）</param>
    public static void LogLines(string[] messages, LogLevel level = LogLevel.Info)
    {
        if (messages == null || messages.Length == 0) return;
        if (level < MinLogLevel) return;

        var nlogLevel = ToNLogLevel(level);
        foreach (var message in messages)
        {
            _logger?.Log(nlogLevel, message);
        }
    }

    /// <summary>
    /// 例外情報を含むログを出力する（常にErrorレベル）
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    /// <param name="exception">例外オブジェクト</param>
    public static void LogException(string message, Exception exception)
    {
        _logger?.Error(exception, message);
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
    /// 独自LogLevelをNLogのLogLevelに変換
    /// </summary>
    private static NLog.LogLevel ToNLogLevel(LogLevel level) => level switch
    {
        LogLevel.Debug => NLog.LogLevel.Debug,
        LogLevel.Info => NLog.LogLevel.Info,
        LogLevel.Warning => NLog.LogLevel.Warn,
        LogLevel.Error => NLog.LogLevel.Error,
        _ => NLog.LogLevel.Info
    };
}
