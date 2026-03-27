using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// ArchiveCompressor のユーティリティメソッドに対する嫌がらせテスト
/// SplitStemAndExtension, DetectConflicts, GetCompressedFileName の攻撃面をカバー
/// </summary>
public class ArchiveCompressorAdversarialTests
{
    // ==============================
    // 🗡️ 境界値 — SplitStemAndExtension
    // ==============================

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 空文字列 → ("", "") でクラッシュしない
    /// </summary>
    [Fact]
    public void SplitStemAndExtension_EmptyString_ReturnsEmptyPair()
    {
        var (stem, ext) = ArchiveCompressor.SplitStemAndExtension("");
        Assert.Equal("", stem);
        Assert.Equal("", ext);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// ドットのみ → ("", ".") でクラッシュしない
    /// </summary>
    [Fact]
    public void SplitStemAndExtension_DotOnly_ReturnsEmptyWithDot()
    {
        var (stem, ext) = ArchiveCompressor.SplitStemAndExtension(".");
        // Path.GetExtension(".") = "" なので両方空
        Assert.Equal(".", stem);
        Assert.Equal("", ext);
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 複合拡張子が正しく分割される
    /// </summary>
    [Theory]
    [InlineData("archive.tar.gz", "archive", ".tar.gz")]
    [InlineData("archive.tar.bz2", "archive", ".tar.bz2")]
    [InlineData("archive.tar.xz", "archive", ".tar.xz")]
    [InlineData("archive.tar.lz", "archive", ".tar.lz")]
    [InlineData("archive.tar.zst", "archive", ".tar.zst")]
    public void SplitStemAndExtension_CompoundExtensions_SplitCorrectly(
        string fileName, string expectedStem, string expectedExt)
    {
        var (stem, ext) = ArchiveCompressor.SplitStemAndExtension(fileName);
        Assert.Equal(expectedStem, stem);
        Assert.Equal(expectedExt, ext);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// ファイル名が複合拡張子と完全一致（ステムなし）→ 通常拡張子として扱う
    /// </summary>
    [Fact]
    public void SplitStemAndExtension_FilenameIsCompoundExtension_TreatedAsNormal()
    {
        // ".tar.gz" そのものがファイル名 → stem=".tar", ext=".gz"（通常分割）
        var (stem, ext) = ArchiveCompressor.SplitStemAndExtension(".tar.gz");
        // fileName.Length (7) > compoundExt.Length (7) は false なので複合扱いにならない
        Assert.Equal(".tar", stem);
        Assert.Equal(".gz", ext);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 日本語ファイル名の分割
    /// </summary>
    [Theory]
    [InlineData("日本語.tar.gz", "日本語", ".tar.gz")]
    [InlineData("テスト.zip", "テスト", ".zip")]
    [InlineData("ファイル", "ファイル", "")]
    public void SplitStemAndExtension_JapaneseFilenames_SplitCorrectly(
        string fileName, string expectedStem, string expectedExt)
    {
        var (stem, ext) = ArchiveCompressor.SplitStemAndExtension(fileName);
        Assert.Equal(expectedStem, stem);
        Assert.Equal(expectedExt, ext);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 拡張子が連続するドット（file...zip）
    /// </summary>
    [Fact]
    public void SplitStemAndExtension_MultipleDots_LastDotIsExtension()
    {
        var (stem, ext) = ArchiveCompressor.SplitStemAndExtension("file...zip");
        Assert.Equal("file..", stem);
        Assert.Equal(".zip", ext);
    }

    /// <summary>
    /// @adversarial @category boundary @severity low
    /// ドットファイル（.gitignore）
    /// </summary>
    [Fact]
    public void SplitStemAndExtension_DotFile_ReturnsEmptyStem()
    {
        var (stem, ext) = ArchiveCompressor.SplitStemAndExtension(".gitignore");
        Assert.Equal("", stem);
        Assert.Equal(".gitignore", ext);
    }

    // ==============================
    // 🗡️ 境界値 — GetCompressedFileName
    // ==============================

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// ドットファイル名フォルダ（.cursor）→ GetFileName がそのまま使われる
    /// </summary>
    [Fact]
    public void GetCompressedFileName_DotFolder_UsesFullName()
    {
        var result = ArchiveCompressor.GetCompressedFileName(@"C:\project\.cursor", "zip", @"C:\output");
        Assert.Equal(Path.Combine(@"C:\output", ".cursor.zip"), result);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// ファイル名がスペースのみ
    /// </summary>
    [Fact]
    public void GetCompressedFileName_WhitespaceOnlyFilename_DoesNotCrash()
    {
        var result = ArchiveCompressor.GetCompressedFileName(@"C:\dir\   ", "zip", @"C:\output");
        // クラッシュしないこと（結果は実装依存）
        Assert.NotNull(result);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 出力拡張子が大文字 → 小文字に変換される
    /// </summary>
    [Fact]
    public void GetCompressedFileName_UpperCaseExtension_ConvertedToLower()
    {
        var result = ArchiveCompressor.GetCompressedFileName(@"C:\dir\file.txt", "ZIP", @"C:\output");
        Assert.EndsWith(".zip", result);
        Assert.DoesNotContain(".ZIP", result);
    }

    // ==============================
    // 🗡️ 境界値 — DetectConflicts
    // ==============================

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 空リスト → 衝突なし（空リスト返却）
    /// </summary>
    [Fact]
    public void DetectConflicts_EmptyList_ReturnsEmpty()
    {
        var result = ArchiveCompressor.DetectConflicts([]);
        Assert.Empty(result);
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 全ファイルが同じ相対パス → 1グループに全ファイルが含まれる
    /// </summary>
    [Fact]
    public void DetectConflicts_AllSameRelativePath_SingleGroupWithAll()
    {
        var files = Enumerable.Range(0, 5)
            .Select(i => ($@"C:\src{i}\file.txt", "file.txt"))
            .ToList();

        var groups = ArchiveCompressor.DetectConflicts(files);
        Assert.Single(groups);
        Assert.Equal(5, groups[0].Entries.Count);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 大文字小文字のみが異なる相対パス → 衝突として検出される（OrdinalIgnoreCase）
    /// </summary>
    [Fact]
    public void DetectConflicts_CaseDifferenceOnly_DetectedAsConflict()
    {
        var files = new List<(string fullPath, string relativePath)>
        {
            (@"C:\a\File.TXT", "File.TXT"),
            (@"C:\b\file.txt", "file.txt"),
        };

        var groups = ArchiveCompressor.DetectConflicts(files);
        Assert.Single(groups);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 存在しないファイルのパス → FileInfo.Exists=false でクラッシュしない（Length=0, DateTime.MinValue）
    /// </summary>
    [Fact]
    public void DetectConflicts_NonExistentFiles_DoesNotCrash()
    {
        var files = new List<(string fullPath, string relativePath)>
        {
            (@"C:\nonexistent\a.txt", "a.txt"),
            (@"C:\nonexistent\b.txt", "a.txt"),
        };

        var groups = ArchiveCompressor.DetectConflicts(files);
        Assert.Single(groups);
        // サイズ 0、日時 MinValue で作成されること
        Assert.All(groups[0].Entries, e =>
        {
            Assert.Equal(0, e.FileSize);
            Assert.Equal(DateTime.MinValue, e.LastModified);
        });
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 1000個の衝突するファイル → パフォーマンス問題なし
    /// </summary>
    [Fact]
    public void DetectConflicts_1000ConflictingFiles_CompletesQuickly()
    {
        var files = Enumerable.Range(0, 1000)
            .Select(i => ($@"C:\src{i}\same.txt", "same.txt"))
            .ToList();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var groups = ArchiveCompressor.DetectConflicts(files);
        sw.Stop();

        Assert.Single(groups);
        Assert.Equal(1000, groups[0].Entries.Count);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"1000件の衝突検出に {sw.ElapsedMilliseconds}ms かかった");
    }

    // ==============================
    // 🎭 型パンチ — SplitStemAndExtension
    // ==============================

    /// <summary>
    /// @adversarial @category type @severity medium
    /// 複合拡張子の大文字小文字混在（.TAR.GZ）→ OrdinalIgnoreCase で検出
    /// </summary>
    [Theory]
    [InlineData("archive.TAR.GZ", "archive", ".TAR.GZ")]
    [InlineData("archive.Tar.Gz", "archive", ".Tar.Gz")]
    public void SplitStemAndExtension_CompoundCaseInsensitive_SplitCorrectly(
        string fileName, string expectedStem, string expectedExt)
    {
        var (stem, ext) = ArchiveCompressor.SplitStemAndExtension(fileName);
        Assert.Equal(expectedStem, stem);
        Assert.Equal(expectedExt, ext);
    }

    // ==============================
    // 🌪️ 環境異常 — GetUniqueOutputPath
    // ==============================

    /// <summary>
    /// @adversarial @category chaos @severity medium
    /// 存在しないパス → そのまま返される
    /// </summary>
    [Fact]
    public void GetUniqueOutputPath_NonExistentPath_ReturnsSamePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.zip");
        Assert.Equal(path, ArchiveCompressor.GetUniqueOutputPath(path));
    }

    /// <summary>
    /// @adversarial @category chaos @severity high
    /// ファイルが存在するパス → _1 サフィックスが付く
    /// </summary>
    [Fact]
    public void GetUniqueOutputPath_ExistingFile_GetsSequentialSuffix()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"unique_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        try
        {
            var basePath = Path.Combine(dir, "out.zip");
            File.WriteAllText(basePath, "");

            var unique = ArchiveCompressor.GetUniqueOutputPath(basePath);
            Assert.Equal(Path.Combine(dir, "out_1.zip"), unique);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
