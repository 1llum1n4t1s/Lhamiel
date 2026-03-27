using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// ArchiveExtractor の静的ユーティリティメソッドに対する嫌がらせテスト
/// </summary>
public class ArchiveExtractorAdversarialTests
{
    // ==============================
    // 🗡️ 境界値・極端入力 — IsSupportedArchiveType
    // ==============================

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 空文字列を渡してもクラッシュしない
    /// </summary>
    [Fact]
    public void IsSupportedArchiveType_EmptyString_ReturnsFalse()
    {
        Assert.False(ArchiveExtractor.IsSupportedArchiveType(""));
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 拡張子なしのファイルパス → false
    /// </summary>
    [Fact]
    public void IsSupportedArchiveType_NoExtension_ReturnsFalse()
    {
        Assert.False(ArchiveExtractor.IsSupportedArchiveType("Makefile"));
        Assert.False(ArchiveExtractor.IsSupportedArchiveType(@"C:\path\to\LICENSE"));
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// ドットだけの拡張子 → false
    /// </summary>
    [Fact]
    public void IsSupportedArchiveType_DotOnly_ReturnsFalse()
    {
        Assert.False(ArchiveExtractor.IsSupportedArchiveType("file."));
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 大文字・混在ケースの拡張子がサポートされる（大文字小文字無視）
    /// </summary>
    [Theory]
    [InlineData("archive.ZIP")]
    [InlineData("archive.Zip")]
    [InlineData("archive.7Z")]
    [InlineData("archive.Tar")]
    [InlineData("archive.RAR")]
    [InlineData("archive.LZH")]
    public void IsSupportedArchiveType_CaseInsensitive_ReturnsTrue(string path)
    {
        Assert.True(ArchiveExtractor.IsSupportedArchiveType(path));
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 二重拡張子（.tar.gz）→ 外側の .gz がサポート対象
    /// </summary>
    [Theory]
    [InlineData("archive.tar.gz", true)]   // .gz はサポート
    [InlineData("archive.tar.bz2", true)]  // .bz2 はサポート
    [InlineData("archive.tar.xz", true)]   // .xz はサポート
    [InlineData("archive.tar.txt", false)] // .txt は非サポート
    public void IsSupportedArchiveType_DoubleExtension_ChecksOuterExtension(string path, bool expected)
    {
        Assert.Equal(expected, ArchiveExtractor.IsSupportedArchiveType(path));
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 拡張子がアーカイブっぽいが非サポートの形式 → false
    /// </summary>
    [Theory]
    [InlineData("file.iso")]
    [InlineData("file.dmg")]
    [InlineData("file.pkg")]
    [InlineData("file.deb")]
    [InlineData("file.rpm")]
    [InlineData("file.msi")]
    public void IsSupportedArchiveType_UnsupportedArchiveFormats_ReturnsFalse(string path)
    {
        Assert.False(ArchiveExtractor.IsSupportedArchiveType(path));
    }

    /// <summary>
    /// @adversarial @category boundary @severity low
    /// Windows 予約名 + アーカイブ拡張子 → サポート対象（拡張子のみで判定）
    /// </summary>
    [Theory]
    [InlineData("CON.zip")]
    [InlineData("NUL.7z")]
    [InlineData("COM1.tar")]
    public void IsSupportedArchiveType_WindowsReservedNames_ReturnsTrue(string path)
    {
        Assert.True(ArchiveExtractor.IsSupportedArchiveType(path));
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 超長パス（260文字超）でもクラッシュしない
    /// </summary>
    [Fact]
    public void IsSupportedArchiveType_VeryLongPath_DoesNotCrash()
    {
        var longDir = new string('a', 300);
        var path = $@"C:\{longDir}\archive.zip";
        // Path.GetExtension は長いパスでもクラッシュしない
        Assert.True(ArchiveExtractor.IsSupportedArchiveType(path));
    }

    // ==============================
    // 🗡️ 境界値 — AreAllSupportedArchives
    // ==============================

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 空のコレクション → true（vacuous truth: すべての要素が条件を満たす）
    /// </summary>
    [Fact]
    public void AreAllSupportedArchives_EmptyCollection_ReturnsTrue()
    {
        Assert.True(ArchiveExtractor.AreAllSupportedArchives([]));
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 存在しないファイルパスのみ → false（File.Exists が false）
    /// </summary>
    [Fact]
    public void AreAllSupportedArchives_NonExistentFiles_ReturnsFalse()
    {
        var paths = new[] { @"C:\nonexistent\fake.zip", @"C:\nonexistent\fake.7z" };
        Assert.False(ArchiveExtractor.AreAllSupportedArchives(paths));
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// ディレクトリパス（.zip 拡張子付き）→ false（File.Exists が false）
    /// </summary>
    [Fact]
    public void AreAllSupportedArchives_DirectoryPath_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "fake_archive.zip");
        Directory.CreateDirectory(tempDir);
        try
        {
            Assert.False(ArchiveExtractor.AreAllSupportedArchives([tempDir]));
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }

    // ==============================
    // 🗡️ 境界値 — GetOutputDirectory / GetBaseOutputDirectory
    // ==============================

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// アーカイブ名が拡張子のみ（.zip）→ GetFileNameWithoutExtension が空文字を返す
    /// </summary>
    [Fact]
    public void GetOutputDirectory_ExtensionOnlyFilename_ReturnsEmptyFolderName()
    {
        var result = ArchiveExtractor.GetOutputDirectory(@"C:\dir\.zip", @"C:\output");
        // Path.GetFileNameWithoutExtension(".zip") = "" なので、outputDir は "C:\output\" になる
        Assert.Equal(Path.Combine(@"C:\output", ""), result);
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// デフォルト出力ディレクトリが空白文字のみ → アーカイブの親ディレクトリにフォールバック
    /// </summary>
    [Fact]
    public void GetBaseOutputDirectory_WhitespaceOnlyDefault_FallsBackToArchiveDir()
    {
        var result = ArchiveExtractor.GetBaseOutputDirectory(@"C:\archives\test.zip", "   ");
        Assert.Equal(@"C:\archives", result);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// outputToSameDirectory=true でアーカイブパスにディレクトリがない → defaultOutputDir にフォールバック
    /// </summary>
    [Fact]
    public void GetBaseOutputDirectory_RootArchive_SameDirectory_FallsBackToDefault()
    {
        var result = ArchiveExtractor.GetBaseOutputDirectory("test.zip", @"C:\output", outputToSameDirectory: true);
        // Path.GetDirectoryName("test.zip") = "" → outputToSameDirectory で "" 採用
        // → IsNullOrWhiteSpace("") = true → defaultOutputDir にフォールバック
        Assert.Equal(@"C:\output", result);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 二重拡張子ファイルの出力ディレクトリ名（archive.tar.gz → archive.tar）
    /// </summary>
    [Fact]
    public void GetOutputDirectory_DoubleExtension_UsesOuterStemOnly()
    {
        var result = ArchiveExtractor.GetOutputDirectory(@"C:\dir\archive.tar.gz", @"C:\output");
        // Path.GetFileNameWithoutExtension("archive.tar.gz") = "archive.tar"
        Assert.Equal(Path.Combine(@"C:\output", "archive.tar"), result);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 日本語ファイル名のアーカイブ → 出力ディレクトリ名も日本語
    /// </summary>
    [Fact]
    public void GetOutputDirectory_JapaneseFilename_PreservedInOutput()
    {
        var result = ArchiveExtractor.GetOutputDirectory(@"C:\dir\日本語アーカイブ.zip", @"C:\output");
        Assert.Equal(Path.Combine(@"C:\output", "日本語アーカイブ"), result);
    }

    // ==============================
    // 🎭 型パンチ — IsSupportedArchiveType
    // ==============================

    /// <summary>
    /// @adversarial @category type @severity medium
    /// Unicode 制御文字を含むパスでクラッシュしない
    /// </summary>
    [Theory]
    [InlineData("file\u200B.zip")]      // ゼロ幅スペース
    [InlineData("file\u202E.zip")]      // RTL override
    [InlineData("\uFEFFfile.zip")]      // BOM
    public void IsSupportedArchiveType_UnicodeControlChars_DoesNotCrash(string path)
    {
        // クラッシュしないこと（結果は true/false どちらでも OK）
        var _ = ArchiveExtractor.IsSupportedArchiveType(path);
    }

    /// <summary>
    /// @adversarial @category type @severity medium
    /// 絵文字を含むファイル名でクラッシュしない
    /// </summary>
    [Fact]
    public void IsSupportedArchiveType_EmojiFilename_DoesNotCrash()
    {
        Assert.True(ArchiveExtractor.IsSupportedArchiveType("📦アーカイブ.zip"));
        Assert.False(ArchiveExtractor.IsSupportedArchiveType("📦アーカイブ.txt"));
    }

    // ==============================
    // 🗡️ 境界値 — SupportedExtensions / IgnoredSystemFiles
    // ==============================

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// SupportedExtensions に全ての文書化された形式が含まれている（ドキュメントとの整合性）
    /// </summary>
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
    [InlineData(".cab")]
    [InlineData(".arj")]
    [InlineData(".z")]
    [InlineData(".tz")]
    public void SupportedExtensions_ContainsAllDocumentedFormats(string ext)
    {
        Assert.Contains(ext, ArchiveExtractor.SupportedExtensions);
    }

    /// <summary>
    /// @adversarial @category boundary @severity low
    /// IgnoredSystemFiles がケースインセンシティブに動作する
    /// </summary>
    [Theory]
    [InlineData("desktop.ini")]
    [InlineData("DESKTOP.INI")]
    [InlineData("Desktop.INI")]
    [InlineData("Thumbs.db")]
    [InlineData("THUMBS.DB")]
    [InlineData(".DS_Store")]
    [InlineData(".ds_store")]
    public void IgnoredSystemFiles_CaseInsensitive(string fileName)
    {
        Assert.Contains(fileName, ArchiveExtractor.IgnoredSystemFiles);
    }
}
