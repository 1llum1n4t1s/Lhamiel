using System.Text.Json;
using Lhamiel.Util;

if (args.Length != 1 || !Path.IsPathFullyQualified(args[0]))
    throw new ArgumentException("未使用の成果物ディレクトリを絶対パスで指定してください。");
var artifactDirectory = args[0];
if (Directory.Exists(artifactDirectory))
    throw new ArgumentException("成果物の上書きを防ぐため、未使用のディレクトリを指定してください。");
Directory.CreateDirectory(artifactDirectory);

// 失敗モード: dump 失敗で要約消失、メッセージ漏洩、通常ログが終了まで未保存。
var failures = new List<string>();
var order = new List<string>();
Exception exception;
try { throw new InvalidOperationException("dummy-secret-outer", new IOException("dummy-secret-inner")); }
catch (Exception caught) { exception = caught; }

Logger.Initialize(new LoggerConfig { LogDirectory = artifactDirectory, FilePrefix = "Lhamiel" });
try
{
    CrashHandler.HandleTerminatingException(exception,
        summary =>
        {
            order.Add("log");
            Logger.Log(summary, LogLevel.Error);
        },
        _ =>
        {
            order.Add("dump");
            // 実際のクラッシュ・MiniDumpWriteDump は起こさず、失敗を注入する。
            throw new IOException("simulated-dump-failure");
        });
    failures.Add("注入した dump 失敗が観測されませんでした。");
}
catch (IOException caught) when (caught.Message == "simulated-dump-failure") { }

// Dispose 前にファイルを読むことで、終端時の同期保存を確認する。
var logText = string.Join("\n", Directory.GetFiles(artifactDirectory, "Lhamiel_*.log").Select(path => { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }));
if (!order.SequenceEqual(new[] { "log", "dump" })) failures.Add("要約と dump の順序が不正です。");
if (!logText.Contains(nameof(InvalidOperationException)) || !logText.Contains(nameof(IOException))
    || !logText.Contains("HResult=0x") || !logText.Contains("Program.cs"))
    failures.Add("型・HResult・スタックが通常ログに保存されませんでした。");
if (logText.Contains("dummy-secret") || logText.Contains("simulated-dump-failure"))
    failures.Add("例外メッセージが通常ログへ漏洩しました。");
Logger.Dispose();
File.WriteAllText(Path.Combine(artifactDirectory, "result.json"), JsonSerializer.Serialize(new
{
    passed = failures.Count == 0,
    order,
    failures,
    limitations = new[] { "実クラッシュ・実ダンプは未実行", "GUI と MotW 負荷試験は別担当" }
}, new JsonSerializerOptions { WriteIndented = true }));
foreach (var failure in failures) Console.Error.WriteLine(failure);
Console.WriteLine(artifactDirectory);
return failures.Count == 0 ? 0 : 1;
