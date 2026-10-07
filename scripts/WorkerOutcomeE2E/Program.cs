using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Lhamiel;
using Lhamiel.Util;
using Lhamiel.View;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

internal static class OutcomeProbe
{
    private static string Root = "";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "AppDataDirectory")]
    private static extern ref string AppDataDirectory(Settings? owner);
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "SettingsFilePath")]
    private static extern ref string SettingsFilePath(Settings? owner);

    [STAThread]
    public static void Main(string[] args)
    {
        Root = Path.GetFullPath(Environment.GetEnvironmentVariable("LHAMIEL_OUTCOME_TEST_ROOT")
            ?? throw new Exception("LHAMIEL_OUTCOME_TEST_ROOT is required"));
        Directory.CreateDirectory(Root);
        var settingsDirectory = Path.Combine(Root, "settings");
        Directory.CreateDirectory(settingsDirectory);
        AppDataDirectory(null) = settingsDirectory;
        SettingsFilePath(null) = Path.Combine(settingsDirectory, "settings.json");
        if (args.Contains("--extraction-worker") || args.Contains("--compression-worker"))
        {
            RunChild(args);
            return;
        }
        if (args.FirstOrDefault() is "--extract" or "--ui-host")
        {
            RunRealParent(args);
            return;
        }
        try { RunParent(args.Single()).GetAwaiter().GetResult(); }
        catch (Exception ex) { Write("probe-error.json", new { ex.Message, ex.StackTrace }); Environment.ExitCode = 1; }
    }

    private static void RunRealParent(string[] args)
    {
        using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            Write($"parent-{Environment.ProcessId}-window-{window.GetType().Name}.json", new {
                pid = Environment.ProcessId, at = DateTimeOffset.UtcNow, type = window.GetType().Name, window.Title });
            if (window is MessageDialog) Dispatcher.UIThread.Post(window.Close);
            if (window is MainWindow && args.Contains("--ui-host"))
                Dispatcher.UIThread.Post(async () =>
                {
                    try
                    {
                        var vm = Lhamiel.ViewModels.MainWindowViewModel.Current ?? throw new Exception("ViewModel missing");
                        await vm.ProcessDroppedPathsAsync([Path.Combine(Root, "good.zip"), Path.Combine(Root, "broken.zip")]);
                        Write("ui-parent-status.json", new { window.IsVisible, environmentExitCode = Environment.ExitCode,
                            at = DateTimeOffset.UtcNow });
                    }
                    catch (Exception ex) { Write("ui-parent-error.json", new { ex.Message }); Environment.ExitCode = 1; }
                    finally { ((IClassicDesktopStyleApplicationLifetime)App.Current!.ApplicationLifetime!).Shutdown(); }
                });
        });
        try { Lhamiel.Program.Main(args); }
        finally { Write("real-parent-returned.json", new { pid = Environment.ProcessId, exitCode = Environment.ExitCode, at = DateTimeOffset.UtcNow }); }
    }

    private static void RunChild(string[] args)
    {
        var request = CompressionWorkerLauncher.CurrentRequest ?? throw new Exception("Missing request");
        var pid = Environment.ProcessId;
        Write($"worker-{pid}-started.json", new { pid, at = DateTimeOffset.UtcNow, request.SourcePaths,
            request.CancellationEventName, productAssembly = typeof(App).Assembly.Location,
            productSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))) });
        using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            Write($"worker-{pid}-window-{window.GetType().Name}.json", new { pid, at = DateTimeOffset.UtcNow,
                type = window.GetType().FullName, window.Title });
            var mode = Environment.GetEnvironmentVariable("LHAMIEL_OUTCOME_MODE");
            if (window is ProgressWindow && Path.GetFileName(request.SourcePaths[0]) == "cancel.zip")
                // Show / Activate と初期化から戻った後、実タイトルバーと同じ終了経路を実行する。
                Dispatcher.UIThread.Post(window.Close);
            if (window is MessageDialog && mode != "--group")
                Dispatcher.UIThread.Post(window.Close); // 子で既に表示した本文を一度だけ閉じる。
            if (window is PasswordDialog && mode == "--password")
                Dispatcher.UIThread.Post(window.Close);
        });
        try { Lhamiel.Program.Main(args); }
        finally { Write($"worker-{pid}-returned.json", new { pid, at = DateTimeOffset.UtcNow, exitCode = Environment.ExitCode }); }
    }

    private static async Task RunParent(string mode)
    {
        if (mode is not ("--observe" or "--mixed" or "--individual" or "--group" or "--password" or "--cli" or "--ui"))
            throw new Exception("Unknown mode");
        Write("failure-cases.json", new { cases = new[] {
            "Child logical failure exits zero", "Individual cancellation counts as success", "Parent progress reaches 100% with a failed child",
            "Parent returns before every launched child exits", "Group cancellation is swallowed", "Error body is shown again by the parent",
            "Successful archive bytes differ", "Worker output escapes isolated settings" } });
        Environment.SetEnvironmentVariable("LHAMIEL_OUTCOME_MODE", mode);
        var settings = new Settings { Check4UpdatesOnStartup = false, OpenExtractionOutputFolder = false,
            OpenCompressionOutputFolder = false, ExtractionOutputToSameDirectory = false,
            AddExtractToContextMenu = false, AddCompressToContextMenu = false,
            ExtractionOutputDirectory = Path.Combine(Root, "output"), CreateArchiveNameFolder = true };
        settings.Save();
        Directory.CreateDirectory(settings.ExtractionOutputDirectory);
        MakeZip("good.zip");
        MakeZip("cancel.zip");
        File.WriteAllText(Path.Combine(Root, "broken.zip"), "This is not an archive.");
        File.WriteAllText(Path.Combine(Root, "broken2.zip"), "This is not an archive either.");
        string[] names = mode switch {
            "--individual" => ["good.zip", "cancel.zip"],
            "--group" => ["broken.zip", "broken2.zip"],
            "--password" => ["good.zip", "password.zip"],
            _ => ["good.zip", "broken.zip"] };
        if (mode == "--password" && !File.Exists(Path.Combine(Root, "password.zip")))
        {
            var fixtureDirectory = Path.Combine(Root, "password-fixture-source");
            Directory.CreateDirectory(fixtureDirectory);
            var fixtureSource = Path.Combine(fixtureDirectory, "hello.txt");
            File.WriteAllText(fixtureSource, "harmless password fixture");
            var passwordArchive = Path.Combine(Root, "password.zip");
            var skipped = await ArchiveCompressor.CompressFilesAsync([fixtureSource], passwordArchive,
                Cube.FileSystem.SevenZip.Format.Zip, settingsOverride: settings,
                password: "fixed-dummy-fixture-password", encryptFileNames: false);
            if (skipped != 0 || !File.Exists(passwordArchive))
                throw new Exception("Password fixture creation failed");
            Write("password-fixture.json", new { archive = passwordArchive, generated = true,
                fixedTestPassword = true, skipped, sourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixtureSource))),
                archiveSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(passwordArchive))) });
        }
        if (mode is "--cli" or "--ui")
        {
            await RunRealParentProcess(mode);
            return;
        }
        using var cancellation = new CancellationTokenSource();
        var progress = new List<int>();
        var progressSink = new InlineProgress(value => { lock (progress) progress.Add(value); });
        Task extraction = CompressionWorkerLauncher.RunExtractionAsync(
            names.Select(name => Path.Combine(Root, name)).ToArray(), settings, cancellation.Token, progressSink);
        try
        {
        if (mode == "--group")
        {
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (Directory.EnumerateFiles(Root, "worker-*-window-MessageDialog.json").Count() < 2 && DateTime.UtcNow < deadline)
                await Task.Delay(50);
            if (Directory.EnumerateFiles(Root, "worker-*-window-MessageDialog.json").Count() != 2)
                throw new Exception("Two error dialogs did not open");
            Write("group-ready.json", new { at = DateTimeOffset.UtcNow });
            cancellation.Cancel();
        }
        var groupCancelled = false;
        try { await extraction.WaitAsync(TimeSpan.FromSeconds(75)); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { groupCancelled = true; }
        // 修正前の Task と修正後の Task<Result> を同じ観測コードで扱う。
        var result = extraction.Status == TaskStatus.RanToCompletion
            ? extraction.GetType().GetProperty("Result")?.GetValue(extraction) : null;
        var counts = result is null ? null : new {
            succeeded = result.GetType().GetProperty("SucceededCount")?.GetValue(result),
            failed = result.GetType().GetProperty("FailedCount")?.GetValue(result),
            cancelled = result.GetType().GetProperty("CancelledCount")?.GetValue(result) };
        var started = Directory.EnumerateFiles(Root, "worker-*-started.json").ToArray();
        var returned = Directory.EnumerateFiles(Root, "worker-*-returned.json").ToArray();
        var childCodes = returned.Select(path => JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("exitCode").GetInt32()).Order().ToArray();
        var goodBytesMatch = mode == "--group" || File.ReadAllText(Path.Combine(Root, "output", "good", "hello.txt")) == "harmless fixture";
        var allChildrenExited = started.Length == 2 && returned.Length == 2
            && started.All(path => !IsAlive(JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("pid").GetInt32()));
        var expectedCodes = mode switch {
            "--group" => new[] { 11, 11 },
            "--individual" or "--password" => new[] { 0, 11 },
            _ => new[] { 0, 10 } };
        var countsMatch = mode == "--group" || (counts is not null
            && Equals(counts.succeeded, 1)
            && Equals(counts.failed, mode is "--individual" or "--password" ? 0 : 1)
            && Equals(counts.cancelled, mode is "--individual" or "--password" ? 1 : 0));
        var passed = allChildrenExited && goodBytesMatch && childCodes.SequenceEqual(expectedCodes)
            && countsMatch && (mode == "--group" ? groupCancelled : progress.SequenceEqual(new[] { 1 }));
        Write("result.json", new { mode, at = DateTimeOffset.UtcNow, productAssembly = typeof(App).Assembly.Location,
            productSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))),
            counts, countsMatch, progress, childCodes, expectedCodes, groupCancelled, allChildrenExited, goodBytesMatch, passed,
            messageDialogCount = Directory.EnumerateFiles(Root, "worker-*-window-MessageDialog.json").Count() });
        if (mode != "--observe" && !passed) throw new Exception("Outcome E2E acceptance failed; inspect result.json");
        }
        finally
        {
            // 観測失敗時にも今回の worker だけへ共通取消を送り、終了を待つ。
            cancellation.Cancel();
            if (!extraction.IsCompleted)
            {
                try { await extraction.WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (Exception ex) { Write("cleanup-incomplete.json", new { ex.Message, at = DateTimeOffset.UtcNow }); }
            }
        }
    }

    private static async Task RunRealParentProcess(string mode)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        start.ArgumentList.Add(mode == "--cli" ? "--extract" : "--ui-host");
        if (mode == "--cli")
        {
            start.ArgumentList.Add(Path.Combine(Root, "good.zip"));
            start.ArgumentList.Add(Path.Combine(Root, "broken.zip"));
        }
        using var parent = Process.Start(start) ?? throw new Exception("Real parent failed to start");
        try { await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90)); }
        finally { if (!parent.HasExited) parent.Kill(entireProcessTree: true); }
        var returned = Directory.EnumerateFiles(Root, "worker-*-returned.json").ToArray();
        var childCodes = returned.Select(path => JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("exitCode").GetInt32()).Order().ToArray();
        var allChildrenExited = returned.Length == 2 && returned.All(path =>
            !IsAlive(JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("pid").GetInt32()));
        var parentErrorDialogs = Directory.EnumerateFiles(Root, "parent-*-window-MessageDialog.json").Count();
        var goodBytesMatch = File.ReadAllText(Path.Combine(Root, "output", "good", "hello.txt")) == "harmless fixture";
        var uiStayedOpen = mode != "--ui" || (File.Exists(Path.Combine(Root, "ui-parent-status.json"))
            && JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "ui-parent-status.json"))).RootElement.GetProperty("IsVisible").GetBoolean());
        var expectedParentCode = mode == "--cli" ? 10 : 0;
        var passed = parent.ExitCode == expectedParentCode && childCodes.SequenceEqual(new[] { 0, 10 })
            && allChildrenExited && parentErrorDialogs == 0 && goodBytesMatch && uiStayedOpen;
        Write("result.json", new { mode, parentCode = parent.ExitCode, expectedParentCode, childCodes, allChildrenExited,
            parentErrorDialogs, goodBytesMatch, uiStayedOpen, passed,
            productSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))) });
        if (!passed) throw new Exception("Real parent E2E acceptance failed; inspect result.json");
    }

    private static bool IsAlive(int pid) { try { using var process = Process.GetProcessById(pid); return !process.HasExited; } catch (ArgumentException) { return false; } }
    private static void MakeZip(string name) { using var zip = ZipFile.Open(Path.Combine(Root, name), ZipArchiveMode.Create); using var writer = new StreamWriter(zip.CreateEntry("hello.txt").Open()); writer.Write("harmless fixture"); }
    private static void Write(string name, object value) => File.WriteAllText(Path.Combine(Root, name), JsonSerializer.Serialize(value, JsonOptions));
    private sealed class InlineProgress(Action<int> report) : IProgress<int> { public void Report(int value) => report(value); }
}
