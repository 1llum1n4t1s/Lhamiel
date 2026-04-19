using System.Security;
namespace Lhamiel.Util;

/// <summary>
/// ファイル操作を集約するヘルパー
/// </summary>
internal static class FileOperations
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// 一時展開した内容からファイルをコピーする。
    /// 部分展開用途では <paramref name="overwrite"/> を false にして既存ファイルを保護する。
    /// 主フローとの整合性を保つため、既存ファイル存在時は <see cref="IOException"/> を投げ、
    /// 呼び出し側でスキップ扱いにする。
    /// </summary>
    public static void CopyExtractedItem(string tempPath, string outputPath, string fullName, bool isDirectory, bool overwrite = false)
    {
        var fullTempPath = Path.GetFullPath(tempPath);
        var fullOutputPath = Path.GetFullPath(outputPath);
        var sourcePath = EnsureSafePath(fullTempPath, fullName, "source");
        var targetPath = EnsureSafePath(fullOutputPath, fullName, "target");

        if (isDirectory)
        {
            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }
            return;
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(App.Text("Error.ExtractedFileNotFound"), sourcePath);
        }

        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        // 既存ファイルを無条件上書きしないことで、部分展開フローでも
        // FileConflictDialog と同等の「既存保護」セマンティクスを提供する。
        File.Copy(sourcePath, targetPath, overwrite);
    }

    private static string EnsureSafePath(string basePath, string relativePath, string pathLabel)
    {
        var normalizedBase = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        var combinedPath = Path.GetFullPath(Path.Combine(basePath, relativePath));
        if (!combinedPath.StartsWith(normalizedBase, PathComparison) &&
            !string.Equals(combinedPath, normalizedBase.TrimEnd(Path.DirectorySeparatorChar), PathComparison))
        {
            throw new SecurityException($"Path traversal attempt detected in {pathLabel} path.");
        }

        return combinedPath;
    }

    /// <summary>
    /// 一時ディレクトリを削除する
    /// </summary>
    public static void CleanupTemporaryPath(string tempPath, Action<string>? logWarning = null)
    {
        try
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
        catch (Exception ex)
        {
            logWarning?.Invoke($"一時ディレクトリ削除に失敗しました: {tempPath}, {ex.Message}");
        }
    }
}
