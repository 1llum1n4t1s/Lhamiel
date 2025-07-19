using System.Diagnostics;
using System.IO;
using System.Text;

namespace Lhamiel.Util;

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
    /// ログを出力する
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    public static void Log(string message)
    {
#if DEBUG
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logMessage = $"[{timestamp}] {message}{Environment.NewLine}";

            File.AppendAllText(LogFilePath, logMessage, Encoding.UTF8);
            TrimLogFile();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ログ出力エラー: {ex.Message}");
        }
#endif
    }

    /// <summary>
    /// 複数行のログを出力する
    /// </summary>
    /// <param name="messages">ログメッセージの配列</param>
    public static void LogLines(string[] messages)
    {
#if DEBUG
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logLines = messages.Select(message => $"[{timestamp}] {message}").ToArray();

            File.AppendAllLines(LogFilePath, logLines, Encoding.UTF8);
            TrimLogFile();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ログ出力エラー: {ex.Message}");
        }
#endif
    }

    /// <summary>
    /// 例外情報を含むログを出力する
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    /// <param name="exception">例外オブジェクト</param>
    public static void LogException(string message, Exception exception)
    {
#if DEBUG
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logMessage = $"[{timestamp}] {message}\n例外: {exception.Message}\nスタックトレース: {exception.StackTrace}{Environment.NewLine}";

            File.AppendAllText(LogFilePath, logMessage, Encoding.UTF8);
            TrimLogFile();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ログ出力エラー: {ex.Message}");
        }
#endif
    }

    /// <summary>
    /// ログファイルを最大行数に制限する
    /// </summary>
    private static void TrimLogFile()
    {
#if DEBUG
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
#endif
    }

    /// <summary>
    /// アプリケーション起動時のログを出力する
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    public static void LogStartup(string[] args)
    {
#if DEBUG
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

        LogLines(messages.ToArray());
#endif
    }
}
