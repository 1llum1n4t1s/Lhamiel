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
    public void GetAvailableSpace_空文字パス_MaxValueを返す()
    {
        Assert.Equal(long.MaxValue, DiskSpaceChecker.GetAvailableSpace(""));
    }

    [Fact]
    public void GetAvailableSpace_存在しないドライブ_MaxValueか0()
    {
        var result = DiskSpaceChecker.GetAvailableSpace("Z:\\NonExistent\\Path");
        Assert.True(result == long.MaxValue || result == 0);
    }

    [Fact]
    public void FormatSize_ゼロ_KBで表示()
    {
        Assert.Equal("0.0 KB", DiskSpaceChecker.FormatSize(0));
    }

    [Fact]
    public void FormatSize_LongMaxValue_GBで表示()
    {
        Assert.Contains("GB", DiskSpaceChecker.FormatSize(long.MaxValue));
    }

    [Fact]
    public void FormatSize_KB_MB境界()
    {
        Assert.Contains("KB", DiskSpaceChecker.FormatSize(1024 * 1024 - 1));
        Assert.Contains("MB", DiskSpaceChecker.FormatSize(1024 * 1024));
    }

    [Fact]
    public void FormatSize_MB_GB境界()
    {
        Assert.Contains("MB", DiskSpaceChecker.FormatSize(1024L * 1024 * 1024 - 1));
        Assert.Contains("GB", DiskSpaceChecker.FormatSize(1024L * 1024 * 1024));
    }

    [Fact]
    public void GetTotalFileSize_空リスト_ゼロを返す()
    {
        Assert.Equal(0, DiskSpaceChecker.GetTotalFileSize([]));
    }

    [Fact]
    public void GetTotalFileSize_存在しないパス_ゼロを返す()
    {
        Assert.Equal(0, DiskSpaceChecker.GetTotalFileSize([@"C:\NonExistent\File.txt"]));
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // ⚡ 並行性テスト
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Fact]
    public async Task GetAvailableSpace_並行呼び出し_全て正の値()
    {
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => DiskSpaceChecker.GetAvailableSpace("C:\\")))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.True(r > 0));
    }

    [Fact]
    public async Task GetTotalFileSize_並行呼び出し_全て正の値()
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "test content");
        try
        {
            var tasks = Enumerable.Range(0, 20)
                .Select(_ => Task.Run(() => DiskSpaceChecker.GetTotalFileSize([tempFile])))
                .ToArray();
            var results = await Task.WhenAll(tasks);
            Assert.All(results, r => Assert.True(r > 0));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void StartPeriodicCheck_即Dispose_クラッシュしない()
    {
        var cts = new CancellationTokenSource();
        var disposable = DiskSpaceChecker.StartPeriodicCheck("C:\\", 1024, null, cts);
        disposable.Dispose();
        cts.Dispose();
    }

    [Fact]
    public void StartPeriodicCheck_二重Dispose_安全()
    {
        var cts = new CancellationTokenSource();
        var disposable = DiskSpaceChecker.StartPeriodicCheck("C:\\", 1024, null, cts);
        disposable.Dispose();
        var ex = Record.Exception(() => disposable.Dispose());
        Assert.True(ex is null or ObjectDisposedException);
        cts.Dispose();
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 🔀 状態遷移テスト
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Fact]
    public void StartPeriodicCheck_キャンセル済みCTS_クラッシュしない()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var disposable = DiskSpaceChecker.StartPeriodicCheck("C:\\", 1024, null, cts);
        disposable.Dispose();
        cts.Dispose();
    }

    [Fact]
    public async Task EnsureDiskSpaceAsync_必要量ゼロ_即座にtrue()
    {
        Assert.True(await DiskSpaceChecker.EnsureDiskSpaceAsync("C:\\", 0, null, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureDiskSpaceAsync_負の必要量_即座にtrue()
    {
        Assert.True(await DiskSpaceChecker.EnsureDiskSpaceAsync("C:\\", -1, null, CancellationToken.None));
    }

    [Fact]
    public async Task EnsureDiskSpaceAsync_ParentWindowNull_trueを返す()
    {
        Assert.True(await DiskSpaceChecker.EnsureDiskSpaceAsync("C:\\", long.MaxValue, null, CancellationToken.None));
    }

    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    // 🌪️ 環境異常テスト
    // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

    [Fact]
    public void GetTotalFileSize_実ファイル_正しいサイズ()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"DiskTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllBytes(Path.Combine(tempDir, "test1.txt"), new byte[1024]);
            File.WriteAllBytes(Path.Combine(tempDir, "test2.txt"), new byte[2048]);
            Assert.Equal(3072, DiskSpaceChecker.GetTotalFileSize(
                [Path.Combine(tempDir, "test1.txt"), Path.Combine(tempDir, "test2.txt")]));
        }
        finally { Directory.Delete(tempDir, true); }
    }

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
