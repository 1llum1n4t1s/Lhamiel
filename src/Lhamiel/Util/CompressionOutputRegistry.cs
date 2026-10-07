namespace Lhamiel.Util;

/// <summary>並列 worker の出力を、重なる走査が終了するまで入力から隔離する。</summary>
internal static class CompressionOutputRegistry
{
    internal sealed record Plan(string FinalPath, string StagingDirectory, string BackupPath)
    {
        internal static Plan Create(string path)
        {
            var fullPath = Path.GetFullPath(path);
            try
            {
                var attributes = File.GetAttributes(fullPath);
                // 出力 leaf の directory link は置換前後で実体キーが変わるため拒否する。
                // 利用者が選んだ親の link と、通常の directory/file leaf は維持する。
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) ==
                    (FileAttributes.Directory | FileAttributes.ReparsePoint))
                    throw new IOException(App.Text("Error.DirectoryLinkCompressionOutput", fullPath));
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 2 or 3) { }
            return new(fullPath, Path.Combine(Path.GetDirectoryName(fullPath)!, "Lhamiel_Temp_" + Guid.NewGuid().ToString("N")),
                fullPath + ".lhamiel-bak-" + Guid.NewGuid().ToString("N"));
        }
        internal string TemporaryPath => Path.Combine(StagingDirectory, Path.GetFileName(FinalPath));
        internal IEnumerable<string> Paths => [FinalPath, StagingDirectory, BackupPath];
        internal void Prepare()
        {
            if (!TempCleanup.RegisterTrackedDirectory(StagingDirectory))
                throw new IOException("圧縮用一時ディレクトリを追跡できませんでした。");
            Directory.CreateDirectory(StagingDirectory);
        }
        internal void Cleanup()
        {
            try { if (Directory.Exists(StagingDirectory)) Directory.Delete(StagingDirectory, true); }
            catch (Exception ex) { Logger.Log($"圧縮用一時ディレクトリの削除に失敗: {StagingDirectory} ({ex.Message})", LogLevel.Warning); }
            if (!Directory.Exists(StagingDirectory)) TempCleanup.UnregisterTrackedDirectory(StagingDirectory);
        }
    }

    private sealed class Payload
    {
        internal readonly string[] Paths;
        internal readonly string[] CleanupPaths;
        internal readonly HashSet<string> PathKeys;
        internal readonly HashSet<string> CleanupKeys;
        internal bool Persisted;
        internal Payload(string[] paths, string[] cleanupPaths, bool legacy = false)
        {
            Paths = paths;
            CleanupPaths = cleanupPaths;
            // v1 の保存表記は旧 scanner のため保持し、照合キーだけ共通化する。
            PathKeys = new(legacy ? paths.Select(OutputPathIdentity.GetCanonicalPath) : paths, StringComparer.OrdinalIgnoreCase);
            CleanupKeys = new(legacy ? cleanupPaths.Select(OutputPathIdentity.GetCanonicalPath) : cleanupPaths, StringComparer.OrdinalIgnoreCase);
        }
        internal bool HasResidue => CleanupPaths.Any(p => File.Exists(p) || Directory.Exists(p));
    }
    private sealed class Output(string id, Payload payload, HashSet<string> scanners)
    {
        internal string Id = id;
        internal Payload Payload = payload;
        internal HashSet<string> Scanners = scanners;
        internal bool Active = true;
        internal bool HasResidue => Payload.HasResidue;
    }
    private sealed class State
    {
        internal int Version = 2;
        internal HashSet<string> Scanners = [];
        internal List<Output> Outputs = [];
    }
    // gate 内でだけ触る。不変 payload と照合集合を各走査・登録で再構築しない。
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Payload> PayloadCache = new(StringComparer.OrdinalIgnoreCase);
    private static string RegistryPath => Path.Combine(Settings.AppDataDirectory, "compression-outputs", "state.bin");
    private static string LeasePath(string registry, string id) => Path.Combine(Path.GetDirectoryName(registry)!, id + ".lease");
    private static string PayloadPath(string registry, string id) => Path.Combine(Path.GetDirectoryName(registry)!, id + ".payload");

    internal static IDisposable Register(IEnumerable<Plan> plans, CancellationToken token = default)
    {
        var planned = plans.ToArray();
        var registry = RegistryPath;
        var id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(registry)!);
        using var gate = CrossProcessResourceGate.EnterMetadata(registry, token);
        var state = Read(registry);
        var stream = OpenLease(registry, id);
        try
        {
            var resolver = OutputPathIdentity.CreateResolver(planned.Select(p => Path.GetDirectoryName(p.FinalPath)!));
            Func<string, string> key = state.Version == 1 ? Path.GetFullPath : resolver.GetKey;
            var payload = new Payload(planned.SelectMany(p => p.Paths).Select(key)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), planned.SelectMany(p => new[] { p.StagingDirectory, p.BackupPath })
                .Select(key).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), legacy: state.Version == 1);
            if (state.Version == 2)
            {
                SavePayload(PayloadPath(registry, id), payload, overwrite: false);
                PayloadCache[PayloadPath(registry, id)] = payload;
            }
            state.Outputs.Add(new Output(id, payload, [.. state.Scanners]));
            Write(registry, state);
            return new OutputLease(registry, id, stream);
        }
        catch { stream.Dispose(); File.Delete(LeasePath(registry, id)); throw; }
    }

    internal static ScanLease BeginScan(CancellationToken token)
    {
        var registry = RegistryPath;
        var id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(registry)!);
        using var gate = CrossProcessResourceGate.EnterMetadata(registry, token);
        var state = Read(registry);
        var stream = OpenLease(registry, id);
        try
        {
            state.Scanners.Add(id);
            foreach (var output in state.Outputs.Where(o => o.Active)) output.Scanners.Add(id);
            var exclusions = GetExclusions(state, id, registry);
            Write(registry, state);
            return new ScanLease(registry, id, stream, exclusions);
        }
        catch { stream.Dispose(); File.Delete(LeasePath(registry, id)); throw; }
    }

    internal sealed class Exclusions
    {
        private readonly HashSet<string>[] _sets;
        internal Exclusions(IEnumerable<string> paths) => _sets = [new(paths, StringComparer.OrdinalIgnoreCase)];
        private Exclusions(HashSet<string>[] sets) => _sets = sets;
        internal static Exclusions FromSets(IEnumerable<HashSet<string>> sets)
        {
            const int sharedSetThreshold = 64;
            var shared = new List<HashSet<string>>();
            var small = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var set in sets)
            {
                // 大量 batch の集合は再コピーしない。長寿命 scanner に積み重なった
                // 単体要求の小集合は一つにまとめ、各ファイルの履歴全件比較を防ぐ。
                if (set.Count >= sharedSetThreshold) shared.Add(set);
                else small.UnionWith(set);
            }
            if (small.Count != 0) shared.Add(small);
            return new Exclusions(shared.ToArray());
        }
        internal bool Contains(string path)
        {
            // 退避対象がディレクトリでも、全出力との線形比較を行わず祖先集合で判定する。
            for (string? current = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar); current != null; current = Path.GetDirectoryName(current))
                foreach (var set in _sets)
                    if (set.Contains(current)) return true;
            return false;
        }
    }

    internal sealed class ScanLease(string registry, string id, FileStream stream, Exclusions initial) : IDisposable
    {
        internal Exclusions Initial { get; } = initial;
        private bool _disposed;
        internal Exclusions Finish()
        {
            using var gate = CrossProcessResourceGate.EnterMetadata(registry);
            var state = Read(registry);
            var exclusions = GetExclusions(state, id, registry);
            state.Scanners.Remove(id);
            foreach (var output in state.Outputs) output.Scanners.Remove(id);
            state.Outputs.RemoveAll(o => !o.Active && o.Scanners.Count == 0 && !o.HasResidue);
            Write(registry, state);
            DisposeStream();
            return exclusions;
        }
        public void Dispose()
        {
            if (_disposed) return;
            DisposeStream();
            using var gate = CrossProcessResourceGate.EnterMetadata(registry);
            Write(registry, Read(registry));
        }
        private void DisposeStream() { _disposed = true; stream.Dispose(); File.Delete(LeasePath(registry, id)); }
    }

    private sealed class OutputLease(string registry, string id, FileStream stream) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            using var gate = CrossProcessResourceGate.EnterMetadata(registry);
            try
            {
                var state = Read(registry);
                var output = state.Outputs.Find(o => o.Id == id);
                if (output != null) output.Active = false;
                state.Outputs.RemoveAll(o => !o.Active && o.Scanners.Count == 0 && !o.HasResidue);
                Write(registry, state);
            }
            finally { stream.Dispose(); File.Delete(LeasePath(registry, id)); }
        }
    }

    private static Exclusions GetExclusions(State state, string scanner, string registry)
    {
        var sets = state.Outputs.Where(o => o.Scanners.Contains(scanner) || !o.Active)
            .Select(o => o.Scanners.Contains(scanner) ? o.Payload.PathKeys : o.Payload.CleanupKeys);
        // 利用者が AppData を圧縮しても、今回作る登録・生存確認ファイルを入力へ混ぜない。
        return Exclusions.FromSets(sets.Append(new HashSet<string>([OutputPathIdentity.GetCanonicalPath(Path.GetDirectoryName(registry)!)], StringComparer.OrdinalIgnoreCase)));
    }

    private static FileStream OpenLease(string registry, string id) => new(LeasePath(registry, id),
        FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);

    private static bool IsAlive(string registry, string id)
    {
        var path = LeasePath(registry, id);
        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) { }
            File.Delete(path); // 排他所有者が退出した sidecar だけを回収する。
            return false;
        }
        catch (FileNotFoundException) { return false; }
        catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33) { return true; }
    }

    private static State Read(string path)
    {
        var state = new State();
        if (File.Exists(path))
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            state.Version = reader.ReadInt32();
            if (state.Version is not (1 or 2)) throw new InvalidDataException("圧縮出力登録の形式が不正です。");
            var scannerCount = ReadCount(reader);
            for (var i = 0; i < scannerCount; i++)
                if (!state.Scanners.Add(ReadId(reader))) throw new InvalidDataException("圧縮出力登録の識別子が重複しています。");
            var outputCount = ReadCount(reader);
            var ids = new HashSet<string>();
            for (var i = 0; i < outputCount; i++)
            {
                var id = ReadId(reader); var active = reader.ReadBoolean();
                if (!ids.Add(id)) throw new InvalidDataException("圧縮出力登録の識別子が重複しています。");
                var payload = state.Version == 1 ? ReadPayload(reader, legacy: true) : LoadPayload(path, id);
                var scanners = new HashSet<string>();
                var count = ReadCount(reader);
                for (var j = 0; j < count; j++)
                    if (!scanners.Add(ReadId(reader))) throw new InvalidDataException("圧縮出力登録の識別子が重複しています。");
                state.Outputs.Add(new Output(id, payload, scanners) { Active = active });
            }
            EnsureEnd(reader);
        }
        // 全データの検証後にだけ、退出した lease の回収・状態変更へ進む。
        state.Scanners.RemoveWhere(id => !IsAlive(path, id));
        foreach (var output in state.Outputs)
        {
            output.Scanners.IntersectWith(state.Scanners);
            if (output.Active && !IsAlive(path, output.Id)) output.Active = false;
        }
        state.Outputs.RemoveAll(o => !o.Active && o.Scanners.Count == 0 && !o.HasResidue);
        // 旧 worker の生存中は v1 を維持。退出済みの残骸は payload へ移せる。
        if (state.Version == 1 && state.Scanners.Count == 0 && state.Outputs.All(o => !o.Active))
        {
            state.Version = 2;
            foreach (var output in state.Outputs)
                output.Payload = new Payload(output.Payload.PathKeys.ToArray(), output.Payload.CleanupKeys.ToArray());
        }
        return state;
    }

    private static string ReadId(BinaryReader reader)
    {
        var id = reader.ReadString();
        return Guid.TryParseExact(id, "N", out _) ? id : throw new InvalidDataException("圧縮出力登録の識別子が不正です。");
    }

    private static Payload ReadPayload(BinaryReader reader, bool legacy = false)
    {
        var paths = new string[ReadCount(reader)];
        for (var i = 0; i < paths.Length; i++) paths[i] = ReadPath(reader);
        var cleanup = new string[ReadCount(reader)];
        for (var i = 0; i < cleanup.Length; i++) cleanup[i] = ReadPath(reader);
        return new Payload(paths, cleanup, legacy);
    }

    private static string ReadPath(BinaryReader reader)
    {
        var path = reader.ReadString();
        if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("圧縮出力登録のパスが不正です。");
        return path;
    }

    private static void EnsureEnd(BinaryReader reader)
    {
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("圧縮出力登録に余分なデータがあります。");
    }

    private static Payload LoadPayload(string registry, string id)
    {
        var path = PayloadPath(registry, id);
        if (!File.Exists(path)) throw new FileNotFoundException("圧縮出力登録の一覧がありません。", path);
        return PayloadCache.GetOrAdd(path, static path =>
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            var payload = ReadPayload(reader);
            EnsureEnd(reader);
            payload.Persisted = true;
            return payload;
        });
    }

    private static void SavePayload(string path, Payload payload, bool overwrite)
    {
        // v1 が正本の移行は、前回の中途終了で残った payload も毎回再確定する。
        // state より先に原子的に確定し、partial payload を v2 から参照させない。
        var next = path + ".next";
        using (var writer = new BinaryWriter(new FileStream(next, FileMode.Create, FileAccess.Write, FileShare.None)))
            WritePayload(writer, payload);
        Commit(next, path, overwrite);
        payload.Persisted = true;
    }

    private static void WritePayload(BinaryWriter writer, Payload payload)
    {
        writer.Write(payload.Paths.Length);
        foreach (var entry in payload.Paths) writer.Write(entry);
        writer.Write(payload.CleanupPaths.Length);
        foreach (var entry in payload.CleanupPaths) writer.Write(entry);
    }

    private static int ReadCount(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        // writer の各要素は最低 1 byte を使う。正常な大量計画を固定上限で拒否せず、
        // 残りデータから成立しない件数だけを、配列確保や列挙より先に拒否する。
        var remainingBytes = reader.BaseStream.Length - reader.BaseStream.Position;
        return count >= 0 && count <= remainingBytes
            ? count : throw new InvalidDataException("圧縮出力登録の件数が不正です。");
    }

    private static void Write(string path, State state)
    {
        if (state.Version == 2)
            foreach (var output in state.Outputs)
            {
                var payloadPath = PayloadPath(path, output.Id);
                if (!output.Payload.Persisted) SavePayload(payloadPath, output.Payload, overwrite: true);
                PayloadCache[payloadPath] = output.Payload;
            }
        // 更新途中に worker が終了しても、最後に確定した登録を破損させない。
        var next = path + ".next";
        using (var writer = new BinaryWriter(new FileStream(next, FileMode.Create, FileAccess.Write, FileShare.None)))
        {
            writer.Write(state.Version); writer.Write(state.Scanners.Count);
            foreach (var id in state.Scanners) writer.Write(id);
            writer.Write(state.Outputs.Count);
            foreach (var output in state.Outputs)
            {
                writer.Write(output.Id); writer.Write(output.Active);
                if (state.Version == 1) WritePayload(writer, output.Payload);
                writer.Write(output.Scanners.Count);
                foreach (var scanner in output.Scanners) writer.Write(scanner);
            }
        }
        Commit(next, path, overwrite: true);
        var retained = state.Outputs.Select(o => PayloadPath(path, o.Id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directory = Path.GetDirectoryName(path)!;
        foreach (var stalePath in Directory.EnumerateFiles(directory, "*.payload*"))
        {
            var name = Path.GetFileName(stalePath);
            var id = name.EndsWith(".payload.next", StringComparison.Ordinal) ? name[..^13]
                : name.EndsWith(".payload", StringComparison.Ordinal) ? name[..^8] : "";
            // 同じ gate 内で登録が確定し、全 scanner の参照が消えた payload だけ回収する。
            // Dispose 呼び出し元の lease handle は、この時点ではまだ生存している。
            if (retained.Contains(PayloadPath(path, id)) || !Guid.TryParseExact(id, "N", out _)) continue;
            PayloadCache.TryRemove(stalePath, out _);
            File.Delete(stalePath);
        }
        // 別 process が回収済みの payload も、この process の集合から解放する。
        foreach (var cachedPath in PayloadCache.Keys.Where(p => string.Equals(Path.GetDirectoryName(p), directory, StringComparison.OrdinalIgnoreCase) && !retained.Contains(p)))
            PayloadCache.TryRemove(cachedPath, out _);
    }

    private static void Commit(string next, string path, bool overwrite)
    {
        // 小さな metadata の連続置換で一時的な ACCESS_DENIED を実測したため、
        // 原子的な確定操作だけを短く再試行する。恒常的な拒否は呼び出し元へ返す。
        for (var attempt = 0; ; attempt++)
        {
            try { File.Move(next, path, overwrite); return; }
            catch (Exception ex) when (attempt < 4 &&
                ((ex is IOException && (ex.HResult & 0xffff) is 32 or 33) ||
                 (ex is UnauthorizedAccessException && (ex.HResult & 0xffff) == 5)))
            { Thread.Sleep(10 << attempt); }
        }
    }
}
