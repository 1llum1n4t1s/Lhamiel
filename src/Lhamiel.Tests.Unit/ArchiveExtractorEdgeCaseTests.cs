using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// ArchiveExtractor の追加エッジケーステスト（実装を信用しない）
/// </summary>
public class ArchiveExtractorEdgeCaseTests
{
    // === IsSupportedArchiveType エッジケース ===

    [Theory]
    [InlineData(".zip")]
    [InlineData(".7z")]
    [InlineData(".tar")]
    [InlineData(".gz")]
    [InlineData(".tgz")]
    [InlineData(".bz2")]
    [InlineData(".tbz2")]
    [InlineData(".tbz")]
    [InlineData(".lzma")]
    [InlineData(".tlz")]
    [InlineData(".xz")]
    [InlineData(".txz")]
    [InlineData(".rar")]
    [InlineData(".lzh")]
    [InlineData(".lha")]
    [InlineData(".cab")]
    [InlineData(".arj")]
    [InlineData(".z")]
    [InlineData(".tz")]
    public void IsSupportedArchiveType_AllSupportedExtensions_ReturnTrue(string ext)
    {
        Assert.True(ArchiveExtractor.IsSupportedArchiveType($"archive{ext}"));
    }

    [Fact]
    public void IsSupportedArchiveType_WithUpperCaseExtension_ReturnsTrue()
    {
        Assert.True(ArchiveExtractor.IsSupportedArchiveType("file.ZIP"));
    }

    [Fact]
    public void IsSupportedArchiveType_WithMixedCaseExtension_ReturnsTrue()
    {
        Assert.True(ArchiveExtractor.IsSupportedArchiveType("file.ZiP"));
    }

    [Fact]
    public void IsSupportedArchiveType_WithNoExtension_ReturnsFalse()
    {
        Assert.False(ArchiveExtractor.IsSupportedArchiveType("archive"));
    }

    [Fact]
    public void IsSupportedArchiveType_WithDocx_ReturnsFalse()
    {
        // ZIPベースだがアーカイブではない
        Assert.False(ArchiveExtractor.IsSupportedArchiveType("document.docx"));
    }

    [Fact]
    public void IsSupportedArchiveType_WithPathIncludingDirectories_ReturnsTrue()
    {
        Assert.True(ArchiveExtractor.IsSupportedArchiveType(@"C:\Users\downloads\archive.zip"));
    }

    [Fact]
    public void IsSupportedArchiveType_WithDotInFolderName_ReturnsCorrectResult()
    {
        Assert.True(ArchiveExtractor.IsSupportedArchiveType(@"C:\project.v2\archive.zip"));
        Assert.False(ArchiveExtractor.IsSupportedArchiveType(@"C:\project.v2\readme.txt"));
    }

    // === AreAllSupportedArchives エッジケース ===

    [Fact]
    public void AreAllSupportedArchives_WithNonExistentFiles_ReturnsFalse()
    {
        var paths = new[] { @"C:\nonexistent\file.zip" };
        Assert.False(ArchiveExtractor.AreAllSupportedArchives(paths));
    }

    [Fact]
    public void AreAllSupportedArchives_WithDirectoryPath_ReturnsFalse()
    {
        // ディレクトリはアーカイブではない
        var tempDir = Path.GetTempPath();
        var paths = new[] { tempDir };
        Assert.False(ArchiveExtractor.AreAllSupportedArchives(paths));
    }

    [Fact]
    public void AreAllSupportedArchives_WithMixedExistenceAndNonExistence_ReturnsFalse()
    {
        // 存在しないファイルが1つでもあればfalse
        var existingFile = Path.GetTempFileName();
        try
        {
            var paths = new[] { existingFile, @"C:\nonexistent\archive.zip" };
            Assert.False(ArchiveExtractor.AreAllSupportedArchives(paths));
        }
        finally
        {
            File.Delete(existingFile);
        }
    }

    // === ShouldShowOverwriteDialog エッジケース ===

    [Fact]
    public void ShouldShowOverwriteDialog_WhenOutputPathDoesNotExist_ReturnsFalse()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var result = ArchiveExtractor.ShouldShowOverwriteDialog(nonExistentPath, null);
        Assert.False(result);
    }

    [Fact]
    public void ShouldShowOverwriteDialog_WithEmptyOverwriteCheckPaths_HasNoOverwriteTarget()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            // 空配列は「親フォルダー直下へ展開するが置換対象なし」を表す。
            var result = ArchiveExtractor.ShouldShowOverwriteDialog(tempDir, Array.Empty<string>());
            Assert.False(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ShouldShowOverwriteDialog_WithMultipleCheckPathsOneExists_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "test_" + Guid.NewGuid());
        var existingSub = Path.Combine(tempDir, "existing");
        Directory.CreateDirectory(existingSub);
        try
        {
            var paths = new[]
            {
                Path.Combine(tempDir, "nonexistent"),
                existingSub,
                Path.Combine(tempDir, "another_nonexistent")
            };
            var result = ArchiveExtractor.ShouldShowOverwriteDialog(tempDir, paths);
            Assert.True(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // === IgnoredSystemFiles/Directories ===

    [Fact]
    public void IgnoredSystemDirectories_ContainsMacOSX()
    {
        Assert.Contains("__MACOSX", ArchiveExtractor.IgnoredSystemDirectories);
    }

    [Fact]
    public void IgnoredSystemFiles_ContainsExpectedItems()
    {
        Assert.Contains("desktop.ini", ArchiveExtractor.IgnoredSystemFiles);
        Assert.Contains("Thumbs.db", ArchiveExtractor.IgnoredSystemFiles);
        Assert.Contains(".DS_Store", ArchiveExtractor.IgnoredSystemFiles);
    }

    [Fact]
    public void IgnoredSystemFiles_IsCaseInsensitive()
    {
        Assert.Contains("DESKTOP.INI", ArchiveExtractor.IgnoredSystemFiles);
        Assert.Contains("thumbs.DB", ArchiveExtractor.IgnoredSystemFiles);
    }


    [Fact]
    public void SupportedExtensions_AllStartWithDot()
    {
        foreach (var ext in ArchiveExtractor.SupportedExtensions)
            Assert.StartsWith(".", ext);
    }

    [Fact]
    public void SupportedExtensions_AllLowerCase()
    {
        foreach (var ext in ArchiveExtractor.SupportedExtensions)
            Assert.Equal(ext, ext.ToLowerInvariant());
    }
}
