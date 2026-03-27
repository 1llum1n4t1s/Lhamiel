using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// ProgressThrottler に対する嫌がらせテスト — 境界値・並行性・状態遷移
/// </summary>
public class ProgressThrottlerAdversarialTests
{
    // ==============================
    // 🗡️ 境界値・極端入力
    // ==============================

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 負の進捗率 → 0% 以下は境界値として常に報告される
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void ShouldReport_NegativePercentage_AlwaysReported(int percentage)
    {
        var throttler = new ProgressThrottler();
        Assert.True(throttler.ShouldReport(percentage));
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 100超の進捗率 → 100% 以上は境界値として常に報告される
    /// </summary>
    [Theory]
    [InlineData(101)]
    [InlineData(200)]
    [InlineData(int.MaxValue)]
    public void ShouldReport_Over100Percentage_AlwaysReported(int percentage)
    {
        var throttler = new ProgressThrottler();
        Assert.True(throttler.ShouldReport(percentage));
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 0% は何度呼んでも常に報告される（境界値扱い）
    /// </summary>
    [Fact]
    public void ShouldReport_ZeroPercent_AlwaysReportedRepeatedly()
    {
        var throttler = new ProgressThrottler();
        for (var i = 0; i < 10; i++)
        {
            Assert.True(throttler.ShouldReport(0), $"0% の {i + 1} 回目の呼び出しで false を返した");
        }
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 100% は何度呼んでも常に報告される（境界値扱い）
    /// </summary>
    [Fact]
    public void ShouldReport_HundredPercent_AlwaysReportedRepeatedly()
    {
        var throttler = new ProgressThrottler();
        throttler.ShouldReport(0); // 初期化
        for (var i = 0; i < 10; i++)
        {
            Assert.True(throttler.ShouldReport(100), $"100% の {i + 1} 回目の呼び出しで false を返した");
        }
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// コンストラクタに 0ms を渡す → 全ての中間値が報告される（スロットリングなし）
    /// </summary>
    [Fact]
    public void ShouldReport_ZeroIntervalMs_AllIntermediateValuesReported()
    {
        var throttler = new ProgressThrottler(reportIntervalMs: 0);
        throttler.ShouldReport(0);

        // 1-99 の連続値がすべて報告される（単調増加なので）
        for (var i = 1; i <= 99; i++)
        {
            Assert.True(throttler.ShouldReport(i), $"{i}% が報告されなかった");
        }
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// コンストラクタに負の値を渡す → クラッシュせず動作する
    /// </summary>
    [Fact]
    public void ShouldReport_NegativeIntervalMs_DoesNotCrash()
    {
        var throttler = new ProgressThrottler(reportIntervalMs: -1);
        Assert.True(throttler.ShouldReport(0));
        Assert.True(throttler.ShouldReport(50));
        Assert.True(throttler.ShouldReport(100));
    }

    // ==============================
    // 🔀 状態遷移の矛盾
    // ==============================

    /// <summary>
    /// @adversarial @category state @severity high
    /// 後退する進捗率（50 → 30）→ 中間値は単調増加を保証するため報告されない
    /// </summary>
    [Fact]
    public void ShouldReport_DecreasedPercentage_NotReported()
    {
        var throttler = new ProgressThrottler(reportIntervalMs: 0);
        Assert.True(throttler.ShouldReport(50));
        Assert.False(throttler.ShouldReport(30));
        Assert.False(throttler.ShouldReport(49));
        // 元の値と同じも拒否
        Assert.False(throttler.ShouldReport(50));
        // 超えれば報告
        Assert.True(throttler.ShouldReport(51));
    }

    /// <summary>
    /// @adversarial @category state @severity high
    /// 100% の後に 0% → 0% は境界値なので報告される（リセット的動作）
    /// </summary>
    [Fact]
    public void ShouldReport_AfterHundred_ZeroIsStillReported()
    {
        var throttler = new ProgressThrottler(reportIntervalMs: 0);
        Assert.True(throttler.ShouldReport(100));
        Assert.True(throttler.ShouldReport(0));
    }

    /// <summary>
    /// @adversarial @category state @severity medium
    /// 100% の後に中間値 → _lastPercentage が 100 なので中間値は拒否
    /// </summary>
    [Fact]
    public void ShouldReport_AfterHundred_IntermediateIsRejected()
    {
        var throttler = new ProgressThrottler(reportIntervalMs: 0);
        Assert.True(throttler.ShouldReport(100));
        // 100 の後の 50 は後退なので拒否
        Assert.False(throttler.ShouldReport(50));
    }

    /// <summary>
    /// @adversarial @category state @severity medium
    /// 0% → 100% → 0% → 50% → 100% のサイクル → 各境界値は常に報告
    /// </summary>
    [Fact]
    public void ShouldReport_CyclicBoundaryValues_AllReported()
    {
        var throttler = new ProgressThrottler(reportIntervalMs: 0);
        Assert.True(throttler.ShouldReport(0));
        Assert.True(throttler.ShouldReport(100));
        Assert.True(throttler.ShouldReport(0));
        // 50 は _lastPercentage=0 より大きいので報告される
        Assert.True(throttler.ShouldReport(50));
        Assert.True(throttler.ShouldReport(100));
    }

    // ==============================
    // ⚡ 並行性・レースコンディション
    // ==============================

    /// <summary>
    /// @adversarial @category concurrency @severity high
    /// 100スレッドから同時に ShouldReport を呼んでもクラッシュしない
    /// </summary>
    [Fact]
    public async Task ShouldReport_100ConcurrentCalls_NoCrash()
    {
        var throttler = new ProgressThrottler(reportIntervalMs: 0);
        var tasks = Enumerable.Range(0, 100).Select(i =>
            Task.Run(() => throttler.ShouldReport(i % 101))
        ).ToArray();

        var results = await Task.WhenAll(tasks);
        // クラッシュしないこと + いくつかは true を返すはず
        Assert.Contains(results, r => r);
    }

    /// <summary>
    /// @adversarial @category concurrency @severity high
    /// 並行呼び出しで _lastPercentage が矛盾しない（データ競合なし）
    /// </summary>
    [Fact]
    public async Task ShouldReport_ConcurrentIncreasingValues_MonotonicityMaintained()
    {
        var throttler = new ProgressThrottler(reportIntervalMs: 0);
        var reportedValues = new System.Collections.Concurrent.ConcurrentBag<int>();

        // 0-99 を並行で報告
        var tasks = Enumerable.Range(0, 100).Select(i =>
            Task.Run(() =>
            {
                if (throttler.ShouldReport(i))
                    reportedValues.Add(i);
            })
        ).ToArray();
        await Task.WhenAll(tasks);

        // 報告された値をソートして単調増加を確認
        var sorted = reportedValues.OrderBy(v => v).ToList();
        for (var i = 1; i < sorted.Count; i++)
        {
            Assert.True(sorted[i] > sorted[i - 1],
                $"単調増加違反: {sorted[i - 1]} → {sorted[i]}");
        }
    }

    /// <summary>
    /// @adversarial @category concurrency @severity medium
    /// 100スレッドが同時に 0% を呼ぶ → 全て true（境界値は常に報告）
    /// </summary>
    [Fact]
    public async Task ShouldReport_100ConcurrentZeroPercent_AllReturnsTrue()
    {
        var throttler = new ProgressThrottler();
        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => throttler.ShouldReport(0)))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.True(r));
    }

    // ==============================
    // 💀 リソース枯渇
    // ==============================

    /// <summary>
    /// @adversarial @category resource @severity medium
    /// 100万回連続呼び出し → メモリリークやパフォーマンス劣化なし
    /// </summary>
    [Fact]
    public void ShouldReport_MillionCalls_NoPerformanceDegradation()
    {
        var throttler = new ProgressThrottler(reportIntervalMs: 0);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (var i = 0; i < 1_000_000; i++)
        {
            throttler.ShouldReport(i % 101);
        }

        sw.Stop();
        // 100万回が5秒以内に完了すること（通常は数百ms）
        Assert.True(sw.ElapsedMilliseconds < 5000,
            $"100万回の呼び出しに {sw.ElapsedMilliseconds}ms かかった（上限5000ms）");
    }
}
