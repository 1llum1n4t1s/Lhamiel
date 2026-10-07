using System.Diagnostics;
using System.Reflection;
using System.Security;
using System.Text.Json;
using Lhamiel.Util;

// 実データやUIへ接触せず、専用dummyツリーで製品の最終配置メソッドを呼ぶ。
var phase = args.Single();
if (phase is not ("before" or "after")) throw new ArgumentException("before / after を指定してください。");
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../"));
var evidence = Path.Combine(repoRoot, ".codex", "gogo-rere-20261007", "evidence");
var root = Path.Combine(evidence, "boundary-" + phase + "-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Logger.Initialize(new LoggerConfig { LogDirectory = Path.Combine(root, "logs"), FilePrefix = "BoundaryProbe", RetentionDays = 0 });
var results = new List<object>();
var failures = 0;
var junctions = new List<string>();
var extractor = typeof(ArchiveExtractor);
var flags = BindingFlags.Static | BindingFlags.NonPublic;

string Case(string name)
{
    var path = Path.Combine(root, name);
    Directory.CreateDirectory(path);
    return path;
}
void Junction(string path, string target)
{
    using var process = Process.Start(new ProcessStartInfo("cmd.exe")
    {
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardError = true, RedirectStandardOutput = true,
        ArgumentList = { "/d", "/c", "mklink", "/J", path, target }
    })!;
    process.WaitForExit();
    if (process.ExitCode != 0) throw new IOException(process.StandardError.ReadToEnd());
    junctions.Add(path);
}
async Task<Exception?> Invoke(string method, params object?[] inputs)
{
    try
    {
        var result = extractor.GetMethod(method, flags)!.Invoke(null, inputs);
        if (result is Task task) await task;
        return null;
    }
    catch (TargetInvocationException ex) { return ex.InnerException ?? ex; }
    catch (Exception ex) { return ex; }
}
void Record(string name, bool pass, object detail)
{
    results.Add(new { name, pass, detail });
    if (!pass) failures++;
    Console.WriteLine($"{name}: {(pass ? "PASS" : "FAIL")}");
}

foreach (var mode in new[] { "new-file", "overwrite-file", "empty-directory" })
{
    var path = Case("merge-" + mode);
    var source = Path.Combine(path, "source");
    var output = Path.Combine(path, "output");
    var external = Path.Combine(path, "dummy-outside-output");
    Directory.CreateDirectory(Path.Combine(source, "link"));
    Directory.CreateDirectory(output);
    Directory.CreateDirectory(external);
    var sentinel = Path.Combine(external, "sentinel.txt");
    File.WriteAllText(sentinel, "original");
    var filename = mode == "overwrite-file" ? "sentinel.txt" : "new.txt";
    if (mode != "empty-directory") File.WriteAllText(Path.Combine(source, "link", filename), "payload");
    Junction(Path.Combine(output, "link"), external);
    var error = await Invoke("MoveExtractedFilesAsync", source, output, null, CancellationToken.None);
    var unchanged = File.ReadAllText(sentinel) == "original" && !File.Exists(Path.Combine(external, "new.txt"));
    var observed = error is SecurityException && unchanged;
    Record("merge-" + mode, phase == "after" ? observed : error == null,
        new { error = error?.GetType().Name, unchanged, payloadExists = File.Exists(Path.Combine(external, filename)) });
}

foreach (var rootLink in new[] { false, true })
{
    var path = Case(rootLink ? "legal-root-link" : "legal-ancestor-link");
    var target = Path.Combine(path, "target");
    var linked = Path.Combine(path, "linked");
    var source = Path.Combine(path, "source");
    Directory.CreateDirectory(target);
    Directory.CreateDirectory(source);
    Junction(linked, target);
    var output = rootLink ? linked : Path.Combine(linked, "output");
    Directory.CreateDirectory(output);
    File.WriteAllText(Path.Combine(source, "new.txt"), "payload");
    var error = await Invoke("MoveExtractedFilesAsync", source, output, null, CancellationToken.None);
    Record(rootLink ? "legal-root-link" : "legal-ancestor-link",
        error == null && File.ReadAllText(Path.Combine(output, "new.txt")) == "payload",
        new { error = error?.GetType().Name });
}

{
    var path = Case("ordinary-merge-skip");
    var source = Path.Combine(path, "source");
    var output = Path.Combine(path, "output");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(output);
    foreach (var name in new[] { "replace.txt", "keep.txt" })
    {
        File.WriteAllText(Path.Combine(source, name), "payload");
        File.WriteAllText(Path.Combine(output, name), "original");
    }
    var error = await Invoke("MoveExtractedFilesAsync", source, output,
        new HashSet<string> { "keep.txt" }, CancellationToken.None);
    Record("ordinary-merge-skip", error == null
        && File.ReadAllText(Path.Combine(output, "replace.txt")) == "payload"
        && File.ReadAllText(Path.Combine(output, "keep.txt")) == "original",
        new { error = error?.GetType().Name });
}

{
    var path = Case("deep-backup");
    var output = Path.Combine(path, "output");
    var external = Path.Combine(path, "dummy-outside-output");
    Directory.CreateDirectory(output);
    Directory.CreateDirectory(external);
    var sentinel = Path.Combine(external, "sentinel.txt");
    File.WriteAllText(sentinel, "original");
    Junction(Path.Combine(output, "link"), external);
    var error = await Invoke("PrepareExistingTargetsForOverwrite",
        new[] { Path.Combine(output, "link", "sentinel.txt") }, output, CancellationToken.None);
    var unchanged = File.Exists(sentinel) && File.ReadAllText(sentinel) == "original";
    Record("deep-backup", phase == "after" ? error != null && unchanged : error == null && !unchanged,
        new { error = error?.GetType().Name, unchanged });
}

foreach (var mode in new[] { "ancestor-link", "file-link" })
{
    var path = Case("copy-" + mode);
    var source = Path.Combine(path, "source");
    var output = Path.Combine(path, "output");
    var external = Path.Combine(path, "dummy-outside-output");
    Directory.CreateDirectory(Path.Combine(source, "link"));
    Directory.CreateDirectory(output);
    Directory.CreateDirectory(external);
    var sentinel = Path.Combine(external, "sentinel.txt");
    File.WriteAllText(sentinel, "original");
    File.WriteAllText(Path.Combine(source, "link", "sentinel.txt"), "payload");
    if (mode == "ancestor-link") Junction(Path.Combine(output, "link"), external);
    else
    {
        Directory.CreateDirectory(Path.Combine(output, "link"));
        try { File.CreateSymbolicLink(Path.Combine(output, "link", "sentinel.txt"), sentinel); }
        catch (IOException ex) when ((ex.HResult & 0xffff) == 1314)
        {
            results.Add(new { name = "copy-file-link", status = "unverified", reason = "Windows symlink privilege unavailable" });
            Console.WriteLine("copy-file-link: UNVERIFIED (Windows symlink privilege unavailable)");
            continue;
        }
    }
    var result = (Task<(bool success, Exception? lastError)>)extractor.GetMethod("TryExtractEntryAsync", flags)!
        .Invoke(null, [source, output, "link/sentinel.txt", false, null, 1, CancellationToken.None])!;
    var (success, error) = await result;
    var unchanged = File.ReadAllText(sentinel) == "original";
    Record("copy-" + mode, phase == "after" ? !success && error is SecurityException && unchanged : success && !unchanged,
        new { success, error = error?.GetType().Name, unchanged });
}

foreach (var mode in new[] { "RestoreFromBackup", "DiscardBackups" })
{
    var path = Case(mode);
    var output = Path.Combine(path, "output");
    var external = Path.Combine(path, "dummy-outside-output");
    Directory.CreateDirectory(output);
    Directory.CreateDirectory(external);
    var sentinel = Path.Combine(external, "sentinel.txt");
    var backup = sentinel + ".Lhamiel_backup_dummy";
    File.WriteAllText(sentinel, "original");
    File.WriteAllText(backup, "backup");
    Junction(Path.Combine(output, "link"), external);
    var originalPath = Path.Combine(output, "link", "sentinel.txt");
    var backups = new List<(string Original, string Backup)> { (originalPath, originalPath + ".Lhamiel_backup_dummy") };
    var method = extractor.GetMethod(mode, flags)!;
    var inputs = method.GetParameters().Length == 1 ? new object[] { backups } : [backups, output];
    var error = await Invoke(mode, inputs);
    var unchanged = File.ReadAllText(sentinel) == "original" && File.Exists(backup) && File.ReadAllText(backup) == "backup";
    Record(mode, phase == "after" ? error == null && unchanged : error == null && !unchanged,
        new { error = error?.GetType().Name, unchanged });
}

var record = new
{
    phase, root, assembly = extractor.Assembly.Location,
    assemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(extractor.Assembly.Location))),
    timestamp = DateTimeOffset.UtcNow, failures, junctions, results,
    cleanup = "dummy tree retained; use global recyclehelper on each recorded leaf junction first, then on the fixture root"
};
var json = JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(evidence, "boundary-" + phase + "-result.json"), json);
Console.WriteLine($"Evidence: {Path.Combine(evidence, "boundary-" + phase + "-result.json")}");
Environment.ExitCode = failures == 0 ? 0 : 1;
