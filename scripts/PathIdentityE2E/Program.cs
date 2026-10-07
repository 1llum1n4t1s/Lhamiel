using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Lhamiel.Util;

if (args[0] == "worker")
{
    File.WriteAllText(args[3] + ".started", "started");
    if (args[1].EndsWith("-cancel", StringComparison.Ordinal))
    {
        using var cancel = new CancellationTokenSource(300);
        try
        {
            using var canceledGate = args[1] == "mutex-cancel"
                ? await CrossProcessResourceGate.EnterAsync(args[2], cancel.Token)
                : await ExtractionDestinationGate.EnterAsync(args[2], cancel.Token);
            throw new InvalidOperationException("Legacy-held waiter did not cancel.");
        }
        catch (OperationCanceledException) { File.WriteAllText(args[3] + ".canceled", "canceled"); }
        return;
    }
    using var gate = args[1] switch
    {
        "mutex" => await CrossProcessResourceGate.EnterAsync(args[2], CancellationToken.None),
        "legacy-mutex" => await LegacyCrossProcessResourceGate.EnterAsync(args[2], CancellationToken.None),
        "legacy-gate" => await LegacyExtractionDestinationGate.EnterAsync(args[2]),
        _ => await ExtractionDestinationGate.EnterAsync(args[2])
    };
    File.WriteAllText(args[3] + ".acquired", "acquired");
    if (args[1] == "commit") File.WriteAllText(args[2], "B-success");
    return;
}

var root = Path.GetFullPath(args[0]);
var expectedBlocked = args[1] == "after";
var real = Path.Combine(root, "real");
var alias = Path.Combine(root, "alias");
var results = new List<string>();
var children = new List<Process>();
try
{
    var final = Path.Combine(real, "result.zip");
    var backup = final + ".backup";
    File.WriteAllText(final, "original");
    Process child;
    using (await ExtractionDestinationGate.EnterAsync(final))
    {
        File.Move(final, backup);
        child = Start("commit", Path.Combine(alias, "result.zip"), "rollback");
        await WaitFor(Path.Combine(root, "rollback.started"));
        await Task.Delay(600);
        Require(File.Exists(Path.Combine(root, "rollback.acquired")) != expectedBlocked, "Alias gate blocking differs from expectation.");
        if (!expectedBlocked)
        {
            await Finish(child);
            Require(File.ReadAllText(final) == "B-success", "B did not commit.");
            // 削除相当の失われた成果物を証拠として残し、実際の直接削除は行わない。
            File.Move(final, final + ".lost-success");
        }
        File.Move(backup, final);
    }
    await Finish(child);
    Require(File.ReadAllText(final) == (expectedBlocked ? "B-success" : "original"), "Rollback output mismatch.");
    results.Add(expectedBlocked ? "PASS alias rollback serialized; B output survives" : "REPRO alias bypass; B success displaced by A rollback");

    foreach (var kind in new[] { "gate", "mutex" })
    {
        using (kind == "mutex" ? await CrossProcessResourceGate.EnterAsync(final, CancellationToken.None) : await ExtractionDestinationGate.EnterAsync(real))
        {
            var marker = kind + "-parent";
            child = Start(kind, Path.Combine(alias, "result.zip"), marker);
            await WaitFor(Path.Combine(root, marker + ".started"));
            await Task.Delay(600);
            Require(File.Exists(Path.Combine(root, marker + ".acquired")) != expectedBlocked, kind + " alias protection mismatch.");
        }
        await Finish(child);
        results.Add("PASS " + kind + " alias/ancestor expectation");
    }

    using (await ExtractionDestinationGate.EnterAsync(final))
    {
        child = Start("gate", Path.Combine(alias, "sibling.zip"), "sibling");
        await Finish(child);
    }
    results.Add("PASS sibling outputs remain parallel");

    using (await ExtractionDestinationGate.EnterAsync(final))
    {
        using var cancel = new CancellationTokenSource(300);
        try
        {
            using var waiter = await ExtractionDestinationGate.EnterAsync(expectedBlocked ? Path.Combine(alias, "result.zip") : final, cancel.Token);
            throw new InvalidOperationException("Waiting request was not canceled.");
        }
        catch (OperationCanceledException) { results.Add("PASS canceled waiter exits"); }
    }
    using (await ExtractionDestinationGate.EnterAsync(Path.Combine(alias, "result.zip"))) { }
    results.Add("PASS gate reacquires after cancellation");

    Settings.AppDataDirectory = Path.Combine(root, "registry-settings");
    var plan = CompressionOutputRegistry.Plan.Create(Path.Combine(alias, "result.zip"));
    var resolver = OutputPathIdentity.CreateResolver([real]);
    string ScanKey(string path) => expectedBlocked ? resolver.GetKey(path) : Path.GetFullPath(path);
    using (var output = CompressionOutputRegistry.Register([plan]))
    using (var scan = CompressionOutputRegistry.BeginScan(default))
    {
        Require(scan.Initial.Contains(ScanKey(final)) == expectedBlocked, "Initial alias exclusion mismatch.");
        Require(scan.Initial.Contains(ScanKey(Path.Combine(real, Path.GetFileName(plan.StagingDirectory), "child.tmp"))) == expectedBlocked,
            "Staging child exclusion mismatch.");
        Require(!scan.Initial.Contains(ScanKey(Path.Combine(real, "sibling.zip"))), "Sibling incorrectly excluded.");
        Require(scan.Finish().Contains(ScanKey(final)) == expectedBlocked, "Finished alias exclusion mismatch.");
    }
    using (var scan = CompressionOutputRegistry.BeginScan(default))
    {
        using (var late = CompressionOutputRegistry.Register([plan])) { }
        Require(scan.Finish().Contains(ScanKey(final)) == expectedBlocked, "Late alias exclusion mismatch.");
    }
    results.Add(expectedBlocked ? "PASS initial/staging/late alias exclusion; sibling retained" : "REPRO initial/staging/late alias exclusion bypass");

    if (expectedBlocked)
    {
        foreach (var kind in new[] { "gate", "mutex" })
        {
            var scenarios = kind == "gate"
                ? new[] { ("same", final, final), ("parent", real, final), ("child", final, real),
                    ("alias-same", Path.Combine(alias, "result.zip"), Path.Combine(alias, "result.zip")),
                    ("canonical-form", OutputPathIdentity.GetCanonicalPath(final), OutputPathIdentity.GetCanonicalPath(final)),
                    ("long-prefix", @"\\?\" + final, @"\\?\" + final) }
                : new[] { ("same", final, final), ("alias-same", Path.Combine(alias, "result.zip"), Path.Combine(alias, "result.zip")),
                    ("canonical-form", OutputPathIdentity.GetCanonicalPath(final), OutputPathIdentity.GetCanonicalPath(final)),
                    ("long-prefix", @"\\?\" + final, @"\\?\" + final) };
            foreach (var (name, holderPath, waiterPath) in scenarios)
            {
                foreach (var legacyHolds in new[] { true, false })
                {
                    var marker = $"mixed-{kind}-{name}-{legacyHolds}";
                    using (await Acquire(kind, holderPath, legacyHolds))
                    {
                        child = Start((legacyHolds ? "" : "legacy-") + kind, waiterPath, marker);
                        await WaitFor(Path.Combine(root, marker + ".started"));
                        await Task.Delay(350);
                        Require(!File.Exists(Path.Combine(root, marker + ".acquired")), "Mixed holder bypass: " + marker);
                    }
                    await Finish(child);
                    results.Add("PASS " + marker);
                }
            }
            using (await Acquire(kind, final, legacy: true))
            {
                child = Start(kind + "-cancel", final, "mixed-" + kind + "-cancel");
                await Finish(child);
                Require(File.Exists(Path.Combine(root, "mixed-" + kind + "-cancel.canceled")), "Mixed cancellation evidence missing.");
                child = Start(kind, Path.Combine(real, "mixed-sibling.zip"), "mixed-" + kind + "-sibling");
                await Finish(child);
            }
            using (await Acquire(kind, final, legacy: false)) { }
            results.Add("PASS mixed " + kind + " cancellation/reacquire/sibling parallel");
        }
        var missing = Path.Combine(real, "not-created", "child", "output.zip");
        Require(OutputPathIdentity.GetCanonicalPath(missing) == OutputPathIdentity.GetCanonicalPath(Path.Combine(alias, "not-created", "child", "output.zip")), "Missing suffix alias differs.");
        Require(OutputPathIdentity.GetCanonicalPath(final) == OutputPathIdentity.GetCanonicalPath(@"\\?\" + final), "Long prefix differs.");
        var aliasResolver = OutputPathIdentity.CreateResolver([alias, @"\\?\" + real]);
        Require(aliasResolver.GetKey(Path.Combine(alias, "not-created", "child", "output.zip")) == OutputPathIdentity.GetCanonicalPath(missing), "Root resolver suffix differs.");
        var stableKey = OutputPathIdentity.GetCanonicalPath(final);
        File.Move(final, backup);
        Require(stableKey == OutputPathIdentity.GetCanonicalPath(final), "Final identity changed during backup.");
        File.Move(backup, final);
        Require(stableKey == OutputPathIdentity.GetCanonicalPath(final), "Final identity changed after restore.");
        var otherLink = Path.Combine(real, "hardlink.zip");
        Require(File.Exists(otherLink), "Hardlink fixture is missing.");
        Require(stableKey != OutputPathIdentity.GetCanonicalPath(otherLink), "Hardlinks share a replacement namespace key.");
        var fileSymlink = Path.Combine(real, "file-symlink.zip");
        Require(File.Exists(fileSymlink), "File symlink fixture is missing.");
        Require(stableKey != OutputPathIdentity.GetCanonicalPath(fileSymlink), "File symlinks share a replacement namespace key.");
        var longDirectory = Path.Combine(real, new string('a', 100), new string('b', 100), new string('c', 100));
        Directory.CreateDirectory(longDirectory);
        Require(OutputPathIdentity.GetCanonicalPath(Path.Combine(longDirectory, "long.zip")) ==
            OutputPathIdentity.GetCanonicalPath(Path.Combine(alias, Path.GetRelativePath(real, longDirectory), "long.zip")), "Long path alias differs.");
        bool failedClosed = false;
        try { OutputPathIdentity.GetCanonicalPath(Path.Combine(final, "invalid-child")); }
        catch (IOException) { failedClosed = true; }
        Require(failedClosed, "File ancestor identity did not fail closed.");
        var timer = Stopwatch.StartNew();
        for (var index = 0; index < 50_000; index++) aliasResolver.GetKey(Path.Combine(alias, "nested", index + ".txt"));
        results.Add("PASS missing suffix/long prefix/long path/file-link namespace/backup key stability/file ancestor fail closed; 50000 cached keys ms=" + timer.ElapsedMilliseconds);
    }
    File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    foreach (var result in results) Console.WriteLine(result);
    Console.WriteLine(root);
}
finally
{
    foreach (var child in children)
    {
        if (!child.HasExited) child.Kill(entireProcessTree: true);
        child.Dispose();
    }
}

Process Start(string mode, string path, string name)
{
    var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
    foreach (var argument in new[] { Assembly.GetExecutingAssembly().Location, "worker", mode, path, Path.Combine(root, name) }) info.ArgumentList.Add(argument);
    var child = Process.Start(info) ?? throw new InvalidOperationException("Worker failed to start.");
    children.Add(child);
    return child;
}
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static Task<IDisposable> Acquire(string kind, string path, bool legacy) => (kind, legacy) switch
{
    ("mutex", true) => LegacyCrossProcessResourceGate.EnterAsync(path, CancellationToken.None),
    ("mutex", false) => CrossProcessResourceGate.EnterAsync(path, CancellationToken.None),
    (_, true) => LegacyExtractionDestinationGate.EnterAsync(path),
    _ => ExtractionDestinationGate.EnterAsync(path)
};
static async Task WaitFor(string path)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    while (!File.Exists(path)) await Task.Delay(25, timeout.Token);
}
static async Task Finish(Process child)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await child.WaitForExitAsync(timeout.Token);
    Require(child.ExitCode == 0, "Worker exit " + child.ExitCode);
}
