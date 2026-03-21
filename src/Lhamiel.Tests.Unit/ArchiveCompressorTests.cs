using Cube.FileSystem.SevenZip;
using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// ArchiveCompressor のユニットテスト（実装を信用しないエッジケース重視）
/// </summary>
public class ArchiveCompressorTests
{
    // === ParseFormat ===

    [Theory]
    [InlineData("ZIP", Format.Zip)]
    [InlineData("7Z", Format.SevenZip)]
    [InlineData("TAR", Format.Tar)]
    [InlineData("GZ", Format.GZip)]
    [InlineData("BZ2", Format.BZip2)]
    [InlineData("XZ", Format.XZ)]
    public void ParseFormat_WithUpperCase_ReturnsCorrectFormat(string input, Format expected)
    {
        Assert.Equal(expected, ArchiveCompressor.ParseFormat(input));
    }

    [Theory]
    [InlineData("zip", Format.Zip)]
    [InlineData("7z", Format.SevenZip)]
    [InlineData("tar", Format.Tar)]
    [InlineData("gz", Format.GZip)]
    [InlineData("bz2", Format.BZip2)]
    [InlineData("xz", Format.XZ)]
    public void ParseFormat_WithLowerCase_ReturnsCorrectFormat(string input, Format expected)
    {
        Assert.Equal(expected, ArchiveCompressor.ParseFormat(input));
    }

    [Theory]
    [InlineData("Zip")]
    [InlineData("zIp")]
    [InlineData("ZiP")]
    public void ParseFormat_WithMixedCase_ReturnsZip(string input)
    {
        Assert.Equal(Format.Zip, ArchiveCompressor.ParseFormat(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("rar")]
    [InlineData("lzh")]
    [InlineData("cab")]
    [InlineData("unknown")]
    [InlineData("  zip  ")]
    public void ParseFormat_WithInvalidOrUnsupported_ReturnsZipAsDefault(string input)
    {
        // 不明な形式はデフォルトでZIPにフォールバックすべき
        Assert.Equal(Format.Zip, ArchiveCompressor.ParseFormat(input));
    }

    // === GetCompressedFileName ===

    [Fact]
    public void GetCompressedFileName_WithFile_ReturnsCorrectPath()
    {
        var result = ArchiveCompressor.GetCompressedFileName(@"C:\temp\document.txt", "zip");
        Assert.EndsWith(".zip", result);
        Assert.Contains("document", result);
    }

    [Fact]
    public void GetCompressedFileName_WithFolder_ReturnsCorrectPath()
    {
        var result = ArchiveCompressor.GetCompressedFileName(@"C:\temp\MyProject", "7z");
        Assert.EndsWith(".7z", result);
        Assert.Contains("MyProject", result);
    }

    [Fact]
    public void GetCompressedFileName_WithTrailingSeparator_HandlesCorrectly()
    {
        // フォルダパスの末尾にセパレータがある場合
        var result = ArchiveCompressor.GetCompressedFileName(@"C:\temp\MyProject\", "zip");
        Assert.EndsWith(".zip", result);
        // フォルダ名が正しく取れること（空文字にならないこと）
        Assert.DoesNotContain("..", result);
        Assert.Contains("MyProject", result);
    }

    [Fact]
    public void GetCompressedFileName_WithOutputDirectory_UsesOutputDir()
    {
        var result = ArchiveCompressor.GetCompressedFileName(
            @"C:\source\data.txt", "zip", @"D:\output", outputToSameDirectory: false);
        Assert.StartsWith(@"D:\output", result);
    }

    [Fact]
    public void GetCompressedFileName_WithSameDirectory_UsesSourceDir()
    {
        var result = ArchiveCompressor.GetCompressedFileName(
            @"C:\source\data.txt", "zip", @"D:\output", outputToSameDirectory: true);
        Assert.StartsWith(@"C:\source", result);
    }

    [Fact]
    public void GetCompressedFileName_WithDotFile_HandlesCorrectly()
    {
        // .gitignore のようなドットファイル
        var result = ArchiveCompressor.GetCompressedFileName(@"C:\temp\.gitignore", "zip");
        Assert.EndsWith(".zip", result);
        // ファイル名部分が空にならないこと
        var fileName = Path.GetFileName(result);
        Assert.True(fileName.Length > 4, $"ファイル名が短すぎます: {fileName}");
    }

    [Fact]
    public void GetCompressedFileName_FormatIsLowerCase()
    {
        var result = ArchiveCompressor.GetCompressedFileName(@"C:\temp\file.txt", "ZIP");
        // 拡張子は小文字であるべき
        Assert.EndsWith(".zip", result);
        Assert.DoesNotContain(".ZIP", result);
    }

    // === SupportedCompressionFormats ===

    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    [InlineData("tar")]
    [InlineData("gz")]
    [InlineData("bz2")]
    [InlineData("xz")]
    public void SupportedCompressionFormats_ContainsExpected(string format)
    {
        Assert.Contains(format, ArchiveCompressor.SupportedCompressionFormats);
    }

    [Theory]
    [InlineData("ZIP")]
    [InlineData("7Z")]
    [InlineData("TAR")]
    public void SupportedCompressionFormats_IsCaseInsensitive(string format)
    {
        Assert.Contains(format, ArchiveCompressor.SupportedCompressionFormats);
    }

    [Theory]
    [InlineData("rar")]
    [InlineData("lzh")]
    [InlineData("cab")]
    [InlineData("arj")]
    [InlineData("")]
    public void SupportedCompressionFormats_DoesNotContainUnsupported(string format)
    {
        Assert.DoesNotContain(format, ArchiveCompressor.SupportedCompressionFormats);
    }

    // === ShouldExcludeFile ===

    [Fact]
    public void ShouldExcludeFile_WithMatchingFilename_ReturnsTrue()
    {
        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".DS_Store", "Thumbs.db" };
        Assert.True(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\.DS_Store", patterns));
    }

    [Fact]
    public void ShouldExcludeFile_WithMatchingDirectory_ReturnsTrue()
    {
        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "__MACOSX" };
        Assert.True(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\__MACOSX\file.txt", patterns));
    }

    [Fact]
    public void ShouldExcludeFile_WithNoMatch_ReturnsFalse()
    {
        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".DS_Store" };
        Assert.False(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\readme.txt", patterns));
    }

    [Fact]
    public void ShouldExcludeFile_WithEmptyPatterns_ReturnsFalse()
    {
        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.False(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\.DS_Store", patterns));
    }

    [Fact]
    public void ShouldExcludeFile_CaseInsensitive_ReturnsTrue()
    {
        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "thumbs.db" };
        Assert.True(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\THUMBS.DB", patterns));
    }

    [Fact]
    public void ShouldExcludeFile_PartialMatch_ReturnsFalse()
    {
        // パターンがファイル名の一部にしかマッチしない場合は除外しない
        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Test" };
        Assert.False(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\TestFile.txt", patterns));
    }
}
