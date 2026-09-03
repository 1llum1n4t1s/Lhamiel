using System.Collections.Concurrent;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

public sealed class MessageServiceLifecycleTests
{
    [Fact]
    public async Task ShowAfterClosingAsync_WaitsForCloseAndDialogDismissal()
    {
        var events = new ConcurrentQueue<string>();
        var closeCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var flow = MessageService.ShowAfterClosingAsync(
            async () =>
            {
                events.Enqueue("close:start");
                await closeCompletion.Task;
                events.Enqueue("close:end");
            },
            async () =>
            {
                events.Enqueue("dialog:start");
                dialogStarted.TrySetResult(true);
                await dialogCompletion.Task;
                events.Enqueue("dialog:end");
            });

        Assert.Equal(["close:start"], events);
        Assert.False(flow.IsCompleted);

        closeCompletion.TrySetResult(true);
        await dialogStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["close:start", "close:end", "dialog:start"], events);
        Assert.False(flow.IsCompleted);

        dialogCompletion.TrySetResult(true);
        await flow.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["close:start", "close:end", "dialog:start", "dialog:end"], events);
    }

    [Fact]
    public async Task ShowAfterClosingAsync_WithoutTransientWindowStillAwaitsDialog()
    {
        var dialogCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var flow = MessageService.ShowAfterClosingAsync(
            closeTransientWindow: null,
            () => dialogCompletion.Task);

        Assert.False(flow.IsCompleted);

        dialogCompletion.TrySetResult(true);
        await flow.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
