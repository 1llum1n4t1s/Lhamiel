using System.Collections.Concurrent;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// SettingsManager のシングルトン状態を触る MutateAndSave のテスト。
/// 保存先は TestEnvironmentIsolation が使い捨てフォルダーへ差し替えているが、
/// シングルトンの状態を共有するため [Collection("Sequential")] で排他実行する。
/// 各テストは finally で IgnoreUpdateTag を元の値へ戻す。
/// </summary>
[Collection("Sequential")]
public class SettingsManagerMutateAndSaveTests
{
    /// <summary>
    /// 100 並列スレッドが MutateAndSave を呼んでも全 Save が完走し最終状態が一貫。
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task SettingsManager_ConcurrentMutateAndSave_NoDataLoss()
    {
        // Arrange
        const int Threads = 100;
        var cancellationToken = TestContext.Current.CancellationToken;
        var mgr = SettingsManager.Instance;
        var originalTag = mgr.Current.IgnoreUpdateTag;
        try
        {
            var barrier = new TaskCompletionSource();
            var exceptions = new ConcurrentBag<Exception>();
            var writtenValues = new ConcurrentBag<string>();

            var tasks = Enumerable.Range(0, Threads).Select(i => Task.Run(async () =>
            {
                await barrier.Task.WaitAsync(cancellationToken);
                try
                {
                    var v = $"v1.0.{i:D4}";
                    mgr.MutateAndSave(s => s.IgnoreUpdateTag = v);
                    writtenValues.Add(v);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }, cancellationToken)).ToArray();

            // Act
            barrier.SetResult();
            await Task.WhenAll(tasks).WaitAsync(cancellationToken);

            // Assert
            Assert.Empty(exceptions);
            Assert.Equal(Threads, writtenValues.Count);
            // 最終値が「いずれかの書き込んだ値」と一致 (lost-update は許容)
            Assert.Contains(mgr.Current.IgnoreUpdateTag, writtenValues);
        }
        finally
        {
            try { mgr.MutateAndSave(s => s.IgnoreUpdateTag = originalTag); } catch { }
        }
    }
}
