using System.Diagnostics;
using System.IO;
using System.Text;

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
/// ログ出力機能を提供するクラス
/// </summary>
public static class Logger
{
    /// <summary>
    /// ログファイルのパス
    /// </summary>
    private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lhamiel.log");

    /// <summary>
    /// ログファイルの最大行数
    /// </summary>
    private const int MaxLogLines = 1000;

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
    /// ログを出力する
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    /// <param name="level">ログレベル（デフォルト: Info）</param>
    public static void Log(string message, LogLevel level = LogLevel.Info)
    {
        if (level < MinLogLevel)
            return;

        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logMessage = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";

            File.AppendAllText(LogFilePath, logMessage, Encoding.UTF8);
            TrimLogFile();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ログ出力エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 複数行のログを出力する
    /// </summary>
    /// <param name="messages">ログメッセージの配列</param>
    /// <param name="level">ログレベル（デフォルト: Info）</param>
    public static void LogLines(string[] messages, LogLevel level = LogLevel.Info)
    {
        if (level < MinLogLevel)
            return;

        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logLines = messages.Select(message => $"[{timestamp}] [{level}] {message}").ToArray();

            File.AppendAllLines(LogFilePath, logLines, Encoding.UTF8);
            TrimLogFile();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ログ出力エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 例外情報を含むログを出力する（常にErrorレベル）
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    /// <param name="exception">例外オブジェクト</param>
    public static void LogException(string message, Exception exception)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logMessage = $"[{timestamp}] [Error] {message}\n例外: {exception.Message}\nスタックトレース: {exception.StackTrace}";

            // InnerExceptionも記録
            if (exception.InnerException != null)
            {
                logMessage += $"\nInnerException: {exception.InnerException.Message}\nInnerStackTrace: {exception.InnerException.StackTrace}";
            }

            logMessage += Environment.NewLine;

            File.AppendAllText(LogFilePath, logMessage, Encoding.UTF8);
            TrimLogFile();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ログ出力エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// ログファイルを最大行数に制限する
    /// </summary>
    private static void TrimLogFile()
    {
        try
        {
            if (File.Exists(LogFilePath))
            {
                var lines = File.ReadAllLines(LogFilePath, Encoding.UTF8);
                if (lines.Length > MaxLogLines)
                {
                    var trimmedLines = lines.Skip(lines.Length - MaxLogLines).ToArray();
                    File.WriteAllLines(LogFilePath, trimmedLines, Encoding.UTF8);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ログファイル整理エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// アプリケーション起動時のログを出力する（Debugレベル）
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    public static void LogStartup(string[] args)
    {
        var messages = new List<string>
        {
            "=== Lhamiel 起動ログ ===",
            $"起動時刻: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}",
            $"実行ファイルパス: {Environment.ProcessPath}",
            $"コマンドライン引数の数: {args.Length}",
            "コマンドライン引数:"
        };

        for (var i = 0; i < args.Length; i++)
        {
            messages.Add($"  [{i}]: {args[i]}");
        }

        LogLines(messages.ToArray(), LogLevel.Debug);
    }
}
