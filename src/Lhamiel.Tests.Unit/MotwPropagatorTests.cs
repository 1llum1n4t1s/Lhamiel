using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// MotwPropagator のユニットテスト。
/// Zone.Identifier ADS の読み書き・伝播ロジックを検証。
/// </summary>
public class MotwPropagatorTests : IDisposable
{
    private readonly string _testDir;

    public MotwPropagatorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"lhamiel_motw_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public void ReadZoneIdentifier_FileWithAds_ReturnsContent()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "ADS は Windows のみ");

        var file = Path.Combine(_testDir, "test_with_ads.zip");
        File.WriteAllText(file, "dummy");

        var zoneContent = "[ZoneTransfer]\r\nZoneId=3\r\n";
        File.WriteAllText(file + ":Zone.Identifier", zoneContent);

        var result = MotwPropagator.ReadZoneIdentifier(file);
        Assert.Equal(zoneContent, result);
    }

    [Fact]
    public void PropagateToDirectory_WritesToAllFiles()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "ADS は Windows のみ");

        var subDir = Path.Combine(_testDir, "subdir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(_testDir, "file1.txt"), "a");
        File.WriteAllText(Path.Combine(_testDir, "file2.txt"), "b");
        File.WriteAllText(Path.Combine(subDir, "file3.txt"), "c");

        var zoneContent = "[ZoneTransfer]\r\nZoneId=3\r\n";
        MotwPropagator.PropagateToDirectory(_testDir, zoneContent);

        Assert.Equal(zoneContent, File.ReadAllText(Path.Combine(_testDir, "file1.txt") + ":Zone.Identifier"));
        Assert.Equal(zoneContent, File.ReadAllText(Path.Combine(_testDir, "file2.txt") + ":Zone.Identifier"));
        Assert.Equal(zoneContent, File.ReadAllText(Path.Combine(subDir, "file3.txt") + ":Zone.Identifier"));
    }
}
