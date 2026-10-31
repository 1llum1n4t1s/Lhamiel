using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

public sealed class LoggerRetentionTests
{
    // 純粋な名前・期限判定だけを検証し、Logger の初期化や実ログの削除は行わない。
    [Theory]
    [InlineData("Lhamiel_20261024.log", true)]
    [InlineData("Lhamiel_20261024_000.log", true)]
    [InlineData("Lhamiel_extraction_1234_20261024.log", true)]
    [InlineData("Lhamiel_extraction_1234_20261024_001.log", true)]
    [InlineData("Lhamiel_compression_5678_20261024.log", true)]
    [InlineData("Lhamiel_compression_5678_20261024_10.log", true)]
    [InlineData("Lhamiel_20261025.log", false)]
    [InlineData("Lhamiel_extraction_1234_20261025_001.log", false)]
    [InlineData("Lhamiel_compression_5678_20261026.log", false)]
    [InlineData("Lhamiel_emergency.log", false)]
    [InlineData("Other_20261024.log", false)]
    [InlineData("LhamielOther_20261024.log", false)]
    [InlineData("Lhamiel_20260230.log", false)]
    [InlineData("Lhamiel_2026102.log", false)]
    [InlineData("Lhamiel_20261024_backup.log", false)]
    [InlineData("Lhamiel_20261024_.log", false)]
    [InlineData("Lhamiel_20261024_001_extra.log", false)]
    [InlineData("Lhamiel_emergency_1234_20261024.log", false)]
    [InlineData("Lhamiel_extraction_0_20261024.log", false)]
    [InlineData("Lhamiel_extraction_-1234_20261024.log", false)]
    [InlineData("Lhamiel_extraction_bad_20261024.log", false)]
    [InlineData("Lhamiel_extraction_1234.log", false)]
    [InlineData("Lhamiel_compression_5678_20261024_backup.log", false)]
    [InlineData("Lhamiel_compression_5678_20261024_1_extra.log", false)]
    [InlineData("Lhamiel_extraction_1234_20261024.log.bak", false)]
    [InlineData("Lhamiel_.log", false)]
    public void ShouldDeleteLogFile_RecognizesOnlyExpiredStandardLogs(string fileName, bool expected)
    {
        Assert.Equal(expected, Logger.ShouldDeleteLogFile(fileName, "Lhamiel", new DateTime(2026, 10, 25)));
    }

    [Theory]
    [InlineData("My_App_20261024.log")]
    [InlineData("My_App_compression_1234_20261024_0.log")]
    public void ShouldDeleteLogFile_PrefixContainingUnderscores_RecognizesDate(string fileName)
    {
        Assert.True(Logger.ShouldDeleteLogFile(fileName, "My_App", new DateTime(2026, 10, 25)));
    }
}
