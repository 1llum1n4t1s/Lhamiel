namespace Lhamiel.Util;

// 台帳だけを実装へ接続し、ユーザー設定・一時削除・実ログはこの fixture に持ち込まない。
internal static class Settings
{
    internal static string AppDataDirectory { get; set; } = "";
}
internal static class TempCleanup
{
    internal static bool RegisterTrackedDirectory(string path) => Path.IsPathFullyQualified(path);
    internal static void UnregisterTrackedDirectory(string path) { _ = Path.IsPathFullyQualified(path); }
}
internal enum LogLevel { Warning }
internal static class Logger
{
    internal static void Log(string message, LogLevel level) => Console.WriteLine(level + ": " + message);
}
internal static class App
{
    internal static string Text(string key, params object[] args) => key + ": " + string.Join(", ", args);
}
