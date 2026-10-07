using Lhamiel.Util;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

static class Probe
{
    [StructLayout(LayoutKind.Sequential)]
    struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [DllImport("kernel32.dll")]
    static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    static extern bool CreateSymbolicLinkW(string link, string target, uint flags);
    static string Root = "";
    static readonly List<object> Results = [];
    static string StatePath => Path.Combine(Settings.AppDataDirectory, "compression-outputs", "state.bin");
    static bool Canonical;
    static string Mode = "";
    static void Main(string[] args)
    {
        Root = Path.GetFullPath(args[0]); Directory.CreateDirectory(Root);
        Mode = args[1]; Canonical = Mode != "before";
        if (args.Length > 2 && args[2] == "crash-child")
        {
            Isolate("crash");
            var plan = FixedPlan("crash", 1);
            var lease = CompressionOutputRegistry.Register([plan]);
            Directory.CreateDirectory(plan.StagingDirectory);
            File.WriteAllText(Path.Combine(plan.StagingDirectory, "residue"), "crash");
            Console.WriteLine("registered"); Console.Out.Flush();
            Environment.Exit(17); GC.KeepAlive(lease); return;
        }
        if (args.Length > 2 && args[2] == "read-residue")
        {
            Isolate("v1-residue"); var residue = FixedPlan("v1-residue", 1);
            using var scan = CompressionOutputRegistry.BeginScan(default);
            Assert(scan.Initial.Contains(Key(residue.StagingDirectory)) && !scan.Initial.Contains(Key(residue.FinalPath)), "new process v2 residue not canonical");
            scan.Finish(); Console.WriteLine("residue-read"); return;
        }
        if (Mode == "slotguard-after") SlotGuard();
        else if (Mode.StartsWith("followup-", StringComparison.Ordinal))
        { foreach (var count in new[] { 100, 1000 }) History(count); ExistingDirectory(); Legacy(); }
        else
        { foreach (var count in new[] { 100, 1000 }) Scale(count); if (Canonical) { Contracts(); Legacy(); Corrupt(); } }
        var dll = Path.Combine(AppContext.BaseDirectory, "Lhamiel.dll");
        var report = new { Runtime = RuntimeInformation.FrameworkDescription, DLLSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))),
            Canonical, Roots = Root, Settings = "isolated Settings.AppDataDirectory, settings.json unset", Results,
            Metric = "Logical metadata bytes summed from actual file length before/after every operation; payload bytes written once. Process IO counters are OS logical transfer bytes, NOT physical disk IO. Elapsed time includes lease/gate/file work. Input logical fixture SHA excludes isolation root." };
        File.WriteAllText(Path.Combine(Root, "result-" + Mode + ".json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report));
    }
    static string Key(string path) => Canonical ? OutputPathIdentity.GetCanonicalPath(path) : Path.GetFullPath(path);
    static CompressionOutputRegistry.Plan FixedPlan(string name, int i)
    {
        var folder = Path.Combine(Root, "fixture", name);
        return new(Path.Combine(folder, $"archive-{i:D5}.zip"), Path.Combine(folder, $"Lhamiel_Temp_{i:D32}"), Path.Combine(folder, $"archive-{i:D5}.zip.lhamiel-bak-{i:D32}"));
    }
    static void Isolate(string name)
    {
        var directory = Path.Combine(Root, Mode, name, "settings"); Directory.CreateDirectory(directory);
        Settings.AppDataDirectory = directory;
    }
    static long Size() => File.Exists(StatePath) ? new FileInfo(StatePath).Length : 0;
    static HashSet<string>[] Sets(CompressionOutputRegistry.Exclusions exclusions) =>
        (HashSet<string>[])typeof(CompressionOutputRegistry.Exclusions).GetField("_sets", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(exclusions)!;
    static void History(int count)
    {
        Isolate("history-" + count);
        var large = Enumerable.Range(0, count).Select(i => FixedPlan("large-" + count, i)).ToArray();
        using var batch = CompressionOutputRegistry.Register(large);
        using var scan = CompressionOutputRegistry.BeginScan(default);
        var shared = Sets(scan.Initial).Single(s => s.Contains(Key(large[0].FinalPath)));
        var setup = Stopwatch.StartNew();
        var shortPlans = Enumerable.Range(0, count).Select(i => FixedPlan("short-" + count, i)).ToArray();
        foreach (var plan in shortPlans) { using var output = CompressionOutputRegistry.Register([plan]); }
        var exclusions = scan.Finish(); setup.Stop();
        Assert(exclusions.Contains(Key(shortPlans[0].FinalPath)) && exclusions.Contains(Key(shortPlans[^1].FinalPath)), "history completed output missing");
        var sets = Sets(exclusions);
        Assert(sets.Any(s => ReferenceEquals(shared, s)), "large batch set was copied");
        var isBefore = Mode == "followup-before";
        Assert(isBefore ? sets.Length >= count : sets.Length == 2, "history membership set count mismatch");
        const int fileCount = 10_000;
        var resolver = OutputPathIdentity.CreateResolver([Path.Combine(Root, "fixture")]);
        var files = Enumerable.Range(0, fileCount).Select(i => resolver.GetKey(Path.Combine(Root, "fixture", "plain", $"input-{i:D5}.txt"))).ToArray();
        var timer = Stopwatch.StartNew();
        foreach (var file in files) Assert(!exclusions.Contains(file), "ordinary file excluded");
        timer.Stop();
        var fixture = string.Join('\n', large.Concat(shortPlans).SelectMany(p => p.Paths).Select(p => Path.GetRelativePath(Root, p)));
        Results.Add(new { Case = "long-scanner-short-request-history-contains", Plans = count, FileContains = fileCount,
            FixtureSha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fixture))),
            MembershipSets = sets.Length, LargeBatchSetReused = true, BuildAndFinishMs = setup.Elapsed.TotalMilliseconds,
            ContainsMs = timer.Elapsed.TotalMilliseconds, StateBytesAfterFinish = Size(), Canonical = true });
    }
    static void ExistingDirectory()
    {
        Isolate("existing-directory"); var plan = FixedPlan("existing-directory", 1);
        Directory.CreateDirectory(plan.FinalPath); var failed = false;
        try
        {
            using var output = CompressionOutputRegistry.Register([plan]); using var scan = CompressionOutputRegistry.BeginScan(default);
            Assert(scan.Initial.Contains(Key(plan.FinalPath)) && scan.Initial.Contains(Key(plan.StagingDirectory)) && scan.Initial.Contains(Key(plan.BackupPath)), "existing-directory sibling exclusion missing");
            scan.Finish();
        }
        catch (IOException) { failed = true; }
        Assert(failed == (Mode == "followup-before"), "existing-directory expectation mismatch");
        Results.Add(new { Case = "existing-final-directory-with-sibling-stage-and-backup", RejectedBefore = failed, DirectoryPresent = Directory.Exists(plan.FinalPath) });
    }
    static void SlotGuard()
    {
        Isolate("slot-guard"); var directory = Path.Combine(Root, "fixture"); Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "target"); Directory.CreateDirectory(target);
        var leafLink = Path.Combine(directory, "leaf.zip"); CreateLink("Junction", leafLink, target);
        string? rejected = null;
        try { CompressionOutputRegistry.Plan.Create(leafLink); }
        catch (IOException ex) { rejected = ex.Message; }
        Assert(rejected != null, "leaf directory junction accepted");
        var normal = Path.Combine(directory, "normal.zip"); Directory.CreateDirectory(normal);
        var normalPlan = CompressionOutputRegistry.Plan.Create(normal);
        using (var output = CompressionOutputRegistry.Register([normalPlan]))
        using (var scan = CompressionOutputRegistry.BeginScan(default))
        { Assert(scan.Initial.Contains(Key(normal)) && scan.Initial.Contains(Key(normalPlan.StagingDirectory)), "normal directory rejected or siblings missing"); scan.Finish(); }
        var parent = Path.Combine(directory, "parent-link"); CreateLink("Junction", parent, target);
        var parentPlan = CompressionOutputRegistry.Plan.Create(Path.Combine(parent, "archive.zip"));
        using (var output = CompressionOutputRegistry.Register([parentPlan]))
        using (var scan = CompressionOutputRegistry.BeginScan(default))
        { Assert(scan.Initial.Contains(Key(parentPlan.FinalPath)), "parent junction rejected"); scan.Finish(); }
        var file = Path.Combine(target, "file.dat"); File.WriteAllText(file, "slot fixture");
        foreach (var kind in new[] { "HardLink", "SymbolicLink" })
        {
            var leaf = Path.Combine(directory, kind + ".zip"); CreateLink(kind, leaf, file);
            var plan = CompressionOutputRegistry.Plan.Create(leaf);
            Assert(plan.FinalPath == Path.GetFullPath(leaf), "file link slot changed");
        }
        Results.Add(new { Case = "leaf-directory-junction-rejected-normal-directory-parent-junction-file-links-allowed", Passed = true,
            Message = rejected, DirectoryTargetPresent = Directory.Exists(target), JunctionsRetained = new[] { leafLink, parent } });
    }
    static void CreateLink(string kind, string path, string target)
    {
        if (kind == "SymbolicLink")
        {
            if (CreateSymbolicLinkW(path, target, 2)) return;
            var error = Marshal.GetLastWin32Error();
            Assert(error == 1314, "file symlink fixture creation failed: " + error);
            var elevated = new ProcessStartInfo("pwsh.exe") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            elevated.ArgumentList.Add("-NoProfile"); elevated.ArgumentList.Add("-NonInteractive"); elevated.ArgumentList.Add("-Command");
            elevated.ArgumentList.Add("New-Item -ItemType SymbolicLink -Path '" + path.Replace("'", "''") + "' -Target '" + target.Replace("'", "''") + "' -ErrorAction Stop | Out-Null");
            using var admin = Process.Start(elevated)!;
            Assert(admin.WaitForExit(30_000) && admin.ExitCode == 0 && File.Exists(path), "elevated file symlink fixture failed");
            return;
        }
        var start = new ProcessStartInfo("pwsh.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("New-Item -ItemType $env:LHAMIEL_REGISTRY_LINK_TYPE -Path $env:LHAMIEL_REGISTRY_LINK_PATH -Target $env:LHAMIEL_REGISTRY_LINK_TARGET -ErrorAction Stop | Out-Null");
        start.Environment["LHAMIEL_REGISTRY_LINK_TYPE"] = kind; start.Environment["LHAMIEL_REGISTRY_LINK_PATH"] = path; start.Environment["LHAMIEL_REGISTRY_LINK_TARGET"] = target;
        using var process = Process.Start(start)!; process.WaitForExit();
        Assert(process.ExitCode == 0, "link fixture creation failed: " + process.StandardError.ReadToEnd());
    }
    static void Scale(int count)
    {
        Isolate("scale-" + count);
        // 前の失敗時 lease を通常の回収経路で解消し、計測開始を同じ空状態にする。
        using (var warm = CompressionOutputRegistry.BeginScan(default)) { warm.Finish(); }
        var plans = Enumerable.Range(0, count).Select(i => FixedPlan("scale-" + count, i)).ToArray();
        var fixture = string.Join('\n', plans.SelectMany(p => p.Paths).Select(p => Path.GetRelativePath(Root, p)));
        var fixtureHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fixture)));
        GetProcessIoCounters(Process.GetCurrentProcess().Handle, out var ioBefore);
        var timer = Stopwatch.StartNew(); long reads = 0, writes = 0; long operations = 0;
        void Measure(Action action) { reads += Size(); action(); writes += Size(); operations++; }
        IDisposable? batch = null; Measure(() => batch = CompressionOutputRegistry.Register(plans));
        var batchStateBytes = Size();
        for (var i = 0; i < count; i++)
        {
            IDisposable? item = null; CompressionOutputRegistry.ScanLease? scan = null;
            Measure(() => item = CompressionOutputRegistry.Register([plans[i]]));
            Measure(() => scan = CompressionOutputRegistry.BeginScan(default));
            Assert(scan!.Initial.Contains(Key(plans[0].FinalPath)) && scan.Initial.Contains(Key(plans[^1].FinalPath)), "batch exclusions missing");
            Measure(() => Assert(scan.Finish().Contains(Key(plans[i].FinalPath)), "finish exclusion missing"));
            Measure(() => item!.Dispose());
        }
        Measure(() => batch!.Dispose()); timer.Stop();
        GetProcessIoCounters(Process.GetCurrentProcess().Handle, out var ioAfter);
        // payload は metadata と異なり登録ごとに一度だけ出力する。fixtureの全N pathsのbatchと各itemが同量。
        long payloadBytes = 0;
        if (Canonical)
            foreach (var payloadPlans in new[] { plans }.Concat(plans.Select(p => new[] { p })))
            {
                using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
                var paths = payloadPlans.SelectMany(p => p.Paths).Select(Key).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var cleanup = payloadPlans.SelectMany(p => new[] { p.StagingDirectory, p.BackupPath }).Select(Key).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                writer.Write(paths.Length); foreach (var path in paths) writer.Write(path);
                writer.Write(cleanup.Length); foreach (var path in cleanup) writer.Write(path);
                payloadBytes += memory.Length;
            }
        Assert(Directory.GetFiles(Path.GetDirectoryName(StatePath)!, "*.lease").Length == 0, "scale lease remaining");
        Assert(Directory.GetFiles(Path.GetDirectoryName(StatePath)!, "*.payload").Length == 0, "scale payload remaining");
        Results.Add(new { Case = "batch-peritem-register-scan-finish-dispose", Plans = count, Paths = 3 * count, FixtureSha256 = fixtureHash,
            Operations = operations, BatchStateBytes = batchStateBytes, LogicalMetadataReadBytes = reads, LogicalMetadataWriteBytes = writes,
            LogicalPayloadWriteBytes = payloadBytes, LogicalReadWriteBytes = reads + writes + payloadBytes,
            ElapsedMs = timer.Elapsed.TotalMilliseconds, ProcessIoReadBytes = ioAfter.ReadBytes - ioBefore.ReadBytes,
            ProcessIoWriteBytes = ioAfter.WriteBytes - ioBefore.WriteBytes, FinalStateBytes = Size() });
    }
    static void Contracts()
    {
        Isolate("contracts"); var a = FixedPlan("contracts", 1); var b = FixedPlan("contracts", 2);
        var output = CompressionOutputRegistry.Register([a]);
        using var scan = CompressionOutputRegistry.BeginScan(default);
        Assert(scan.Initial.Contains(Key(a.FinalPath)), "active output not excluded");
        using (var later = CompressionOutputRegistry.Register([b])) { }
        output.Dispose();
        var final = scan.Finish();
        Assert(final.Contains(Key(a.FinalPath)) && final.Contains(Key(b.FinalPath)), "completed during scan lost");
        using (var fresh = CompressionOutputRegistry.BeginScan(default))
        { Assert(!fresh.Initial.Contains(Key(a.FinalPath)) && !fresh.Initial.Contains(Key(b.FinalPath)), "completed final excluded from fresh scan"); fresh.Finish(); }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { using var unexpected = CompressionOutputRegistry.Register([a], cancelled.Token); throw new Exception("cancel accepted"); }
        catch (OperationCanceledException) { }
        var directory = Path.GetDirectoryName(StatePath)!;
        Assert(Directory.GetFiles(directory, "*.lease").Length == 0, "contract lease leaked");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true };
        start.ArgumentList.Add(Root); start.ArgumentList.Add("after"); start.ArgumentList.Add("crash-child");
        using (var child = Process.Start(start)!) { child.WaitForExit(); Assert(child.ExitCode == 17 && child.StandardOutput.ReadToEnd().Contains("registered"), "crash child failed"); }
        Isolate("crash"); var crashPlan = FixedPlan("crash", 1);
        using (var fresh = CompressionOutputRegistry.BeginScan(default))
        { Assert(fresh.Initial.Contains(Key(crashPlan.StagingDirectory)) && !fresh.Initial.Contains(Key(crashPlan.FinalPath)), "crash residue contract failed"); fresh.Finish(); }
        Results.Add(new { Case = "normal-later-register-cancel-child-crash-residue", Passed = true, Residue = crashPlan.StagingDirectory });
    }
    static void Legacy()
    {
        Isolate("v1-active"); var plan = FixedPlan("v1-active", 1); var id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        using (var lease = new FileStream(Path.Combine(Path.GetDirectoryName(StatePath)!, id + ".lease"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
        {
            WriteLegacy(id, plan);
            using var addition = CompressionOutputRegistry.Register([FixedPlan("v1-active", 2)]);
            Assert(LegacyHasPath(FixedPlan("v1-active", 2).FinalPath), "old v1 reader cannot see new output");
            using var scan = CompressionOutputRegistry.BeginScan(default);
            Assert(scan.Initial.Contains(Key(plan.FinalPath)), "legacy active exclusion missing");
            scan.Finish(); Assert(BitConverter.ToInt32(File.ReadAllBytes(StatePath)) == 1, "active legacy migrated");
        }
        using (var fresh = CompressionOutputRegistry.BeginScan(default)) { fresh.Finish(); }
        Assert(BitConverter.ToInt32(File.ReadAllBytes(StatePath)) == 2, "legacy empty not migrated");
        Isolate("v1-residue"); plan = FixedPlan("v1-residue", 1); id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(plan.StagingDirectory); WriteLegacy(id, plan);
        // v1 正本がある間の中途終了を再現し、partial .payload を再利用しないことを確認する。
        var partial = Path.Combine(Path.GetDirectoryName(StatePath)!, id + ".payload");
        File.WriteAllBytes(partial, [1, 2, 3]); File.WriteAllBytes(partial + ".next", [4, 5]);
        using (var fresh = CompressionOutputRegistry.BeginScan(default))
        { Assert(fresh.Initial.Contains(Key(plan.StagingDirectory)) && !fresh.Initial.Contains(Key(plan.FinalPath)), "legacy residue missing"); fresh.Finish(); }
        Assert(BitConverter.ToInt32(File.ReadAllBytes(StatePath)) == 2, "residue not migrated");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true };
        start.ArgumentList.Add(Root); start.ArgumentList.Add(Mode); start.ArgumentList.Add("read-residue");
        using (var child = Process.Start(start)!) { child.WaitForExit(); Assert(child.ExitCode == 0 && child.StandardOutput.ReadToEnd().Contains("residue-read"), "new process residue failed"); }
        Results.Add(new { Case = "v1-active-preserved-empty-residue-and-partial-payload-migrated-cross-process", Passed = true });
    }
    static void WriteLegacy(string id, CompressionOutputRegistry.Plan plan)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        using var writer = new BinaryWriter(File.Create(StatePath));
        writer.Write(1); writer.Write(0); writer.Write(1); writer.Write(id); writer.Write(true);
        var paths = plan.Paths.ToArray(); writer.Write(paths.Length); foreach (var path in paths) writer.Write(path);
        writer.Write(2); writer.Write(plan.StagingDirectory); writer.Write(plan.BackupPath); writer.Write(0);
    }
    static bool LegacyHasPath(string expected)
    {
        using var reader = new BinaryReader(File.OpenRead(StatePath)); Assert(reader.ReadInt32() == 1, "not legacy");
        var scanners = reader.ReadInt32(); for (var i = 0; i < scanners; i++) reader.ReadString();
        var outputs = reader.ReadInt32(); var found = false;
        for (var i = 0; i < outputs; i++)
        {
            reader.ReadString(); reader.ReadBoolean(); var count = reader.ReadInt32();
            for (var j = 0; j < count; j++) found |= string.Equals(expected, reader.ReadString(), StringComparison.OrdinalIgnoreCase);
            count = reader.ReadInt32(); for (var j = 0; j < count; j++) reader.ReadString();
            count = reader.ReadInt32(); for (var j = 0; j < count; j++) reader.ReadString();
        }
        return found;
    }
    static void Corrupt()
    {
        foreach (var kind in new[] { "unknown-version", "negative-count", "truncated", "trailing", "missing-payload" })
        {
            Isolate("corrupt-" + kind); Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            using (var writer = new BinaryWriter(File.Create(StatePath)))
            {
                writer.Write(kind == "unknown-version" ? 99 : 2);
                if (kind != "truncated")
                {
                    writer.Write(kind == "negative-count" ? -1 : 0);
                    writer.Write(kind == "missing-payload" ? 1 : 0);
                    if (kind == "trailing") writer.Write(123);
                    if (kind == "missing-payload") { writer.Write(Guid.NewGuid().ToString("N")); writer.Write(true); writer.Write(0); }
                }
            }
            var before = SHA256.HashData(File.ReadAllBytes(StatePath)); var rejected = false;
            try { using var lease = CompressionOutputRegistry.Register([FixedPlan(kind, 1)]); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { rejected = true; }
            Assert(rejected && before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(StatePath))), "bad state accepted or overwritten: " + kind);
            Results.Add(new { Case = kind, Rejected = true, OriginalPreserved = true });
        }
    }
    static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
