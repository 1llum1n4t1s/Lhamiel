namespace Lhamiel.Util;

/// <summary>
/// 圧縮の上書き確認ダイアログ（ArchiveOverwriteDialog）用の純粋ロジック。
/// UI を持たないので単体テストできる。
/// </summary>
internal static class ArchiveOverwriteInfo
{
    /// <summary>この年より前の日時（MinValue や FILETIME の 1601 年）は「取得できなかった値」として扱う。</summary>
    private const int MinValidYear = 1700;

    /// <summary>名前を個別に並べる最大件数。超えた分は「ほかN件」にまとめる。</summary>
    internal const int MaxListedNames = 3;

    /// <summary>
    /// 圧縮前の合計サイズとファイル数を数える。ディレクトリのリパースポイントは辿らず、
    /// 隠し・システムも含め、アクセスできない項目は飛ばす。キャンセルは <see cref="OperationCanceledException"/>。
    /// </summary>
    internal static (long Bytes, int Files) ScanTotal(IReadOnlyList<string> sourcePaths, CancellationToken cancellationToken)
    {
        long bytes = 0;
        var files = 0;
        var pending = new Stack<DirectoryInfo>();

        foreach (var path in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryAddFile(path, ref bytes, ref files)) continue;
            if (Directory.Exists(path)) pending.Push(new DirectoryInfo(path));
        }

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            try
            {
                foreach (var entry in dir.EnumerateFileSystemInfos("*", new EnumerationOptions
                {
                    AttributesToSkip = 0,
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false,
                }))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entry is DirectoryInfo sub)
                    {
                        if (!sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) pending.Push(sub);
                    }
                    else if (entry is FileInfo file)
                    {
                        AddFileLength(file, ref bytes, ref files);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // アクセス不可・消失したフォルダーは飛ばす。
            }
        }

        return (bytes, files);
    }

    /// <summary>1ファイルの取得失敗で同じフォルダーの残りを飛ばさないよう、ファイル単位で捕捉する。</summary>
    private static void AddFileLength(FileInfo file, ref long bytes, ref int files)
    {
        try
        {
            bytes += file.Length;
            files++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 列挙中に消えた・読めないファイルは飛ばす。
        }
    }

    /// <summary>ダイアログ表示前に取得しておく更新日時・サイズ（UI スレッドでファイル I/O をしないため）。</summary>
    internal readonly record struct OverwritePreview(DateTime? DestinationModified, long? DestinationSize, DateTime? SourceModified);

    /// <summary>表示用の更新日時・サイズを集める（呼び出し側が Task.Run で実行する）。</summary>
    internal static OverwritePreview Gather(IReadOnlyList<string> sourcePaths, string destinationPath)
        => new(TryGetModified(destinationPath), TryGetFileSize(destinationPath),
            sourcePaths.Count == 1 ? TryGetModified(sourcePaths[0]) : null);

    private static bool TryAddFile(string path, ref long bytes, ref int files)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return false;
            bytes += info.Length;
            files++;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>更新日時を返す。存在しない・取得できない・偽値（0001年など）のときは null。</summary>
    internal static DateTime? TryGetModified(string path)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            if (!info.Exists) return null;
            var time = info.LastWriteTime;
            return time.Year < MinValidYear ? null : time;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>ファイルのサイズを返す。ディレクトリ・存在しない・取得できないときは null。</summary>
    internal static long? TryGetFileSize(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>パスの末尾の名前（ドライブ直下などで空になるときはパス自体）。</summary>
    internal static string GetDisplayName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    /// <summary>先頭 <see cref="MaxListedNames"/> 件の名前と、超えた件数を返す。</summary>
    internal static (IReadOnlyList<string> Names, int More) SummarizeNames(IReadOnlyList<string> sourcePaths)
    {
        var names = sourcePaths.Take(MaxListedNames).Select(GetDisplayName).ToList();
        return (names, Math.Max(0, sourcePaths.Count - MaxListedNames));
    }
}
