using Cube.FileSystem.SevenZip;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// スキャン→Add()→Save() の間にファイルが削除された場合の動作を検証する。
/// 一時コピー廃止後の仕様: 削除されたファイルはスキップし、残りのファイルで圧縮を続行する。
/// </summary>
[Collection("Sequential")]
public class FileDeletionTimingTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"DeleteTimingTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task WithTempDir(Func<string, Task> action)
    {
        var dir = CreateTempDir();
        try { await action(dir); }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// @adversarial @category state @severity high
    /// スキャン後にファイルが削除された場合、スキップして圧縮が成功する
    /// </summary>
    [Fact]
    public async Task DeletedBeforeAdd_SkippedAndArchiveCreated()
    {
        await WithTempDir(async dir =>
        {
            var remaining = Path.Combine(dir, "remaining.txt");
            var deleted = Path.Combine(dir, "deleted.txt");
            File.WriteAllText(remaining, "残るファイル");
            File.WriteAllText(deleted, "消えるファイル");
            var archivePath = Path.Combine(dir, "out.zip");

            var resolvedFiles = new List<(string fullPath, string relativePath)>
            {
                (remaining, "remaining.txt"),
                (deleted, "deleted.txt")
            };

            // スキャン後にファイルを削除
            File.Delete(deleted);

            // エラーにならず圧縮が完了する
            await ArchiveCompressor.CompressFilesAsync([dir], archivePath, Format.Zip,
                resolvedFiles: resolvedFiles);

            Assert.True(File.Exists(archivePath));
            using var reader = new ArchiveReader(archivePath);
            // 残ったファイルだけがアーカイブに含まれる
            Assert.Contains(reader.Items, i => !i.IsDirectory && i.FullName.Contains("remaining.txt"));
            Assert.DoesNotContain(reader.Items, i => i.FullName.Contains("deleted.txt"));
        });
    }

    /// <summary>
    /// @adversarial @category state @severity high
    /// 全ファイルが削除された場合、空のアーカイブが作成される（クラッシュしない）
    /// </summary>
    [Fact]
    public async Task AllFilesDeleted_CreatesEmptyArchive()
    {
        await WithTempDir(async dir =>
        {
            var file = Path.Combine(dir, "only.txt");
            File.WriteAllText(file, "唯一のファイル");
            var archivePath = Path.Combine(dir, "out.zip");

            var resolvedFiles = new List<(string fullPath, string relativePath)>
            {
                (file, "only.txt")
            };

            File.Delete(file);

            // クラッシュせずに完了する
            await ArchiveCompressor.CompressFilesAsync([dir], archivePath, Format.Zip,
                resolvedFiles: resolvedFiles);

            Assert.True(File.Exists(archivePath));
        });
    }

    /// <summary>
    /// @adversarial @category state @severity medium
    /// ディレクトリエントリ（relativePath が '/' で終わる）は存在チェックをスキップする
    /// </summary>
    [Fact]
    public async Task DirectoryEntry_NotSkippedByExistenceCheck()
    {
        await WithTempDir(async dir =>
        {
            var file = Path.Combine(dir, "file.txt");
            File.WriteAllText(file, "テスト");
            var archivePath = Path.Combine(dir, "out.zip");

            var resolvedFiles = new List<(string fullPath, string relativePath)>
            {
                (dir, "mydir/"),   // ディレクトリエントリ
                (file, "mydir/file.txt")
            };

            await ArchiveCompressor.CompressFilesAsync([dir], archivePath, Format.Zip,
                resolvedFiles: resolvedFiles);

            Assert.True(File.Exists(archivePath));
            using var reader = new ArchiveReader(archivePath);
            Assert.Contains(reader.Items, i => !i.IsDirectory && i.FullName.Contains("file.txt"));
        });
    }

    /// <summary>
    /// @adversarial @category state @severity medium
    /// 全ファイル正常時は全て圧縮される（回帰テスト）
    /// </summary>
    [Fact]
    public async Task AllFilesExist_AllCompressedSuccessfully()
    {
        await WithTempDir(async dir =>
        {
            var file1 = Path.Combine(dir, "a.txt");
            var file2 = Path.Combine(dir, "b.txt");
            File.WriteAllText(file1, "ファイルA");
            File.WriteAllText(file2, "ファイルB");
            var archivePath = Path.Combine(dir, "out.zip");

            await ArchiveCompressor.CompressFilesAsync([dir], archivePath, Format.Zip);

            Assert.True(File.Exists(archivePath));
            using var reader = new ArchiveReader(archivePath);
            Assert.Contains(reader.Items, i => !i.IsDirectory && i.FullName.Contains("a.txt"));
            Assert.Contains(reader.Items, i => !i.IsDirectory && i.FullName.Contains("b.txt"));
        });
    }
}
