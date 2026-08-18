using Xunit;

namespace Lhamiel.Tests.Unit;

public sealed class AppCommandLineLifecycleTests
{
    [Fact]
    public void HandleIpcForwardResult_FailureNotifiesUser()
    {
        var notified = false;

        Assert.False(App.HandleIpcForwardResult(false, () => notified = true));
        Assert.True(notified);
    }

    [Fact]
    public void HandleIpcForwardResult_SuccessDoesNotNotifyUser()
    {
        var notified = false;

        Assert.True(App.HandleIpcForwardResult(true, () => notified = true));
        Assert.False(notified);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TryFinishEmptyCommandLineRequest_InvokesShutdownContract(bool shouldShutdown)
    {
        bool? requestedShutdown = null;

        var finished = App.TryFinishEmptyCommandLineRequest(
            processablePathCount: 0,
            shouldShutdown,
            value => requestedShutdown = value);

        Assert.True(finished);
        Assert.Equal(shouldShutdown, requestedShutdown);
    }

    [Fact]
    public void TryFinishEmptyCommandLineRequest_WithProcessablePath_DoesNotShutdown()
    {
        var callbackInvoked = false;

        var finished = App.TryFinishEmptyCommandLineRequest(
            processablePathCount: 1,
            shouldShutdown: true,
            _ => callbackInvoked = true);

        Assert.False(finished);
        Assert.False(callbackInvoked);
    }
}
