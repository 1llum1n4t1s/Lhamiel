using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lhamiel;
using Lhamiel.Util;

// 失敗条件: ディレクトリリンクを置換する、リンク先が変わる、説明せず成功0で終了する、ダイアログが残る。
internal static class Probe
{
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "AppDataDirectory")]
    private static extern ref string DataDirectory(Settings? owner);
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "SettingsFilePath")]
    private static extern ref string SettingsPath(Settings? owner);

    [STAThread]
    private static int Main(string[] args)
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("LHAMIEL_LEAF_GUARD_ROOT")
            ?? throw new InvalidOperationException("Fixture root missing"));
        var settings = Path.Combine(root, "settings");
        DataDirectory(null) = settings;
        SettingsPath(null) = Path.Combine(settings, "settings.json");
        var processPrefix = Environment.ProcessId.ToString();
        using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            if (window.GetType().Name != "MessageDialog") return;
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(80);
                var body = window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text)
                    .Where(text => !string.IsNullOrEmpty(text)).ToArray();
                Write(root, processPrefix + "-dialog.json", new { window.Title, body,
                    worker = Lhamiel.Program.IsArchiveWorker });
                window.Close();
            });
        });
        Lhamiel.Program.Main(args);
        var code = Environment.ExitCode;
        Write(root, processPrefix + "-returned.json", new { code, worker = Lhamiel.Program.IsArchiveWorker });
        if (!Lhamiel.Program.IsArchiveWorker)
        {
            var dialogs = Directory.GetFiles(root, "*-dialog.json")
                .Select(path => JsonDocument.Parse(File.ReadAllText(path))).ToArray();
            var target = Path.Combine(root, "target", "preserved.txt");
            var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)));
            var expected = File.ReadAllText(Path.Combine(root, "target-sha256.txt")).Trim();
            var namedLeaf = Path.Combine(root, "input", "payload.zip");
            var linkRetained = Directory.Exists(namedLeaf)
                && (File.GetAttributes(namedLeaf) & FileAttributes.ReparsePoint) != 0;
            var localized = dialogs.Any(dialog => dialog.RootElement.GetProperty("body").EnumerateArray()
                .Any(text => text.GetString()?.Contains("圧縮の出力先がフォルダーへのリンクのため、上書きできません", StringComparison.Ordinal) == true));
            foreach (var dialog in dialogs) dialog.Dispose();
            var passed = code == 10 && actual == expected && linkRetained && localized && dialogs.Length == 1;
            Write(root, "result.json", new { passed, parentExitCode = code, targetHashMatches = actual == expected,
                linkRetained, localized, dialogCount = dialogs.Length,
                productSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))) });
            return passed ? 0 : 1;
        }
        return code;
    }

    private static void Write(string root, string name, object value) => File.WriteAllText(Path.Combine(root, name),
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
}
