using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// ProgressThrottler のユニットテスト（実装を信用しないエッジケース重視）
/// </summary>
public class ProgressThrottlerTests
{
    // === 境界値 ===

    [Fact]
    public void ShouldReport_ZeroPercent_AlwaysReportsTrue()
    {
        var throttler = new ProgressThrottler(1000);
        Assert.True(throttler.ShouldReport(0));
    }

    [Fact]
    public void ShouldReport_HundredPercent_AlwaysReportsTrue()
    {
        var throttler = new ProgressThrottler(1000);
        Assert.True(throttler.ShouldReport(100));
    }

    [Fact]
    public void ShouldReport_NegativePercent_AlwaysReportsTrue()
    {
        // 負の値は境界値として扱われるべき
        var throttler = new ProgressThrottler(1000);
        Assert.True(throttler.ShouldReport(-1));
    }

    [Fact]
    public void ShouldReport_Over100Percent_AlwaysReportsTrue()
    {
        // 100超の値は境界値として扱われるべき
        var throttler = new ProgressThrottler(1000);
        Assert.True(throttler.ShouldReport(101));
    }

    // === 単調増加保証 ===

    [Fact]
    public void ShouldReport_SamePercentageTwice_SecondReturnsFalse()
    {
        var throttler = new ProgressThrottler(0); // スロットリング無効
        throttler.ShouldReport(50);
        Assert.False(throttler.ShouldReport(50));
    }

    [Fact]
    public void ShouldReport_DecreasingPercentage_ReturnsFalse()
    {
        var throttler = new ProgressThrottler(0);
        throttler.ShouldReport(50);
        Assert.False(throttler.ShouldReport(40));
    }

    [Fact]
    public void ShouldReport_IncreasingPercentage_ReturnsTrue()
    {
        var throttler = new ProgressThrottler(0); // スロットリング無効
        throttler.ShouldReport(10);
        Assert.True(throttler.ShouldReport(20));
    }

    // === スロットリング ===

    [Fact]
    public void ShouldReport_RapidUpdates_ThrottlesCorrectly()
    {
        var throttler = new ProgressThrottler(500); // 500ms間隔
        Assert.True(throttler.ShouldReport(10)); // 最初は常にtrue

        // すぐに次の値を送る → スロットリングされるべき
        Assert.False(throttler.ShouldReport(11));
    }

    [Fact]
    public void ShouldReport_ZeroInterval_NoThrottling()
    {
        var throttler = new ProgressThrottler(0);
        Assert.True(throttler.ShouldReport(1));
        Assert.True(throttler.ShouldReport(2));
        Assert.True(throttler.ShouldReport(3));
    }

    // === 連続呼び出しパターン ===

    [Fact]
    public void ShouldReport_FullProgressSequence_MonotonicIncrease()
    {
        var throttler = new ProgressThrottler(0);
        var reportedValues = new List<int>();

        for (var i = 0; i <= 100; i++)
        {
            if (throttler.ShouldReport(i))
                reportedValues.Add(i);
        }

        // 報告された値が単調増加であること
        for (var i = 1; i < reportedValues.Count; i++)
            Assert.True(reportedValues[i] > reportedValues[i - 1]);

        // 0% と 100% は必ず含まれること
        Assert.Contains(0, reportedValues);
        Assert.Contains(100, reportedValues);
    }

    [Fact]
    public void ShouldReport_BoundaryAfterMiddle_AlwaysReports()
    {
        var throttler = new ProgressThrottler(0);
        throttler.ShouldReport(50);
        // 100% は前の値に関わらず常に報告される
        Assert.True(throttler.ShouldReport(100));
    }

    [Fact]
    public void ShouldReport_ZeroAfterHundred_AlwaysReports()
    {
        var throttler = new ProgressThrottler(0);
        throttler.ShouldReport(100);
        // 0% は前の値に関わらず常に報告される（リセットシナリオ）
        Assert.True(throttler.ShouldReport(0));
    }

    // === スレッドセーフティ ===

    [Fact]
    public async Task ShouldReport_ConcurrentCalls_NeverThrows()
    {
        var throttler = new ProgressThrottler(0);
        var tasks = Enumerable.Range(0, 100).Select(i =>
            Task.Run(() => throttler.ShouldReport(i % 101))
        );
        // 例外なく完了すること
        await Task.WhenAll(tasks);
    }
}
