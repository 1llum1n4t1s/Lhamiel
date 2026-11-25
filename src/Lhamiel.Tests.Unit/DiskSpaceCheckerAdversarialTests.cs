using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// DiskSpaceChecker の嫌がらせテスト
/// </summary>
public class DiskSpaceCheckerAdversarialTests
{
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 🗡️ 境界値テスト
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Fact]
    public void GetAvailableSpace_空文字パス_安全側のゼロを返す()
    {
        Assert.Equal(0, DiskSpaceChecker.GetAvailableSpace(""));
    }

    [Fact]
    public void GetAvailableSpace_存在しないドライブ_安全側のゼロを返す()
    {
        // Z: 固定だと実在する環境で失敗するため、未使用のドライブ文字を動的に選ぶ。
        var inUse = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        char? unused = null;
        for (var c = 'Z'; c >= 'D'; c--)
        {
            if (!inUse.Contains(c))
            {
                unused = c;
                break;
            }
        }
        Assert.SkipWhen(unused is null, "未使用のドライブ文字が無い");

        var result = DiskSpaceChecker.GetAvailableSpace($"{unused}:\\NonExistent\\Path");
        Assert.Equal(0, result);
    }

    [Fact]
    public void GetTotalFileSize_空リスト_ゼロを返す()
    {
        Assert.Equal(0, DiskSpaceChecker.GetTotalFileSize([]));
    }

    [Fact]
    public void GetTotalFileSize_既存ファイルと消失ファイルの混在_サイズ不明を返す()
    {
        var existing = Path.GetTempFileName();
        try
        {
            Assert.Equal(-1, DiskSpaceChecker.GetTotalFileSize(
                [existing, Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt")]));
        }
        finally
        {
            File.Delete(existing);
        }
    }

    [Fact]
    public void GetTotalFileSize_大きい既存ファイルと消失ファイルの混在_既知サイズを維持する()
    {
        var existing = Path.GetTempFileName();
        const long knownSize = 101L * 1024 * 1024;
        try
        {
            using (var stream = new FileStream(existing, FileMode.Open, FileAccess.Write))
                stream.SetLength(knownSize);
            Assert.Equal(knownSize, DiskSpaceChecker.GetTotalFileSize(
                [existing, Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt")]));
        }
        finally
        {
            File.Delete(existing);
        }
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 🔀 状態遷移テスト
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Fact]
    public async Task EnsureDiskSpaceAsync_必要量ゼロ_即座にtrue()
    {
        Assert.True(await DiskSpaceChecker.EnsureDiskSpaceAsync("C:\\", 0, null, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureDiskSpaceAsync_サイズ不明_空き容量があれば続行()
    {
        Assert.True(await DiskSpaceChecker.EnsureDiskSpaceAsync("C:\\", -1, null, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureDiskSpaceAsync_ParentWindowNull_容量不足なら停止()
    {
        Assert.False(await DiskSpaceChecker.EnsureDiskSpaceAsync("C:\\", long.MaxValue, null, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureDiskSpaceAsync_サイズ不明かつ空き容量取得不能なら停止()
    {
        Assert.False(await DiskSpaceChecker.EnsureDiskSpaceAsync("", -1, null, CancellationToken.None));
    }

    [Fact]
    public void GetArchiveUncompressedSize_取得失敗は空アーカイブと区別する()
    {
        using var temp = TestDirectory.Create("UnknownArchiveSize");
        var missing = Path.Combine(temp.Path, "missing.7z");
        Assert.Equal(-1, DiskSpaceChecker.GetArchiveUncompressedSize(missing));
        var empty = Path.Combine(temp.Path, "empty.zip");
        using (var archive = new System.IO.Compression.ZipArchive(
                   File.Create(empty), System.IO.Compression.ZipArchiveMode.Create)) { }
        Assert.Equal(0, DiskSpaceChecker.GetArchiveUncompressedSize(empty));
    }

    [Fact]
    public async Task ExtractArchiveAsync_サイズ不明かつ出力容量不明なら展開前に停止()
    {
        using var temp = TestDirectory.Create("UnknownSizePreflight");
        // 書庫を開く処理へ進めば FileNotFoundException になるため、事前中止を区別できる。
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ArchiveExtractor.ExtractArchiveAsync(Path.Combine(temp.Path, "missing.7z"), ""));
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 🌪️ 環境異常テスト
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Fact]
    public void GetTotalFileSize_ディレクトリ指定_再帰合算()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"DiskTest_{Guid.NewGuid():N}");
        var subDir = Path.Combine(tempDir, "sub");
        Directory.CreateDirectory(subDir);
        try
        {
            File.WriteAllBytes(Path.Combine(tempDir, "root.bin"), new byte[100]);
            File.WriteAllBytes(Path.Combine(subDir, "child.bin"), new byte[200]);
            Assert.Equal(300, DiskSpaceChecker.GetTotalFileSize([tempDir]));
        }
        finally { Directory.Delete(tempDir, true); }
    }

    [Fact]
    public void GetTotalFileSize_ファイルとディレクトリ混在_正しい合計()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"DiskTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var file1 = Path.Combine(tempDir, "standalone.txt");
            File.WriteAllBytes(file1, new byte[500]);
            var innerDir = Path.Combine(tempDir, "inner");
            Directory.CreateDirectory(innerDir);
            File.WriteAllBytes(Path.Combine(innerDir, "nested.txt"), new byte[300]);
            Assert.Equal(800, DiskSpaceChecker.GetTotalFileSize([file1, innerDir]));
        }
        finally { Directory.Delete(tempDir, true); }
    }
}
