using System.Collections.Concurrent;
using System.Diagnostics;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// SettingsManager のシングルトン状態を触る MutateAndSave / Mutate / CreateSnapshot のテスト群。
/// %LocalAppData%\Lhamiel\settings.json に副作用があるため [Collection("Sequential")] で排他実行する。
/// 各テストは finally で IgnoreUpdateTag を元の値へ戻す。
/// 旧 VelopackIntegrationHappyTests / VelopackIntegrationAdversarialTests から内容別に分離した。
/// </summary>
[Collection("Sequential")]
public class SettingsManagerMutateAndSaveTests
{
    /// <summary>
    /// SettingsManager.MutateAndSave() に渡したミューテータが Current 設定に適用され、後続参照に反映されること。
    /// </summary>
    [Fact]
    public void SettingsManager_MutateAndSave_AppliesMutationAtomically()
    {
        // Arrange
        var mgr = SettingsManager.Instance;
        var original = mgr.Current.IgnoreUpdateTag;
        var marker = "happy-marker-" + Guid.NewGuid().ToString("N");

        try
        {
            // Act
            mgr.MutateAndSave(s => s.IgnoreUpdateTag = marker);

            // Assert
            Assert.Equal(marker, mgr.Current.IgnoreUpdateTag);
        }
        finally
        {
            try { mgr.MutateAndSave(s => s.IgnoreUpdateTag = original); } catch { }
        }
    }

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

    /// <summary>
    /// MutateAndSave 内で mutator が例外を投げた場合、lock が確実に解放され後続操作が完走する。
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task SettingsManager_MutateAndSaveThrowingMutator_LockReleased()
    {
        // Arrange
        var mgr = SettingsManager.Instance;
        var originalTag = mgr.Current.IgnoreUpdateTag;
        try
        {
            // Act
            Assert.Throws<InvalidOperationException>(() =>
                mgr.MutateAndSave(_ => throw new InvalidOperationException("mutator boom")));

            // Assert: lock が解放されていれば後続操作が即座に成功
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            var followUp = Task.Run(() =>
            {
                mgr.Mutate(s => s.IgnoreUpdateTag = "after_throw");
            }, cts.Token);

            await followUp;
            Assert.Equal("after_throw", mgr.Current.IgnoreUpdateTag);
        }
        finally
        {
            try { mgr.MutateAndSave(s => s.IgnoreUpdateTag = originalTag); } catch { }
        }
    }

    /// <summary>
    /// Mutate + CreateSnapshot を並列実行しても InvalidOperationException (列挙中変更) が発生しない。
    /// 除外パターンは .lhaignore に移行済みのため、ここでは IgnoreUpdateTag (string) を入れ替える。
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task SettingsManager_ConcurrentSnapshotAndMutate_NoEnumerationException()
    {
        // Arrange
        var testCancellation = TestContext.Current.CancellationToken;
        var mgr = SettingsManager.Instance;
        var originalTag = mgr.Current.IgnoreUpdateTag;
        try
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(testCancellation);
            var exceptions = new ConcurrentBag<Exception>();

            var mutator = Task.Run(() =>
            {
                var rng = new Random(42);
                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        mgr.Mutate(s => s.IgnoreUpdateTag = $"v{rng.Next(1, 1000)}.{rng.Next(0, 100)}.{rng.Next(0, 100)}");
                    }
                    catch (Exception ex) { exceptions.Add(ex); }
                }
            }, testCancellation);

            var snapshotters = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 100; i++)
                {
                    testCancellation.ThrowIfCancellationRequested();
                    try
                    {
                        var snap = mgr.CreateSnapshot();
                        _ = snap.IgnoreUpdateTag?.Length ?? 0;
                    }
                    catch (Exception ex) { exceptions.Add(ex); }
                }
            }, testCancellation)).ToArray();

            // Act
            await Task.WhenAll(snapshotters).WaitAsync(testCancellation);
            stop.Cancel();
            await mutator.WaitAsync(testCancellation);

            // Assert
            Assert.Empty(exceptions);
        }
        finally
        {
            try { mgr.Mutate(s => s.IgnoreUpdateTag = originalTag); } catch { }
        }
    }

    /// <summary>
    /// MutateAndSave を 500 回連続呼び出ししても全て完走 (ファイル handle 枯渇しない)。
    /// </summary>
    [Fact(Timeout = 60000)]
    public void SettingsManager_MutateAndSave_Repeated500Times_CompletesAllWrites()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mgr = SettingsManager.Instance;
        var originalIgnoreTag = mgr.Current.IgnoreUpdateTag;

        try
        {
            // Act
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 500; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int captured = i;
                mgr.MutateAndSave(s => s.IgnoreUpdateTag = $"v0.0.{captured % 256}");
            }
            sw.Stop();

            // Assert
            Assert.StartsWith("v0.0.", mgr.Current.IgnoreUpdateTag);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60),
                $"500 回の MutateAndSave が遅すぎる: {sw.Elapsed}");
        }
        finally
        {
            try { mgr.MutateAndSave(s => s.IgnoreUpdateTag = originalIgnoreTag); } catch { }
        }
    }
}

/// <summary>
/// SettingsManager.MutateAndSave の引数検証。状態を変更する前に例外となるため並列実行してよい。
/// </summary>
public class SettingsManagerArgumentValidationTests
{
    /// <summary>
    /// MutateAndSave に null Action を渡すと ArgumentNullException が即座にスローされること。
    /// </summary>
    [Fact]
    public void SettingsManager_MutateAndSave_NullMutator_ThrowsArgumentNullException()
    {
        // Arrange
        Action<Settings>? mutator = null;

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => SettingsManager.Instance.MutateAndSave(mutator!));
        Assert.Equal("mutator", ex.ParamName);
    }
}
