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
    // FileSizeDisplay 境界値テスト
    // ═══════════════════════════════════════════════════

    [Fact]
    public void FileSizeDisplay_ゼロバイト()
    {
        var entry = new FileConflictEntry("test.txt", "test.txt", 0, DateTime.Now);
        Assert.Equal("0.0 KB", entry.FileSizeDisplay);
    }

    [Fact]
    public void FileSizeDisplay_KB境界()
    {
        var entry1023 = new FileConflictEntry("a", "a", 1023, DateTime.Now);
        var entry1024 = new FileConflictEntry("a", "a", 1024, DateTime.Now);
        Assert.Equal("1.0 KB", entry1023.FileSizeDisplay);
        Assert.Equal("1.0 KB", entry1024.FileSizeDisplay);
    }

    [Fact]
    public void FileSizeDisplay_GB境界()
    {
        var entryMB = new FileConflictEntry("a", "a", 1024L * 1024 * 1024 - 1, DateTime.Now);
        var entryGB = new FileConflictEntry("a", "a", 1024L * 1024 * 1024, DateTime.Now);
        Assert.Contains("MB", entryMB.FileSizeDisplay);
        Assert.Contains("GB", entryGB.FileSizeDisplay);
    }

    [Fact]
    public void FileSizeDisplay_MaxValue_GBで表示()
    {
        var entry = new FileConflictEntry("a", "a", long.MaxValue, DateTime.Now);
        Assert.Contains("GB", entry.FileSizeDisplay);
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
