using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Lhamiel.Util;

// 失敗モード: 異種ゲートのすり抜けで rollback が別処理の成功出力を失う、
// 親を排他にして兄弟圧縮を直列化する、取消・例外で途中取得ハンドルが残る。
// 実ゲートをリンクした独立プロセスを barrier で制御し、ダミー出力だけで検証する。
if (args.Length > 0 && args[0] == "worker")
{
    var mode = args[1];
    var path = args[2];
    var marker = args[3];
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(mode == "cancel" ? 1 : 20));
    File.WriteAllText(marker + ".started", mode);
    try
    {
        using var hierarchy = mode == "legacy" ? null : await ExtractionDestinationGate.EnterAsync(path, cts.Token);
        using var mutex = mode == "extraction" ? null : await CrossProcessResourceGate.EnterAsync(path, cts.Token);
        if (mode != "extraction") File.WriteAllText(path, "compression committed");
        File.WriteAllText(marker + ".acquired", mode);
        while (!File.Exists(marker + ".release"))
            await Task.Delay(25, cts.Token);
    }
    catch (OperationCanceledException) when (mode == "cancel")
    {
        File.WriteAllText(marker + ".canceled", mode);
        // 取消後もプロセスを生かし、OS の終了時解放に頼らないことを親が確認する。
        using var releaseDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!File.Exists(marker + ".release")) await Task.Delay(25, releaseDeadline.Token);
    }
    return;
}

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("Cross-process hierarchy checks require Windows.");
var artifactRoot = Path.GetFullPath(args.Length == 1 ? args[0] : Path.Combine(".codex", "gogo-rere-20261007", "output-gate-e2e", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(artifactRoot);
var results = new List<string>();
var children = new List<Process>();
try
{
    // 旧 mutex は展開の退避中でも確定でき、その後の dummy rollback に上書きされる。
    var oldDir = MakeCase("legacy-rollback");
    var oldOutput = Path.Combine(oldDir, "output.zip");
    using (await ExtractionDestinationGate.EnterAsync(oldDir))
    {
        var child = Start("legacy", oldOutput, oldDir);
        await WaitFor(oldDir + ".acquired");
        File.WriteAllText(oldOutput, "extraction rollback restored original");
        Release(oldDir);
        await Finish(child);
        Require(File.ReadAllText(oldOutput) != "compression committed", "Legacy race was not reproduced.");
    }
    results.Add("PASS: legacy mutex bypass reproduced; dummy rollback loses committed output");

    var fixedDir = MakeCase("fixed-rollback");
    var fixedOutput = Path.Combine(fixedDir, "output.zip");
    Process fixedChild;
    using (await ExtractionDestinationGate.EnterAsync(fixedDir))
    {
        fixedChild = Start("fixed", fixedOutput, fixedDir);
        await WaitFor(fixedDir + ".started");
        await Task.Delay(400);
        Require(!File.Exists(fixedDir + ".acquired"), "Compression bypassed extraction directory gate.");
        File.WriteAllText(fixedOutput, "extraction rollback restored original");
    }
    await WaitFor(fixedDir + ".acquired");
    Release(fixedDir);
    await Finish(fixedChild);
    Require(File.ReadAllText(fixedOutput) == "compression committed", "Committed output was lost after rollback.");
    results.Add("PASS: compression waits for extraction rollback; later committed output survives");

    var reverseDir = MakeCase("extraction-waits");
    Process extractionChild;
    using (await ExtractionDestinationGate.EnterAsync(Path.Combine(reverseDir, "output.zip")))
    {
        extractionChild = Start("extraction", reverseDir, reverseDir);
        await WaitFor(reverseDir + ".started");
        await Task.Delay(400);
        Require(!File.Exists(reverseDir + ".acquired"), "Extraction bypassed compressed file gate.");
    }
    await WaitFor(reverseDir + ".acquired");
    Release(reverseDir);
    await Finish(extractionChild);
    results.Add("PASS: extraction directory waits for a compressed file beneath it");

    var siblingsDir = MakeCase("siblings");
    using (await ExtractionDestinationGate.EnterAsync(Path.Combine(siblingsDir, "first.zip")))
    {
        var child = Start("fixed", Path.Combine(siblingsDir, "second.zip"), siblingsDir);
        await WaitFor(siblingsDir + ".acquired");
        Release(siblingsDir);
        await Finish(child);
    }
    results.Add("PASS: different compressed files under the same directory acquire concurrently");

    var cancelDir = MakeCase("cancellation");
    var cancelOutput = Path.Combine(cancelDir, "output.zip");
    Process cancelChild;
    using (await ExtractionDestinationGate.EnterAsync(cancelDir))
    {
        cancelChild = Start("cancel", cancelOutput, cancelDir);
        await WaitFor(cancelDir + ".canceled");
        Require(!File.Exists(cancelOutput), "Canceled waiter modified output.");
    }
    using var reacquireDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    using (await ExtractionDestinationGate.EnterAsync(cancelDir, reacquireDeadline.Token)) { }
    using (await ExtractionDestinationGate.EnterAsync(cancelOutput, reacquireDeadline.Token)) { }
    Release(cancelDir);
    await Finish(cancelChild);
    results.Add("PASS: cancellation releases partial parent handles; directory and file reacquire");
    File.WriteAllText(Path.Combine(artifactRoot, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    foreach (var result in results) Console.WriteLine(result);
    Console.WriteLine("Artifacts: " + artifactRoot);
}
finally
{
    foreach (var child in children)
    {
        if (!child.HasExited) child.Kill(entireProcessTree: true);
        child.Dispose();
    }
}

string MakeCase(string name)
{
    var directory = Path.Combine(artifactRoot, name);
    Directory.CreateDirectory(directory);
    return directory;
}
Process Start(string mode, string path, string marker)
{
    var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
    foreach (var argument in new[] { Assembly.GetExecutingAssembly().Location, "worker", mode, path, marker })
        info.ArgumentList.Add(argument);
    var child = Process.Start(info) ?? throw new InvalidOperationException("Worker could not start.");
    children.Add(child);
    return child;
}
static async Task WaitFor(string marker)
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    while (!File.Exists(marker)) await Task.Delay(25, deadline.Token);
}
static void Release(string marker) => File.WriteAllText(marker + ".release", "release");
static async Task Finish(Process child)
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await child.WaitForExitAsync(deadline.Token);
    Require(child.ExitCode == 0, "Worker failed: " + child.ExitCode);
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
