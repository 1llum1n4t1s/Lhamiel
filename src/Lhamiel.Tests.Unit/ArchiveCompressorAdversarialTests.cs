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
    // 🗡️ 境界値・型パンチ — SplitStemAndExtension
    // ==============================

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// SplitStemAndExtension が通常拡張子・複合拡張子・境界的なファイル名を期待どおり分割すること。
    /// 旧 ArchiveCompressorAdversarialTests の SplitStemAndExtension_* 8 件と
    /// SplitStemAndExtension の各入力パターンをこの 1 つの Theory で検証する。
    /// </summary>
    [Theory]
    // 空文字列 → ("", "") でクラッシュしない
    [InlineData("", "", "")]
    // ドットのみ → Path.GetExtension(".") = "" なので stem に残る
    [InlineData(".", ".", "")]
    // 複合拡張子が正しく分割される
    [InlineData("archive.tar.gz", "archive", ".tar.gz")]
    [InlineData("archive.tar.bz2", "archive", ".tar.bz2")]
    [InlineData("archive.tar.xz", "archive", ".tar.xz")]
    [InlineData("archive.tar.lz", "archive", ".tar.lz")]
    [InlineData("archive.tar.zst", "archive", ".tar.zst")]
    [InlineData("backup.tar.bz2", "backup", ".tar.bz2")]
    [InlineData("data.tar.xz", "data", ".tar.xz")]
    [InlineData("a.tar.gz", "a", ".tar.gz")]  // 1 文字の stem でも複合扱い
    // ファイル名が複合拡張子と完全一致（stem なし）→ 通常分割（fileName.Length > compoundExt.Length が false）
    [InlineData(".tar.gz", ".tar", ".gz")]
    // 日本語ファイル名
    [InlineData("日本語.tar.gz", "日本語", ".tar.gz")]
    [InlineData("テスト.zip", "テスト", ".zip")]
    [InlineData("ファイル", "ファイル", "")]
    // 通常拡張子・拡張子なし・多重ドット
    [InlineData("photo.jpg", "photo", ".jpg")]
    [InlineData("Makefile", "Makefile", "")]
    [InlineData("my.file.txt", "my.file", ".txt")]
    [InlineData("file...zip", "file..", ".zip")]  // 連続ドットは最後のドットが拡張子
    // ドットファイル（.gitignore）→ stem は空
    [InlineData(".gitignore", "", ".gitignore")]
    // 複合拡張子の大文字小文字混在 → OrdinalIgnoreCase で検出し、元の表記を保つ
    [InlineData("archive.TAR.GZ", "archive", ".TAR.GZ")]
    [InlineData("archive.Tar.Gz", "archive", ".Tar.Gz")]
    public void SplitStemAndExtension_SplitsAsExpected(
        string fileName, string expectedStem, string expectedExt)
    {
        var (stem, ext) = ArchiveCompressor.SplitStemAndExtension(fileName);
        Assert.Equal(expectedStem, stem);
        Assert.Equal(expectedExt, ext);
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
}
