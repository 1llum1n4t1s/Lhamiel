using log4net;
using log4net.Config;
using System.IO;
using System.Reflection;

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
/// Log4netを使用したログ出力機能を提供するクラス
/// </summary>
public static class Logger
{
    private static readonly ILog log = LogManager.GetLogger(typeof(Logger));
    private static bool isConfigured = false;

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
    /// Log4netを初期化する
    /// </summary>
    public static void Initialize()
    {
        if (!isConfigured)
        {
            // Log4net初期化前にログファイルをトリミング
            TruncateLogFileIfNeeded();

            var entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var logRepository = LogManager.GetRepository(entryAssembly);
            var configFile = new FileInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log4net.config"));

            if (configFile.Exists)
            {
                XmlConfigurator.Configure(logRepository, configFile);
            }
            else
            {
                // 設定ファイルがない場合は基本的な設定を使用
                BasicConfigurator.Configure(logRepository);
            }

            isConfigured = true;
        }
    }

    /// <summary>
    /// ログファイルが設定された最大行数を超えていたら古い行を削除する（起動時に1回のみ実行）
    /// </summary>
    public static void TruncateLogFileIfNeeded()
    {
        var settings = Settings.Load();
        var maxLines = settings.LogMaxLines > 0 ? settings.LogMaxLines : 1000;
        var logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lhamiel.log");
        var tempFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lhamiel.log.tmp");

        try
        {
            if (!File.Exists(logFilePath))
                return;

            // ファイルサイズで簡易チェック（パフォーマンス改善）
            var fileInfo = new FileInfo(logFilePath);
            if (fileInfo.Length < maxLines * 80) // 1行あたり平均80バイトと仮定
                return;

            // 全行を読み込む
            var allLines = File.ReadAllLines(logFilePath);
            var lineCount = allLines.Length;

            if (lineCount <= maxLines)
                return;

            // 最後のmaxLines行のみを保持
            var linesToKeep = allLines.Skip(lineCount - maxLines).ToArray();
            
            // 一時ファイルに書き込み
            File.WriteAllLines(tempFilePath, linesToKeep, System.Text.Encoding.UTF8);

            // 元のファイルを削除して一時ファイルをリネーム
            File.Delete(logFilePath);
            File.Move(tempFilePath, logFilePath);
            
            // コンソールに出力（デバッグ用）
            Console.WriteLine($"ログファイルをトリミングしました: {lineCount}行 -> {linesToKeep.Length}行（最大行数: {maxLines}行）");
            System.Diagnostics.Debug.WriteLine($"ログファイルをトリミングしました: {lineCount}行 -> {linesToKeep.Length}行（最大行数: {maxLines}行）");
        }
        catch (Exception ex)
        {
            // エラーをコンソールに出力
            Console.WriteLine($"ログファイルのトリミングに失敗しました: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"ログファイルのトリミングに失敗しました: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"スタックトレース: {ex.StackTrace}");
            
            // 一時ファイルが残っている場合は削除
            try
            {
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
            }
            catch
            {
                // 無視
            }
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

        Initialize();

        switch (level)
        {
            case LogLevel.Debug:
                log.Debug(message);
                break;
            case LogLevel.Info:
                log.Info(message);
                break;
            case LogLevel.Warning:
                log.Warn(message);
                break;
            case LogLevel.Error:
                log.Error(message);
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
        Initialize();

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
        Initialize();

        log.Error(message, exception);
    }

    /// <summary>
    /// アプリケーション起動時のログを出力する（Debugレベル）
    /// </summary>
    /// <param name="args">コマンドライン引数</param>
    public static void LogStartup(string[] args)
    {
        Initialize();

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
