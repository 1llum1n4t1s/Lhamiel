using Avalonia;
using Avalonia.Controls;
using Lhamiel.Models;
using Lhamiel.Util;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

static class Probe
{
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "AppDataDirectory")]
    static extern ref string AppDataDirectory(Settings? owner);
    [UnsafeAccessor(UnsafeAccessorKind.StaticField, Name = "SettingsFilePath")]
    static extern ref string SettingsFilePath(Settings? owner);
    static string Root = "";
    static readonly List<object> Results = [];
    static readonly List<string> Errors = [];
    static async Task Main(string[] args)
    {
        Root = Path.GetFullPath(args[0]); Directory.CreateDirectory(Root);
        var settings = Path.Combine(Root, "settings"); Directory.CreateDirectory(settings);
        AppDataDirectory(null) = settings; SettingsFilePath(null) = Path.Combine(settings, "settings.json");
        // null の進捗窓でも共通進捗配送は Dispatcher を使うため、実バックエンドを初期化する。
        // fixture は窓を作らず、await の継続を未起動の UI ループへ捕捉させない。
        AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
        SynchronizationContext.SetSynchronizationContext(null);
        ArchiveProcessor.UiDispatcherImpl = new ImmediateDispatcher();
        ArchiveProcessor.MessageServiceImpl = new Messages();
        ArchiveProcessor.ConflictDialogImpl = new Overwrite();
        if(args[1] == "--child")
        {
            var plan = CompressionOutputRegistry.Plan.Create(args[2]);
            using var registration = CompressionOutputRegistry.Register([plan]);
            plan.Prepare(); File.WriteAllText(plan.TemporaryPath, "unfinished-private-output");
            CreateZip(plan.FinalPath);
            if(args.Length > 3) Environment.Exit(17);
            plan.Cleanup();
            return;
        }
        await Overlap("completed", false);
        await Overlap("crashed", true);
        await PlannedPaths();
        await EntryPoints();
        await Formats();
        Assert(Errors.Count == 0, "処理中エラー: " + string.Join("; ", Errors));
        var leases = Directory.GetFiles(Path.Combine(settings, "compression-outputs"), "*.lease");
        Assert(leases.Length == 0, "終了後のregistry leaseが残存");
        Results.Add(new { RegistryLeases = leases.Length, Errors });
        File.WriteAllText(Path.Combine(Root, "result.json"), JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(Results));
    }
    static void Assert(bool ok, string error) { if(!ok) throw new Exception(error); }
    static string Fixture(string name)
    {
        var folder = Path.Combine(Root, name, "photos"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "a.txt"), "stable-original-input");
        CreateZip(Path.Combine(folder, "previous.zip")); return folder;
    }
    static void CreateZip(string path)
    {
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        using var w = new StreamWriter(z.CreateEntry("old.txt").Open()); w.Write("completed-data");
    }
    static Task<List<(string fullPath, string relativePath)>> Scan(List<string> paths, IProgress<ProgressInfo>? progress = null) =>
        ArchiveCompressor.ScanSourceFiles(paths, GitignoreMatcher.Compile([]), dirModeOverride: DirectoryStructureMode.IncludeRoot, progress: progress);
    static async Task Overlap(string name, bool crash)
    {
        var folder = Fixture(name); var live = Path.Combine(folder, "a.zip"); var fired = false;
        var progress = new InlineProgress(_ => {
            if(fired) return; fired = true;
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            foreach(var arg in new[] { Root, "--child", live }) start.ArgumentList.Add(arg);
            if(crash) start.ArgumentList.Add("--crash");
            using var process = Process.Start(start)!;
            if(!process.WaitForExit(15000)) { process.Kill(true); throw new Exception("child timeout"); }
            Assert(process.ExitCode == (crash ? 17 : 0), "child exit mismatch");
        });
        var files = await Scan([folder], progress);
        Assert(!files.Any(f => f.fullPath == live), "期間内に終了したworker出力が混入");
        Assert(files.Any(f => f.fullPath.EndsWith("previous.zip")), "以前の完成archiveが脱落");
        var output = Path.Combine(Root, name, "result.zip");
        await ArchiveCompressor.CompressFilesAsync([folder], output, ArchiveCompressor.ParseFormat("zip"), resolvedFiles: files);
        using(var zip = ZipFile.OpenRead(output))
        {
            Assert(!zip.Entries.Any(e => e.FullName.EndsWith("a.zip")), "ZIP内容へ別出力が混入");
            using var data = zip.GetEntry("photos/a.txt")!.Open(); using var memory = new MemoryStream(); data.CopyTo(memory);
            Assert(SHA256.HashData(memory.ToArray()).SequenceEqual(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder,"a.txt")))), "ZIP内容SHA不一致");
        }
        var later = await Scan([folder]); Assert(later.Any(f => f.fullPath == live), "後発scanが完成archiveを除外");
        Assert(!later.Any(f => f.fullPath.Contains(".lhamiel-tmp-")), "crash残留stagingが後発scanへ混入");
        var explicitInput = await Scan([live]); Assert(explicitInput.Single().fullPath == live, "明示archive入力が脱落");
        if(crash) Assert(TempCleanup.CleanupTrackedDirectories(nowUtc: DateTime.UtcNow.AddHours(1)) > 0, "crash stagingの追跡清掃失敗");
        Results.Add(new { Case = name, Mixed = false, LaterCompletedIncluded = true, ExplicitIncluded = true, InputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder,"a.txt")))) });
    }
    static async Task PlannedPaths()
    {
        var folder = Fixture("plans"); var plan = CompressionOutputRegistry.Plan.Create(Path.Combine(folder,"a.zip"));
        using var registration = CompressionOutputRegistry.Register([plan]);
        CreateZip(plan.FinalPath); plan.Prepare(); File.WriteAllText(plan.TemporaryPath,"in-progress");
        Directory.CreateDirectory(plan.BackupPath); File.WriteAllText(Path.Combine(plan.BackupPath,"nested.txt"),"backup");
        var files = await Scan([folder]);
        Assert(!files.Any(f => plan.Paths.Any(p => f.fullPath.StartsWith(p,StringComparison.OrdinalIgnoreCase))), "planned final/staging/backup混入");
        Assert((await Scan([plan.FinalPath])).Count == 1, "active final明示選択を除外");
        plan.Cleanup(); Results.Add(new { Case = "planned-final-staging-backup", Mixed = false });
    }
    static async Task EntryPoints()
    {
        var single = Fixture("single"); var fixedOutput = Path.Combine(single,"outputs");
        Assert(await ArchiveProcessor.CompressItemAsync(single, fixedOutput,false,"zip",null,closeWindowOnCompletion:false),"single failed");
        CheckZip(Path.Combine(fixedOutput,"photos.zip"),"single");
        // 同じ出力への上書きでも以前のfinal/staging/backupを入力へ取り込まない。
        Assert(await ArchiveProcessor.CompressItemAsync(single,fixedOutput,false,"zip",null,closeWindowOnCompletion:false),"overwrite failed");
        CheckZip(Path.Combine(fixedOutput,"photos.zip"),"overwrite");
        var merged = Fixture("merged"); var mergedOutput = Path.Combine(merged,"outputs");
        Assert(await ArchiveProcessor.CompressMergedAsync([merged,Path.Combine(merged,"a.txt")],mergedOutput,false,"zip",null,closeWindowOnCompletion:false),"merged failed");
        CheckZip(Path.Combine(mergedOutput,"photos.zip"),"merged");
        var batch = Fixture("batch"); bool? all = null;
        Assert(await ArchiveProcessor.CompressItemsAsync([batch,Path.Combine(batch,"a.txt")],"",true,"zip",null!,closeWindowOnCompletion:false,reportAllSucceeded:value=>all=value),"batch failed");
        Assert(all == true,"batch callback failed"); CheckZip(Path.Combine(Path.GetDirectoryName(batch)!,"photos.zip"),"batch");
    }
    static void CheckZip(string path,string label)
    {
        using var zip = ZipFile.OpenRead(path); var entries = zip.Entries.Select(e=>e.FullName).ToArray();
        Assert(!entries.Any(e=>e.Contains(".lhamiel-") || e.EndsWith("a.zip") || e.EndsWith("photos.zip")),label+" output mixed");
        Assert(entries.Any(e=>e.EndsWith("previous.zip")),label+" completed archive missing");
        Results.Add(new { Case=label, Entries=entries });
    }
    static async Task Formats()
    {
        foreach(var format in new[] { "tar", "lzh" })
        {
            var folder = Fixture(format); var output = Path.Combine(folder,"output."+format);
            var plan = CompressionOutputRegistry.Plan.Create(output);
            using var registration = CompressionOutputRegistry.Register([plan]); plan.Prepare();
            List<(string fullPath,string relativePath)>? concurrent = null; var fired=false;
            var progress = new InlineProgress(_=> { if(fired || !Directory.EnumerateFileSystemEntries(plan.StagingDirectory).Any()) return; fired=true; concurrent=Scan([folder]).GetAwaiter().GetResult(); });
            try
            {
                var files = await Scan([folder]);
                await ArchiveCompressor.CompressFilesCoreAsync([folder],plan.TemporaryPath,ArchiveCompressor.ParseFormat(format),progress,resolvedFiles:files);
                File.Move(plan.TemporaryPath,output);
                concurrent ??= await Scan([folder]);
                Assert(!concurrent.Any(f=>f.fullPath.StartsWith(plan.StagingDirectory,StringComparison.OrdinalIgnoreCase) || f.fullPath==output),"native sidecar混入");
                string[] entries;
                if(format=="tar") { using var stream=File.OpenRead(output); using var reader=new TarReader(stream); var names=new List<string>(); while(reader.GetNextEntry() is {} entry) names.Add(entry.Name); entries=names.ToArray(); }
                else entries=LzhArchiveBackendProvider.Current.List(output).Select(e=>e.Name).ToArray();
                Assert(!entries.Any(e=>e.Contains(".lhamiel-")),"native output content混入");
                Results.Add(new { Case=format, SidecarScan=fired, Entries=entries });
            }
            finally { plan.Cleanup(); }
        }
    }
    sealed class InlineProgress(Action<ProgressInfo> action) : IProgress<ProgressInfo> { public void Report(ProgressInfo value) => action(value); }
    sealed class ImmediateDispatcher : IUiDispatcher { public void Post(Action action)=>action(); public Task InvokeAsync(Func<Task> callback)=>callback(); public Task<T> InvokeAsync<T>(Func<Task<T>> callback)=>callback(); }
    sealed class Messages : IMessageService { public Task ShowError(string message,string? title=null) { lock(Errors) Errors.Add(message); return Task.CompletedTask; } }
    sealed class Overwrite : IConflictDialogService
    {
        public Task<bool> CanOverwriteFromBackgroundAsync(string source,string destination,Window? owner)=>Task.FromResult(true);
        public Task<(FileConflictResult result,List<(string fullPath,string relativePath)> selectedFiles)> ShowFromBackgroundAsync(List<FileConflictGroup> groups,Window? owner,bool isTwoPane=true)=>throw new Exception("unexpected conflict dialog");
    }
}
