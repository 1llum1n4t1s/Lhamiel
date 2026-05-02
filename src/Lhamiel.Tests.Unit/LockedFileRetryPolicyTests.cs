using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// LockedFileRetryPolicy のユニットテスト。
/// 指数バックオフリトライの動作、各例外タイプの判定、成功・失敗パターンを検証。
/// </summary>
public class LockedFileRetryPolicyTests
{
    [Fact]
    public void Execute_SuccessOnFirstAttempt_NoRetry()
    {
        var callCount = 0;
        LockedFileRetryPolicy.Execute(() => callCount++, "test");
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Execute_SharingViolationThenSuccess_Retries()
    {
        var callCount = 0;
        LockedFileRetryPolicy.Execute(() =>
        {
            callCount++;
            if (callCount == 1)
                throw new IOException("locked") { HResult = unchecked((int)0x80070020) };
        }, "test", maxAttempts: 3, initialDelayMs: 1);

        Assert.Equal(2, callCount);
    }

    [Fact]
    public void Execute_LockViolationThenSuccess_Retries()
    {
        var callCount = 0;
        LockedFileRetryPolicy.Execute(() =>
        {
            callCount++;
            if (callCount == 1)
                throw new IOException("lock violation") { HResult = unchecked((int)0x80070021) };
        }, "test", maxAttempts: 3, initialDelayMs: 1);

        Assert.Equal(2, callCount);
    }

    [Fact]
    public void Execute_IOExceptionWithoutSpecificHResult_StillRetries()
    {
        var callCount = 0;
        LockedFileRetryPolicy.Execute(() =>
        {
            callCount++;
            if (callCount == 1)
                throw new IOException("generic IO error");
        }, "test", maxAttempts: 3, initialDelayMs: 1);

        Assert.Equal(2, callCount);
    }

    [Fact]
    public void Execute_UnauthorizedAccessException_Retries()
    {
        var callCount = 0;
        LockedFileRetryPolicy.Execute(() =>
        {
            callCount++;
            if (callCount == 1)
                throw new UnauthorizedAccessException("access denied");
        }, "test", maxAttempts: 3, initialDelayMs: 1);

        Assert.Equal(2, callCount);
    }

    [Fact]
    public void Execute_NonTransientException_ThrowsImmediately()
    {
        var callCount = 0;
        Assert.Throws<InvalidOperationException>(() =>
        {
            LockedFileRetryPolicy.Execute(() =>
            {
                callCount++;
                throw new InvalidOperationException("not transient");
            }, "test", maxAttempts: 3, initialDelayMs: 1);
        });

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Execute_ExceedsMaxAttempts_ThrowsLastException()
    {
        var callCount = 0;
        var ex = Assert.Throws<IOException>(() =>
        {
            LockedFileRetryPolicy.Execute(() =>
            {
                callCount++;
                throw new IOException("always locked") { HResult = unchecked((int)0x80070020) };
            }, "test", maxAttempts: 3, initialDelayMs: 1);
        });

        Assert.Equal(3, callCount);
        Assert.Equal("always locked", ex.Message);
    }

    // === Execute<T> テスト ===

    [Fact]
    public void ExecuteT_SuccessOnFirstAttempt_ReturnsValue()
    {
        var result = LockedFileRetryPolicy.Execute(() => 42, "test");
        Assert.Equal(42, result);
    }

    [Fact]
    public void ExecuteT_RetryThenSuccess_ReturnsValue()
    {
        var callCount = 0;
        var result = LockedFileRetryPolicy.Execute(() =>
        {
            callCount++;
            if (callCount == 1)
                throw new IOException("locked") { HResult = unchecked((int)0x80070020) };
            return "success";
        }, "test", maxAttempts: 3, initialDelayMs: 1);

        Assert.Equal("success", result);
    }

    // === ExecuteAsync テスト ===

    [Fact]
    public async Task ExecuteAsync_SuccessOnFirstAttempt_Completes()
    {
        var callCount = 0;
        await LockedFileRetryPolicy.ExecuteAsync(async () =>
        {
            callCount++;
            await Task.CompletedTask;
        }, "test");

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_RetryThenSuccess_Completes()
    {
        var callCount = 0;
        await LockedFileRetryPolicy.ExecuteAsync(async () =>
        {
            callCount++;
            await Task.CompletedTask;
            if (callCount == 1)
                throw new IOException("locked") { HResult = unchecked((int)0x80070020) };
        }, "test", maxAttempts: 3, initialDelayMs: 1);

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledToken_ThrowsOperationCancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await LockedFileRetryPolicy.ExecuteAsync(
                () => Task.CompletedTask, "test",
                cancellationToken: cts.Token);
        });
    }

    [Fact]
    public async Task ExecuteAsync_ExceedsMaxAttempts_ThrowsLastException()
    {
        var callCount = 0;
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await LockedFileRetryPolicy.ExecuteAsync(async () =>
            {
                callCount++;
                await Task.CompletedTask;
                throw new IOException("always locked") { HResult = unchecked((int)0x80070020) };
            }, "test", maxAttempts: 3, initialDelayMs: 1);
        });

        Assert.Equal(3, callCount);
    }
}
