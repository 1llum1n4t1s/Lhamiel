using Lhamiel.Models;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// FileConflictInfo モデルの嫌がらせテスト 😈
/// FileConflictEntry / FileConflictGroup の境界値・型パンチ・環境異常を攻める
/// </summary>
public class FileConflictInfoAdversarialTests
{
    // ═══════════════════════════════════════════════════
    // 🗡️ カテゴリ1: 境界値・極端入力（Boundary Assault）
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// @description ルートパス（C:\）で ParentFolderName が空文字を返す既知バグ
    /// @expected 空文字にならず、何らかのフォールバック値を返す
    /// </summary>
    [Fact]
    public void ParentFolderName_ルートパス_空文字を返す_バグ検出()
    {
        var entry = new FileConflictEntry(@"C:\file.txt", "file.txt", 1024, DateTime.Now);

        var result = entry.ParentFolderName;

        // 修正済み: ルートパスではディレクトリパスそのもの（C:\）をフォールバック
        Assert.Equal(@"C:\", result);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// @description ルートパス（D:\）でShortenedPathがどうなるか
    /// </summary>
    [Fact]
    public void ShortenedPath_ルートパス_クラッシュしない()
    {
        var entry = new FileConflictEntry(@"D:\readme.txt", "readme.txt", 512, DateTime.Now);

        var result = entry.ShortenedPath;

        // クラッシュしないこと
        Assert.NotNull(result);
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// @description 空文字列 FullPath で各プロパティがクラッシュしないこと
    /// </summary>
    [Fact]
    public void AllProperties_空FullPath_クラッシュしない()
    {
        var entry = new FileConflictEntry("", "", 0, DateTime.MinValue);

        // どれもクラッシュしないこと
        var parent = entry.ParentFolderName;
        var shortened = entry.ShortenedPath;
        var sizeDisplay = entry.FileSizeDisplay;

        Assert.NotNull(parent);
        Assert.NotNull(shortened);
        Assert.NotNull(sizeDisplay);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// @description 深いネスト（10階層）でShortenedPathが末尾2階層のみ返すこと
    /// </summary>
    [Fact]
    public void ShortenedPath_深いネスト_末尾2階層で省略()
    {
        var entry = new FileConflictEntry(
            @"C:\a\b\c\d\e\f\g\h\i\j\file.txt", "file.txt", 1024, DateTime.Now);

        var result = entry.ShortenedPath;

        Assert.Contains(@"i\j", result);
        Assert.StartsWith(@"...\", result);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// @description 2階層以下のパスではそのまま返すこと
    /// </summary>
    [Fact]
    public void ShortenedPath_浅いパス_省略なし()
    {
        var entry = new FileConflictEntry(@"C:\docs\file.txt", "file.txt", 1024, DateTime.Now);

        var result = entry.ShortenedPath;

        Assert.DoesNotContain("...", result);
    }

    // ═══════════════════════════════════════════════════
    // 🗡️ FileSizeDisplay 境界値テスト
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// @adversarial @category boundary @severity low
    /// @description ファイルサイズ 0 B の表示
    /// </summary>
    [Fact]
    public void FileSizeDisplay_ゼロバイト()
    {
        var entry = new FileConflictEntry("test.txt", "test.txt", 0, DateTime.Now);
        Assert.Equal("0.0 KB", entry.FileSizeDisplay);
    }

    /// <summary>
    /// @adversarial @category boundary @severity low
    /// @description ファイルサイズ境界値 1023 B → 1024 B
    /// </summary>
    [Fact]
    public void FileSizeDisplay_KB境界()
    {
        var entry1023 = new FileConflictEntry("a", "a", 1023, DateTime.Now);
        var entry1024 = new FileConflictEntry("a", "a", 1024, DateTime.Now);

        Assert.Equal("1.0 KB", entry1023.FileSizeDisplay);
        Assert.Equal("1.0 KB", entry1024.FileSizeDisplay);
    }

    /// <summary>
    /// @adversarial @category boundary @severity low
    /// @description ファイルサイズ境界値 MB → GB
    /// </summary>
    [Fact]
    public void FileSizeDisplay_GB境界()
    {
        var entryMB = new FileConflictEntry("a", "a", 1024L * 1024 * 1024 - 1, DateTime.Now);
        var entryGB = new FileConflictEntry("a", "a", 1024L * 1024 * 1024, DateTime.Now);

        Assert.Contains("MB", entryMB.FileSizeDisplay);
        Assert.Contains("GB", entryGB.FileSizeDisplay);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// @description 負のファイルサイズ（通常ありえないが防御テスト）
    /// </summary>
    [Fact]
    public void FileSizeDisplay_負の値_クラッシュしない()
    {
        var entry = new FileConflictEntry("a", "a", -1, DateTime.Now);

        // クラッシュしないこと
        var result = entry.FileSizeDisplay;
        Assert.NotNull(result);
    }

    /// <summary>
    /// @adversarial @category boundary @severity low
    /// @description long.MaxValue のファイルサイズ表示
    /// </summary>
    [Fact]
    public void FileSizeDisplay_MaxValue_クラッシュしない()
    {
        var entry = new FileConflictEntry("a", "a", long.MaxValue, DateTime.Now);

        var result = entry.FileSizeDisplay;
        Assert.NotNull(result);
        Assert.Contains("GB", result);
    }

    // ═══════════════════════════════════════════════════
    // 🎭 カテゴリ5: 型パンチ・プロトコル違反（Type Punching）
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// @adversarial @category type @severity high
    /// @description Unicode制御文字を含むパス
    /// </summary>
    [Fact]
    public void ParentFolderName_Unicode制御文字_クラッシュしない()
    {
        var entry = new FileConflictEntry(
            "C:\\folder\u200B\\file.txt", "file.txt", 100, DateTime.Now);

        var result = entry.ParentFolderName;
        Assert.NotNull(result);
    }

    /// <summary>
    /// @adversarial @category type @severity high
    /// @description RTL制御文字を含むパス（セキュリティリスク）
    /// </summary>
    [Fact]
    public void ParentFolderName_RTL制御文字_クラッシュしない()
    {
        var entry = new FileConflictEntry(
            "C:\\folder\u202E\\file.txt", "file.txt", 100, DateTime.Now);

        var result = entry.ParentFolderName;
        Assert.NotNull(result);
    }

    /// <summary>
    /// @adversarial @category type @severity medium
    /// @description 絵文字フォルダ名
    /// </summary>
    [Fact]
    public void ParentFolderName_絵文字フォルダ()
    {
        var entry = new FileConflictEntry(
            @"C:\📁フォルダ\file.txt", "file.txt", 100, DateTime.Now);

        Assert.Equal("📁フォルダ", entry.ParentFolderName);
    }

    /// <summary>
    /// @adversarial @category type @severity medium
    /// @description Windows予約デバイス名がパスに含まれる
    /// </summary>
    [Fact]
    public void ParentFolderName_Windows予約名フォルダ()
    {
        var entry = new FileConflictEntry(
            @"C:\CON\file.txt", "file.txt", 100, DateTime.Now);

        Assert.Equal("CON", entry.ParentFolderName);
    }

    /// <summary>
    /// @adversarial @category type @severity medium
    /// @description UNCパスの ParentFolderName
    /// </summary>
    [Fact]
    public void ParentFolderName_UNCパス()
    {
        var entry = new FileConflictEntry(
            @"\\server\share\file.txt", "file.txt", 100, DateTime.Now);

        var result = entry.ParentFolderName;
        Assert.NotNull(result);
        // 修正済み: UNCルートではディレクトリパスそのものをフォールバック
        Assert.Equal(@"\\server\share", result);
    }

    // ═══════════════════════════════════════════════════
    // 🌪️ カテゴリ6: 環境異常（Environmental Chaos）
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// @adversarial @category chaos @severity medium
    /// @description DateTime.MinValue の LastModified 表示
    /// </summary>
    [Fact]
    public void Entry_MinDateTime_クラッシュしない()
    {
        var entry = new FileConflictEntry("test.txt", "test.txt", 0, DateTime.MinValue);

        // record の ToString がクラッシュしないこと
        var str = entry.ToString();
        Assert.NotNull(str);
    }

    /// <summary>
    /// @adversarial @category chaos @severity medium
    /// @description DateTime.MaxValue の LastModified
    /// </summary>
    [Fact]
    public void Entry_MaxDateTime_クラッシュしない()
    {
        var entry = new FileConflictEntry("test.txt", "test.txt", 0, DateTime.MaxValue);

        var str = entry.ToString();
        Assert.NotNull(str);
    }

    // ═══════════════════════════════════════════════════
    // ⚡ カテゴリ2: 並行性（Concurrency）
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// @adversarial @category concurrency @severity medium
    /// @description 複数スレッドから同じ FileConflictEntry の読み取りアクセス
    /// </summary>
    [Fact]
    public async Task Entry_並行読み取り_スレッドセーフ()
    {
        var entry = new FileConflictEntry(
            @"C:\test\folder\file.txt", "file.txt", 1024 * 1024, DateTime.Now);

        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            var p = entry.ParentFolderName;
            var s = entry.ShortenedPath;
            var f = entry.FileSizeDisplay;
        }));

        await Task.WhenAll(tasks); // クラッシュしないこと
    }

    // ═══════════════════════════════════════════════════
    // 🔀 カテゴリ4: FileConflictGroup 状態遷移テスト
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// @adversarial @category state @severity medium
    /// @description 空の Entries リストを持つ FileConflictGroup
    /// </summary>
    [Fact]
    public void FileConflictGroup_空Entries_クラッシュしない()
    {
        var group = new FileConflictGroup
        {
            ConflictingName = "test.txt",
            Entries = []
        };

        Assert.Empty(group.Entries);
        Assert.Equal("test.txt", group.ConflictingName);
    }

    /// <summary>
    /// @adversarial @category state @severity medium
    /// @description 1件のみの Entries（衝突してないのにグループ化された場合）
    /// </summary>
    [Fact]
    public void FileConflictGroup_1件のみ_クラッシュしない()
    {
        var group = new FileConflictGroup
        {
            ConflictingName = "test.txt",
            Entries =
            [
                new FileConflictEntry(@"C:\a\test.txt", "test.txt", 100, DateTime.Now)
            ]
        };

        Assert.Single(group.Entries);
    }

    /// <summary>
    /// @adversarial @category state @severity low
    /// @description ConflictingName が空文字列
    /// </summary>
    [Fact]
    public void FileConflictGroup_空ConflictingName_クラッシュしない()
    {
        var group = new FileConflictGroup
        {
            ConflictingName = "",
            Entries =
            [
                new FileConflictEntry("a", "a", 0, DateTime.Now),
                new FileConflictEntry("b", "b", 0, DateTime.Now)
            ]
        };

        Assert.Equal("", group.ConflictingName);
        Assert.Equal(2, group.Entries.Count);
    }
}
