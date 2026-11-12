using Lhamiel.Models;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// FileConflictInfo モデルの嫌がらせテスト 😈
/// </summary>
public class FileConflictInfoAdversarialTests
{
    // ═══════════════════════════════════════════════════
    // 🗡️ 境界値テスト
    // ═══════════════════════════════════════════════════

    [Fact]
    public void ParentFolderName_ルートパス_フォールバック値を返す()
    {
        var entry = new FileConflictEntry(@"C:\file.txt", "file.txt", 1024, DateTime.Now);
        Assert.Equal(@"C:\", entry.ParentFolderName);
    }

    [Fact]
    public void ShortenedPath_深いネスト_末尾2階層で省略()
    {
        var entry = new FileConflictEntry(
            @"C:\a\b\c\d\e\f\g\h\i\j\file.txt", "file.txt", 1024, DateTime.Now);
        var result = entry.ShortenedPath;
        Assert.Contains(@"i\j", result);
        Assert.StartsWith(@"...\", result);
    }

    [Fact]
    public void ShortenedPath_浅いパス_省略なし()
    {
        var entry = new FileConflictEntry(@"C:\docs\file.txt", "file.txt", 1024, DateTime.Now);
        Assert.DoesNotContain("...", entry.ShortenedPath);
    }

    // ═══════════════════════════════════════════════════
    // FileSizeDisplay の受け渡し
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// FileSizeDisplay は FileSize を <see cref="Lhamiel.Util.DiskSpaceChecker.FormatSize"/> へそのまま渡した結果であること。
    /// 書式そのものの境界値（0 / 1023 / 1024 / MB-GB 境界 / long.MaxValue）は
    /// DiskSpaceCheckerAdversarialTests の FormatSize_* で検証する。ここでは代表サイズで受け渡しだけを確認する。
    /// </summary>
    [Fact]
    public void FileSizeDisplay_FileSizeをFormatSizeへ受け渡す()
    {
        foreach (var size in new[] { 0L, 1023L, 1024L, 1024L * 1024 * 1024 - 1, 1024L * 1024 * 1024, long.MaxValue })
        {
            var entry = new FileConflictEntry("a", "a", size, DateTime.Now);
            Assert.Equal(Lhamiel.Util.DiskSpaceChecker.FormatSize(size), entry.FileSizeDisplay);
        }

        // 受け渡し先が実際に書式化していること（恒等変換や空文字ではないこと）も固定する
        Assert.Equal("1.0 KB", new FileConflictEntry("a", "a", 1023, DateTime.Now).FileSizeDisplay);
    }

    // ═══════════════════════════════════════════════════
    // 🎭 型パンチテスト
    // ═══════════════════════════════════════════════════

    [Fact]
    public void ParentFolderName_絵文字フォルダ()
    {
        var entry = new FileConflictEntry(@"C:\📁フォルダ\file.txt", "file.txt", 100, DateTime.Now);
        Assert.Equal("📁フォルダ", entry.ParentFolderName);
    }

    [Fact]
    public void ParentFolderName_Windows予約名フォルダ()
    {
        var entry = new FileConflictEntry(@"C:\CON\file.txt", "file.txt", 100, DateTime.Now);
        Assert.Equal("CON", entry.ParentFolderName);
    }

    [Fact]
    public void ParentFolderName_UNCパス_フォールバック()
    {
        var entry = new FileConflictEntry(@"\\server\share\file.txt", "file.txt", 100, DateTime.Now);
        Assert.Equal(@"\\server\share", entry.ParentFolderName);
    }
}
