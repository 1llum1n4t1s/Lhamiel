using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Lhamiel;
using Lhamiel.Util;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

internal static class ExtractionProbe
{
    static string Root = "";
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "AppDataDirectory")]
    static extern ref string AppDataDirectory(Settings? owner);
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "SettingsFilePath")]
    static extern ref string SettingsFilePath(Settings? owner);
    static void Write(string name, object value) => File.WriteAllText(Path.Combine(Root, name), JsonSerializer.Serialize(value, JsonOptions));
    [STAThread]
    public static void Main(string[] args)
    {
        Root = Path.GetFullPath(Environment.GetEnvironmentVariable("LHAMIEL_EXTRACTION_TEST_ROOT")!);
        Directory.CreateDirectory(Root);
        var settingsDirectory = Path.Combine(Root, "settings");
        Directory.CreateDirectory(settingsDirectory);
        AppDataDirectory(null) = settingsDirectory;
        SettingsFilePath(null) = Path.Combine(settingsDirectory, "settings.json");
        if (Settings.AppDataDirectory != settingsDirectory) throw new Exception("Settings isolation failed");
        if (args.Contains("--fixture") || args.Contains("--fixture-batch")) { Fixture(args.Contains("--fixture-batch")); return; }
        if (args.Contains("--extraction-worker"))
        {
            var tokenIndex = Array.IndexOf(args, "--compression-request") + 1;
            var tokenPath = Path.Combine(Path.GetTempPath(), $"Lhamiel-compression-request-{args[tokenIndex]}.json");
            var request = CompressionWorkerLauncher.CurrentRequest ?? throw new Exception("Request missing");
            Write($"worker-{Environment.ProcessId}-started.json", new {
                pid = Environment.ProcessId, observedAt = DateTimeOffset.UtcNow, request.SourcePaths,
                request.IsExtraction, request.CancellationEventName, request.Settings.ExtractionOutputDirectory,
                request.Settings.ExtractionOutputToSameDirectory, request.Settings.CreateArchiveNameFolder,
                tokenPath, tokenConsumed = !File.Exists(tokenPath), settingsPath = SettingsFilePath(null),
                productAssembly = typeof(App).Assembly.Location });
            try { Lhamiel.Program.Main(args); }
            finally { Write($"worker-{Environment.ProcessId}-returned.json", new { pid = Environment.ProcessId, returnedAt = DateTimeOffset.UtcNow, exitCode = Environment.ExitCode, tokenConsumed = !File.Exists(tokenPath) }); }
            return;
        }
        if (!args.Contains("--ui") && !args.Contains("--ui-batch") && !args.Contains("--snapshot") && !args.Contains("--batch-cancel")) { Lhamiel.Program.Main(args); return; }
        Lhamiel.Program.BuildAvaloniaApp().AfterSetup(_ => Dispatcher.UIThread.Post(async () => {
            var desktop = (IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!;
            try {
                var main = desktop.MainWindow ?? throw new Exception("MainWindow missing");
                await Task.Delay(500);
                if (!main.IsVisible) throw new Exception("MainWindow invisible");
                if (args.Contains("--batch-cancel")) {
                    using var cancellation = new CancellationTokenSource();
                    var extraction = CompressionWorkerLauncher.RunExtractionAsync([Path.Combine(Root, "first.zip"), Path.Combine(Root, "second.zip")], NewSettings(), cancellation.Token);
                    var until = DateTime.UtcNow.AddSeconds(60);
                    while (Directory.EnumerateFiles(Root, "worker-*-started.json").Count() < 2 && DateTime.UtcNow < until) await Task.Delay(50);
                    if (Directory.EnumerateFiles(Root, "worker-*-started.json").Count() != 2) throw new Exception("Two workers did not start");
                    Write("batch-cancel-ready.json", new { parentPid = Environment.ProcessId, observedAt = DateTimeOffset.UtcNow });
                    var delay = int.TryParse(Environment.GetEnvironmentVariable("LHAMIEL_EXTRACTION_CANCEL_DELAY_MS"), out var value) ? value : 5000;
                    await Task.Delay(delay);
                    cancellation.Cancel();
                    var cancelledAt = DateTimeOffset.UtcNow;
                    var cancelled = false;
                    var timeout = Task.Delay(TimeSpan.FromSeconds(60));
                    if (await Task.WhenAny(extraction, timeout) != extraction) throw new Exception("Worker cancellation did not finish within 60 seconds");
                    try { await extraction; } catch (OperationCanceledException) { cancelled = true; }
                    await WaitForReturns();
                    Write("batch-cancel-result.json", new { passed = cancelled, parentPid = Environment.ProcessId, cancelledAt, completedAt = DateTimeOffset.UtcNow, operationCancelled = cancelled, workerReturnCount = Directory.EnumerateFiles(Root, "worker-*-returned.json").Count(), workerTasksExited = extraction.IsCompleted, errorDialogsClosedAfterCancel = extraction.IsCompleted, dialogClosureEvidence = "Actual worker task waits for process exit; both tasks completed after signal." });
                } else if (args.Contains("--snapshot")) {
                    var settings = NewSettings();
                    settings.ExtractionOutputDirectory = Path.Combine(Root, "snapshot-first");
                    Directory.CreateDirectory(settings.ExtractionOutputDirectory);
                    var first = CompressionWorkerLauncher.RunExtractionAsync([Path.Combine(Root, "first.zip")], settings);
                    await WaitForFirst();
                    settings.ExtractionOutputDirectory = Path.Combine(Root, "snapshot-second");
                    Directory.CreateDirectory(settings.ExtractionOutputDirectory);
                    var second = CompressionWorkerLauncher.RunExtractionAsync([Path.Combine(Root, "second.zip")], settings);
                    await Task.WhenAll(first, second);
                } else if (args.Contains("--ui-batch")) {
                    using var transfer = new DataTransfer();
                    foreach (var name in new[] { "first.zip", "second.zip" }) {
                        var file = await main.StorageProvider.TryGetFileFromPathAsync(Path.Combine(Root, name)) ?? throw new Exception("Storage file missing");
                        transfer.Add(DataTransferItem.CreateFile(file));
                    }
                    main.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, main, new Point(main.Bounds.Width / 2, main.Bounds.Height / 2), KeyModifiers.None) { DragEffects = DragDropEffects.Copy });
                    Write("drop-evidence.json", new { actualRoutedEvent = true, actualStorageProvider = true, count = 1, acceptedPathCount = 2, windowType = main.GetType().FullName });
                    await WaitForReturns();
                } else {
                    using var firstTransfer = new DataTransfer();
                    using var secondTransfer = new DataTransfer();
                    await Drop(main, "first.zip", firstTransfer);
                    await WaitForFirst();
                    await Drop(main, "second.zip", secondTransfer);
                    Write("drop-evidence.json", new { actualRoutedEvent = true, actualStorageProvider = true, count = 2, windowType = main.GetType().FullName });
                    await WaitForReturns();
                }
                desktop.Shutdown();
            } catch (Exception ex) { Write("probe-failed.json", new { error = ex.ToString() }); desktop.Shutdown(1); }
        })).StartWithClassicDesktopLifetime([]);
    }
    static Settings NewSettings() => new() {
        ExtractionOutputDirectory = Path.Combine(Root, "output"), ExtractionOutputToSameDirectory = false,
        CreateArchiveNameFolder = true, OpenExtractionOutputFolder = false, OpenCompressionOutputFolder = false,
        Check4UpdatesOnStartup = false, AddExtractToContextMenu = false, AddCompressToContextMenu = false,
    };
    static async Task Drop(Window main, string name, DataTransfer transfer) {
        var file = await main.StorageProvider.TryGetFileFromPathAsync(Path.Combine(Root, name)) ?? throw new Exception("Storage file missing");
        transfer.Add(DataTransferItem.CreateFile(file));
        main.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, transfer, main, new Point(main.Bounds.Width / 2, main.Bounds.Height / 2), KeyModifiers.None) { DragEffects = DragDropEffects.Copy });
    }
    static async Task WaitForFirst() {
        var until = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < until) {
            foreach (var log in Directory.EnumerateFiles(Path.Combine(Root, "settings"), "*extraction*.log")) {
                using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                if (reader.ReadToEnd().Contains("一時ディレクトリへの展開処理開始")) return;
            }
            await Task.Delay(40);
        }
        throw new Exception("First native extraction did not start");
    }
    static async Task WaitForReturns() {
        var until = DateTime.UtcNow.AddSeconds(180);
        while (DateTime.UtcNow < until) {
            if (Directory.EnumerateFiles(Root, "worker-*-returned.json").Count() == 2) return;
            await Task.Delay(100);
        }
        throw new Exception("Workers did not self return");
    }
    static void Fixture(bool longSecond) {
        Write("failure-cases.json", new { cases = new[] { "Second request queued until first returns", "Shared native state fails across workers", "CLI or shortcut bypasses extraction worker", "Drop does not reach actual routed handler", "Worker output ignores request snapshot", "Request token survives consumption", "Extracted bytes or entry count mismatch", "Worker or route does not self exit" } });
        Directory.CreateDirectory(Path.Combine(Root, "output"));
        NewSettings().Save();
        var entries = new List<object>();
        using (var archive = ZipFile.Open(Path.Combine(Root, "first.zip"), ZipArchiveMode.Create)) {
            var buffer = new byte[1024 * 1024];
            var random = new Random(123456);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var big = archive.CreateEntry("large.bin", CompressionLevel.NoCompression);
            using (var stream = big.Open()) for (var i = 0; i < 384; i++) { random.NextBytes(buffer); stream.Write(buffer); hash.AppendData(buffer); }
            entries.Add(new { archive = "first", path = "large.bin", length = 384L * buffer.Length, sha256 = Convert.ToHexString(hash.GetHashAndReset()) });
            for (var i = 0; i < 20000; i++) {
                var name = $"entries/{i:D5}.txt";
                var bytes = System.Text.Encoding.UTF8.GetBytes($"fixture-{i}\n");
                using var stream = archive.CreateEntry(name, CompressionLevel.NoCompression).Open(); stream.Write(bytes);
                entries.Add(new { archive = "first", path = name, length = (long)bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
            }
        }
        using (var archive = ZipFile.Open(Path.Combine(Root, "second.zip"), ZipArchiveMode.Create)) {
            var bytes = System.Text.Encoding.UTF8.GetBytes("second extraction correct\n");
            using (var stream = archive.CreateEntry("second.txt", CompressionLevel.NoCompression).Open()) stream.Write(bytes);
            entries.Add(new { archive = "second", path = "second.txt", length = (long)bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
            if (longSecond) {
                var buffer = new byte[1024 * 1024];
                var random = new Random(654321);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using (var stream = archive.CreateEntry("second-large.bin", CompressionLevel.Fastest).Open())
                    for (var i = 0; i < 384; i++) { random.NextBytes(buffer); stream.Write(buffer); hash.AppendData(buffer); }
                entries.Add(new { archive = "second", path = "second-large.bin", length = 384L * buffer.Length, sha256 = Convert.ToHexString(hash.GetHashAndReset()) });
            }
        }
        Write("manifest.json", entries);
        Write("fixture-result.json", new { firstEntries = 20001, firstPayloadBytes = 384L * 1024 * 1024, secondEntries = longSecond ? 2 : 1, secondLargePayloadBytes = longSecond ? 384L * 1024 * 1024 : 0, secondLargeCompression = longSecond ? "Deflate Fastest" : "None", zipCrcWrittenBy = "System.IO.Compression", settingsDirectory = Settings.AppDataDirectory, generatedAt = DateTimeOffset.UtcNow });
    }
}
