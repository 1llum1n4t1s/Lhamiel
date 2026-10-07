using Lhamiel.Util;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

static class Probe
{
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "AppDataDirectory")]
    static extern ref string AppDataDirectory(Settings? owner);
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "SettingsFilePath")]
    static extern ref string SettingsFilePath(Settings? owner);
    static readonly List<object> Results = [];
    static string Root = "";
    static void Main(string[] args)
    {
        Root = Path.GetFullPath(args[0]); Directory.CreateDirectory(Root);
        var broken = args[1] == "--expect-broken";
        Isolate("large");
        var outputRoot = Path.Combine(Root, "outputs");
        var plans = Enumerable.Range(0, 33_334).Select(i => CompressionOutputRegistry.Plan.Create(Path.Combine(outputRoot, $"{i}.zip"))).ToArray();
        var output = CompressionOutputRegistry.Register(plans);
        var failures = new List<string>();
        Attempt("BeginScan", () => { using var scan = CompressionOutputRegistry.BeginScan(default); Assert(scan.Initial.Contains(OutputPathIdentity.GetCanonicalPath(plans[0].FinalPath)) && scan.Initial.Contains(OutputPathIdentity.GetCanonicalPath(plans[^1].FinalPath)), "large exclusions missing"); scan.Finish(); }, failures);
        Attempt("Dispose", output.Dispose, failures);
        Attempt("NextSmallRegister", () => { using var small = CompressionOutputRegistry.Register([CompressionOutputRegistry.Plan.Create(Path.Combine(outputRoot,"small.zip"))]); using var scan = CompressionOutputRegistry.BeginScan(default); scan.Finish(); }, failures);
        Assert(broken ? failures.Count == 3 : failures.Count == 0, "large registry expectation mismatch: " + string.Join("; ",failures));
        Results.Add(new { Case="large-state-roundtrip", Plans=plans.Length, Paths=plans.Length*3, ExpectedBroken=broken, Failures=failures, StateBytes=new FileInfo(StatePath()).Length });
        if(!broken && !args.Contains("--large-only"))
        {
            var leases=Directory.GetFiles(Path.GetDirectoryName(StatePath())!,"*.lease"); Assert(leases.Length==0,"leases leaked");
            Assert(new FileInfo(StatePath()).Length == 12,"empty registry not restored");
            Malformed("negative-count", -1, []);
            Malformed("count-exceeds-remaining", 2, [0]);
            Malformed("unfinished-string", 1, [5]);
            StageCleanup();
        }
        File.WriteAllText(Path.Combine(Root,"result.json"),JsonSerializer.Serialize(Results,new JsonSerializerOptions { WriteIndented=true }));
        Console.WriteLine(JsonSerializer.Serialize(Results));
    }
    static void Isolate(string name)
    {
        var path=Path.Combine(Root,name,"settings"); Directory.CreateDirectory(path);
        AppDataDirectory(null)=path; SettingsFilePath(null)=Path.Combine(path,"settings.json");
    }
    static string StatePath()=>Path.Combine(Settings.AppDataDirectory,"compression-outputs","state.bin");
    static void Attempt(string step, Action action, List<string> failures)
    {
        try { action(); } catch(InvalidDataException ex) { failures.Add(step+":"+ex.GetType().Name); }
    }
    static void Malformed(string name,int count,byte[] rest)
    {
        Isolate(name); Directory.CreateDirectory(Path.GetDirectoryName(StatePath())!);
        using(var writer=new BinaryWriter(new FileStream(StatePath(),FileMode.CreateNew))) { writer.Write(1); writer.Write(count); writer.Write(rest); }
        var expected=SHA256.HashData(File.ReadAllBytes(StatePath())); string? rejected=null;
        try { using var output=CompressionOutputRegistry.Register([CompressionOutputRegistry.Plan.Create(Path.Combine(Root,name,"output.zip"))]); }
        catch(Exception ex) when(ex is InvalidDataException or EndOfStreamException) { rejected=ex.GetType().Name; }
        Assert(rejected != null,"malformed state accepted"); Assert(expected.SequenceEqual(SHA256.HashData(File.ReadAllBytes(StatePath()))),"malformed state overwritten");
        Results.Add(new { Case=name, Rejected=rejected, OriginalPreserved=true });
    }
    static void StageCleanup()
    {
        Isolate("stage-cleanup");
        var plan=CompressionOutputRegistry.Plan.Create(Path.Combine(Root,"stage-cleanup","archive.zip"));
        var stem=Path.GetFileName(plan.StagingDirectory);
        Assert(stem.StartsWith("Lhamiel_Temp_") && Guid.TryParseExact(stem["Lhamiel_Temp_".Length..],"N",out _),"stage not recognized by TempCleanup naming");
        plan.Prepare(); File.WriteAllText(plan.TemporaryPath,"orphan-payload");
        var manifest=Path.Combine(Settings.AppDataDirectory,"tracked-temp-directories.txt");
        Assert(File.ReadAllLines(manifest).Contains(plan.StagingDirectory),"stage tracking missing");
        var cleaned=TempCleanup.CleanupTrackedDirectories(nowUtc: DateTime.UtcNow.AddHours(1));
        Assert(cleaned==1 && !Directory.Exists(plan.StagingDirectory),"tracked orphan stage not reclaimed");
        Assert(!File.Exists(manifest) || !File.ReadAllLines(manifest).Contains(plan.StagingDirectory),"tracking not cleared");
        Results.Add(new { Case="new-stage-tracked-cleanup", Name=stem, Cleaned=cleaned, DirectoryRemaining=false });
    }
    static void Assert(bool ok,string message) { if(!ok) throw new Exception(message); }
}
