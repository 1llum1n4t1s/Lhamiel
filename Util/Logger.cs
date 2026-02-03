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
            // Microsoft.Extensions.Logging.LogLevel を明示的に使用
            logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
            
            // ローリングファイル出力の設定（README の options 形式）
            // https://github.com/Cysharp/ZLogger#rollingfile
            // FilePathSelector: 戻り値のファイル名は連番のみで終わる必要あり（(\d)+$）。ファイル名を連番のみにすることで検証を通過する。
            logging.AddZLoggerRollingFile(options =>
            {
                options.FilePathSelector = (_, sequenceNumber) =>
                    Path.Combine(Settings.AppDataDirectory, sequenceNumber.ToString());
                options.RollingSizeKB = settings.LogMaxSizeMB * 1024;
            });

            // コンソール出力
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

        switch (level)
        {
            case LogLevel.Debug:
                _logger?.ZLogDebug($"{message}");
                break;
            case LogLevel.Info:
                _logger?.ZLogInformation($"{message}");
                break;
            case LogLevel.Warning:
                _logger?.ZLogWarning($"{message}");
                break;
            case LogLevel.Error:
                _logger?.ZLogError($"{message}");
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

        foreach (var message in messages)
        {
            // ZLog*** を直接呼ぶことで、各行で補間文字列ハンドラーの恩恵を受ける
            switch (level)
            {
                case LogLevel.Debug:
                    _logger?.ZLogDebug($"{message}");
                    break;
                case LogLevel.Info:
                    _logger?.ZLogInformation($"{message}");
                    break;
                case LogLevel.Warning:
                    _logger?.ZLogWarning($"{message}");
                    break;
                case LogLevel.Error:
                    _logger?.ZLogError($"{message}");
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
        _logger?.ZLogError(exception, $"{message}");
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
