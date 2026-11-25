using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// ProgressThrottler のテスト — スロットリング間隔の基本動作に加え、
/// 境界値・状態遷移の嫌がらせケースを 1 ファイルに集約している。
/// <para>
/// 旧 ProgressThrottlerTests の「同じ値の 2 回目は報告しない」は
/// <see cref="ShouldReport_DecreasedPercentage_NotReported"/>、「0〜100% の連続報告」は
/// <see cref="ShouldReport_ZeroIntervalMs_AllIntermediateValuesReported"/> が同じ入力を検証する。
/// </para>
/// </summary>
public class ProgressThrottlerAdversarialTests
{
    /// <summary>
    /// 報告間隔内の連続更新は、初回だけ報告して以降を抑止する
    /// </summary>
    [Fact]
    public void ShouldReport_RapidUpdates_ThrottlesCorrectly()
    {
        var throttler = new ProgressThrottler(500); // 500ms間隔
        Assert.True(throttler.ShouldReport(10)); // 最初は常にtrue

        // すぐに次の値を送る → スロットリングされるべき
        Assert.False(throttler.ShouldReport(11));
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
    /// コンストラクタに 0ms を渡す → 0% から 100% までの全値が報告される（スロットリングなし）
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

        // 連続報告の終端として 100% も報告される
        // （旧 ProgressThrottlerTests.ShouldReport_FullProgressSequence_MonotonicIncrease の検証範囲）
        Assert.True(throttler.ShouldReport(100));

        // 初回に 0% を経由しない場合も、最初の中間値から連続で報告される
        // （旧 ProgressThrottlerTests.ShouldReport_ZeroInterval_NoThrottling の入力）
        var withoutZero = new ProgressThrottler(reportIntervalMs: 0);
        Assert.True(withoutZero.ShouldReport(1));
        Assert.True(withoutZero.ShouldReport(2));
        Assert.True(withoutZero.ShouldReport(3));
    }

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
        Assert.False(throttler.ShouldReport(40));
        Assert.False(throttler.ShouldReport(49));
        // 元の値と同じも拒否
        Assert.False(throttler.ShouldReport(50));
        // 超えれば報告
        Assert.True(throttler.ShouldReport(51));
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
    /// （100% の後の 0% が報告される＝リセット的動作もここで確認する）
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
}
