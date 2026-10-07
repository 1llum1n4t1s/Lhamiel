using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lhamiel.Util;

/// <summary>合法な出力ルートの別名を、置換されるファイル名を保った共通キーへ解決する。</summary>
internal static class OutputPathIdentity
{
    internal static string GetCanonicalPath(string path)
    {
        var fullPath = Normalize(path);
        if (!OperatingSystem.IsWindows()) return fullPath;

        var suffix = new Stack<string>();
        var current = fullPath;
        while (true)
        {
            FileAttributes? attributes = null;
            try { attributes = File.GetAttributes(current); }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 2 or 3) { }

            if (attributes is { } existing && (existing & FileAttributes.Directory) != 0)
            {
                var canonical = ResolveDirectory(current);
                while (suffix.TryPop(out var name)) canonical = Path.Combine(canonical, name);
                return Normalize(canonical);
            }
            // ファイル本体の hardlink/symlink は置換で変わるので、親 + leaf をキーにする。
            // 途中の祖先がファイルなら、有効な出力境界を解決できない。
            if (attributes is not null && suffix.Count != 0)
                throw new IOException("出力パスの祖先がディレクトリではありません。");
            var parent = Path.GetDirectoryName(current);
            if (parent is null)
                throw new IOException("出力パスの既存ルートを解決できませんでした。");
            suffix.Push(Path.GetFileName(current));
            current = parent;
        }
    }

    internal static Resolver CreateResolver(IEnumerable<string> sourcePaths) => new(sourcePaths);

    internal sealed class Resolver
    {
        private readonly Dictionary<string, string> _roots = new(StringComparer.OrdinalIgnoreCase);

        internal Resolver(IEnumerable<string> sourcePaths)
        {
            foreach (var source in sourcePaths)
            {
                var root = Normalize(source);
                if (!Directory.Exists(root)) root = Path.GetDirectoryName(root) ?? root;
                if (!_roots.ContainsKey(root)) _roots.Add(root, GetCanonicalPath(root));
            }
        }

        internal string GetKey(string path)
        {
            var fullPath = Normalize(path);
            // DFS は root 配下の reparse point を除外する。各ファイルの handle を開かず、
            // 最も近い明示 source root の解決結果を辞書で O(depth) で再利用する。
            for (string? current = fullPath; current is not null; current = Path.GetDirectoryName(current))
            {
                if (_roots.TryGetValue(current, out var canonical))
                    return current.Length == fullPath.Length ? canonical
                        : Normalize(Path.Combine(canonical, Path.GetRelativePath(current, fullPath)));
            }
            throw new IOException("走査対象が解決済みソースルートの範囲外です。");
        }
    }

    private static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        // 通常パスと long-path prefix の同じ root を resolver でも同一視する。
        if (fullPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            fullPath = @"\\" + fullPath[8..];
        else if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal) && fullPath.Length > 5 && fullPath[5] == ':')
            fullPath = fullPath[4..];
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    private static string ResolveDirectory(string path)
    {
        var nativePath = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path
            : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..]
            : @"\\?\" + path;
        using var handle = CreateFileW(nativePath, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw NativeError("出力ディレクトリを開けませんでした。");

        // GUID は drive letter / SUBST / mount point を統一する。SMB は volume GUID を
        // 持たないため DOS/UNC に限って fallback し、それも失敗したら処理を止める。
        var canonical = ReadFinalPath(handle, 1);
        if (canonical is null && Marshal.GetLastWin32Error() is 2 or 3 or 87)
            canonical = ReadFinalPath(handle, 0);
        return canonical ?? throw NativeError("出力ディレクトリの実パスを解決できませんでした。");
    }

    private static string? ReadFinalPath(SafeFileHandle handle, uint flags)
    {
        var buffer = new char[512];
        while (true)
        {
            var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, flags);
            if (length == 0) return null;
            if (length < buffer.Length) return new string(buffer, 0, (int)length);
            buffer = new char[checked((int)length + 1)];
        }
    }

    private static IOException NativeError(string message)
    {
        var error = Marshal.GetLastWin32Error();
        return new IOException(message, new Win32Exception(error));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, [Out] char[] path, uint length, uint flags);
}
