using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using ZLogger;
using ZLogger.Providers;

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
/// ZLoggerを使用したログ出力機能を提供するクラス
/// </summary>
public static class Logger
{
    private static ILoggerFactory? _loggerFactory;
    private static ILogger? _logger;
    private static bool _isConfigured;

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
    public static void Initialize()
    {
        if (_isConfigured) return;

        var settings = Settings.Load();
        var logFilePath = Path.Combine(Settings.AppDataDirectory, "Lhamiel.log");

        // ディレクトリ作成
        if (!Directory.Exists(Settings.AppDataDirectory))
        {
            Directory.CreateDirectory(Settings.AppDataDirectory);
        }
        
        // ZLogger の初期化
        _loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
            
            logging.AddZLoggerRollingFile(options =>
            {
                options.FilePathSelector = (timestamp, sequenceNumber) =>
                    Path.Combine(Settings.AppDataDirectory, $"Lhamiel_{timestamp.ToLocalTime():yyyyMMdd}_{sequenceNumber:000}.log");
                options.RollingSizeKB = settings.LogMaxSizeMB * 1024;
            });

            logging.AddZLoggerConsole();
        });

        _logger = _loggerFactory.CreateLogger("Lhamiel");
        _isConfigured = true;

        Log("Logger initialized with ZLogger (RollingFile)", LogLevel.Debug);
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

        Initialize();

        var timestamp = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]";
        var levelStr = level switch
        {
            LogLevel.Debug => "[DEBUG]",
            LogLevel.Info => "[INFO]",
            LogLevel.Warning => "[WARN]",
            LogLevel.Error => "[ERROR]",
            _ => "[INFO]"
        };

        var formattedMessage = $"{timestamp} {levelStr} {message}";

        switch (level)
        {
            case LogLevel.Debug:
                _logger?.ZLogDebug($"{formattedMessage}");
                break;
            case LogLevel.Info:
                _logger?.ZLogInformation($"{formattedMessage}");
                break;
            case LogLevel.Warning:
                _logger?.ZLogWarning($"{formattedMessage}");
                break;
            case LogLevel.Error:
                _logger?.ZLogError($"{formattedMessage}");
                break;
        }
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

        Initialize();

        var timestamp = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]";
        var levelStr = level switch
        {
            LogLevel.Debug => "[DEBUG]",
            LogLevel.Info => "[INFO]",
            LogLevel.Warning => "[WARN]",
            LogLevel.Error => "[ERROR]",
            _ => "[INFO]"
        };

        foreach (var message in messages)
        {
            var formattedMessage = $"{timestamp} {levelStr} {message}";
            switch (level)
            {
                case LogLevel.Debug:
                    _logger?.ZLogDebug($"{formattedMessage}");
                    break;
                case LogLevel.Info:
                    _logger?.ZLogInformation($"{formattedMessage}");
                    break;
                case LogLevel.Warning:
                    _logger?.ZLogWarning($"{formattedMessage}");
                    break;
                case LogLevel.Error:
                    _logger?.ZLogError($"{formattedMessage}");
                    break;
            }
        }
    }

    /// <summary>
    /// 例外情報を含むログを出力する（常にErrorレベル）
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    /// <param name="exception">例外オブジェクト</param>
    public static void LogException(string message, Exception exception)
    {
        Initialize();
        var timestamp = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]";
        var levelStr = "[ERROR]";
        var formattedMessage = $"{timestamp} {levelStr} {message}";
        _logger?.ZLogError(exception, $"{formattedMessage}");
    }

    /// <summary>
    /// アプリケーション起動時のログを出力する（Debugレベル）
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    public static void LogStartup(string[] args)
    {
        if (LogLevel.Debug < MinLogLevel) return;

        Initialize();

        _logger?.ZLogDebug($"""
            === Lhamiel 起動ログ ===
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
        _loggerFactory?.Dispose();
        _isConfigured = false;
    }
}
