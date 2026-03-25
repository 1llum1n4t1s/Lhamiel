namespace Lhamiel.Models;

/// <summary>
/// アーカイブ内で同名になるファイルの情報
/// </summary>
public record FileConflictEntry(
    string FullPath,
    string RelativePath,
    long FileSize,
    DateTime LastModified)
{
    /// <summary>
    /// サムネイル取得用に一時展開されたファイルのパス（存在しない場合は null）
    /// </summary>
    public string? TempThumbnailPath { get; init; }

    /// <summary>
    /// 親フォルダ名（表示用）。
    /// ルートパス（C:\）やUNCルート（\\server\share\）の場合は
    /// Path.GetFileName が空を返すため、ディレクトリパスそのものをフォールバックとして使う。
    /// </summary>
    public string ParentFolderName
    {
        get
        {
            var dir = Path.GetDirectoryName(FullPath) ?? "";
            var name = Path.GetFileName(dir);
            // ルートパス (C:\) や UNCルート (\\server\share) では GetFileName が空を返す
            return !string.IsNullOrEmpty(name) ? name : dir;
        }
    }

    /// <summary>
    /// 短縮パス表示（末尾2階層 + 先頭省略）。例: ...\Documents\a
    /// </summary>
    public string ShortenedPath
    {
        get
        {
            var dir = Path.GetDirectoryName(FullPath);
            if (string.IsNullOrEmpty(dir)) return ParentFolderName;

            var parts = dir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return parts.Length <= 2
                ? dir
                : $@"...\{parts[^2]}\{parts[^1]}";
        }
    }

    /// <summary>
    /// ファイルサイズの表示用文字列
    /// </summary>
    public string FileSizeDisplay => Util.DiskSpaceChecker.FormatSize(FileSize);
}

/// <summary>
/// 同名ファイルのグループ（衝突単位）
/// </summary>
public class FileConflictGroup
{
    /// <summary>
    /// アーカイブ内の相対パス（衝突しているファイル名）
    /// </summary>
    public required string ConflictingName { get; init; }

    /// <summary>
    /// 衝突しているファイルのリスト
    /// </summary>
    public required List<FileConflictEntry> Entries { get; init; }
}

/// <summary>
/// ファイル競合ダイアログの結果
/// </summary>
public enum FileConflictResult
{
    /// <summary>
    /// 続行（ユーザーの選択に従う）
    /// </summary>
    Continue,

    /// <summary>
    /// キャンセル（圧縮を中止）
    /// </summary>
    Cancel
}
