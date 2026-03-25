using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// 同名ファイル衝突解決のテスト
/// </summary>
public class FileConflictResolutionTests
{
    [Fact]
    public void ResolveRelativePathConflicts_衝突なし_変更なし()
    {
        var files = new List<(string fullPath, string relativePath)>
        {
            (@"C:\A\file1.txt", "file1.txt"),
            (@"C:\B\file2.txt", "file2.txt"),
            (@"C:\C\file3.txt", "file3.txt"),
        };

        var result = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: false);

        Assert.Equal(3, result.Count);
        Assert.Equal("file1.txt", result[0].relativePath);
        Assert.Equal("file2.txt", result[1].relativePath);
        Assert.Equal("file3.txt", result[2].relativePath);
    }

    [Fact]
    public void ResolveRelativePathConflicts_リネーム方式_連番サフィックス付与()
    {
        var files = new List<(string fullPath, string relativePath)>
        {
            (@"C:\FolderA\001.jpg", "001.jpg"),
            (@"C:\FolderB\001.jpg", "001.jpg"),
            (@"C:\FolderC\001.jpg", "001.jpg"),
        };

        var result = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: false);

        Assert.Equal(3, result.Count);
        Assert.Equal("001.jpg", result[0].relativePath);
        Assert.Equal("001_1.jpg", result[1].relativePath);
        Assert.Equal("001_2.jpg", result[2].relativePath);
    }

    [Fact]
    public void ResolveRelativePathConflicts_パス保持方式_親フォルダ名付与()
    {
        var files = new List<(string fullPath, string relativePath)>
        {
            (@"C:\FolderA\001.jpg", "001.jpg"),
            (@"C:\FolderB\001.jpg", "001.jpg"),
        };

        var result = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: true);

        Assert.Equal(2, result.Count);
        Assert.Equal(Path.Combine("FolderA", "001.jpg"), result[0].relativePath);
        Assert.Equal(Path.Combine("FolderB", "001.jpg"), result[1].relativePath);
    }

    [Fact]
    public void ResolveRelativePathConflicts_パス保持方式_既にサブフォルダ付きは変更なし()
    {
        // サブフォルダ付き + フラットで衝突するケース
        var files = new List<(string fullPath, string relativePath)>
        {
            (@"C:\Parent\SubA\file.txt", Path.Combine("SubA", "file.txt")),
            (@"C:\FolderB\file.txt", "file.txt"),
            (@"C:\FolderC\file.txt", "file.txt"),
        };

        var result = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: true);

        Assert.Equal(3, result.Count);
        // サブフォルダ付きはそのまま
        Assert.Equal(Path.Combine("SubA", "file.txt"), result[0].relativePath);
        // フラットなファイルは親フォルダ名が付く
        Assert.Equal(Path.Combine("FolderB", "file.txt"), result[1].relativePath);
        Assert.Equal(Path.Combine("FolderC", "file.txt"), result[2].relativePath);
    }

    [Fact]
    public void ResolveRelativePathConflicts_リネーム方式_大文字小文字無視で衝突検出()
    {
        var files = new List<(string fullPath, string relativePath)>
        {
            (@"C:\A\File.TXT", "File.TXT"),
            (@"C:\B\file.txt", "file.txt"),
        };

        var result = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: false);

        Assert.Equal(2, result.Count);
        Assert.Equal("File.TXT", result[0].relativePath);
        // 大文字小文字が異なっても衝突として扱われる
        Assert.Equal("file_1.txt", result[1].relativePath);
    }

    [Fact]
    public void ResolveRelativePathConflicts_リネーム方式_部分衝突のみリネーム()
    {
        var files = new List<(string fullPath, string relativePath)>
        {
            (@"C:\A\001.jpg", "001.jpg"),
            (@"C:\B\002.jpg", "002.jpg"),
            (@"C:\C\001.jpg", "001.jpg"),
            (@"C:\D\003.jpg", "003.jpg"),
        };

        var result = ArchiveCompressor.ResolveRelativePathConflicts(files, preservePath: false);

        Assert.Equal(4, result.Count);
        Assert.Equal("001.jpg", result[0].relativePath);
        Assert.Equal("002.jpg", result[1].relativePath);
        Assert.Equal("001_1.jpg", result[2].relativePath);
        Assert.Equal("003.jpg", result[3].relativePath);
    }

    [Fact]
    public void GetUniqueOutputPath_既存ファイルなし_そのまま返す()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "UniquePathTest_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            var path = Path.Combine(tempDir, "output.zip");
            var result = ArchiveCompressor.GetUniqueOutputPath(path);
            Assert.Equal(path, result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetUniqueOutputPath_既存ファイルあり_連番サフィックス付与()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "UniquePathTest_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            // 既存ファイルを作成
            var basePath = Path.Combine(tempDir, "output.zip");
            File.WriteAllText(basePath, "dummy");

            var result = ArchiveCompressor.GetUniqueOutputPath(basePath);
            Assert.Equal(Path.Combine(tempDir, "output_1.zip"), result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetUniqueOutputPath_連番も既存_次の連番を返す()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "UniquePathTest_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            // 既存ファイルを複数作成
            File.WriteAllText(Path.Combine(tempDir, "output.zip"), "dummy");
            File.WriteAllText(Path.Combine(tempDir, "output_1.zip"), "dummy");
            File.WriteAllText(Path.Combine(tempDir, "output_2.zip"), "dummy");

            var result = ArchiveCompressor.GetUniqueOutputPath(Path.Combine(tempDir, "output.zip"));
            Assert.Equal(Path.Combine(tempDir, "output_3.zip"), result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // SplitStemAndExtension テスト

    [Theory]
    [InlineData("archive.tar.gz", "archive", ".tar.gz")]
    [InlineData("backup.tar.bz2", "backup", ".tar.bz2")]
    [InlineData("data.tar.xz", "data", ".tar.xz")]
    [InlineData("photo.jpg", "photo", ".jpg")]
    [InlineData("Makefile", "Makefile", "")]
    [InlineData(".gitignore", "", ".gitignore")]
    [InlineData("my.file.txt", "my.file", ".txt")]
    [InlineData("a.tar.gz", "a", ".tar.gz")]
    public void SplitStemAndExtension_各パターン(string fileName, string expectedStem, string expectedExt)
    {
        var (stem, ext) = ArchiveCompressor.SplitStemAndExtension(fileName);
        Assert.Equal(expectedStem, stem);
        Assert.Equal(expectedExt, ext);
    }
}
