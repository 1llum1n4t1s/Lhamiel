using Xunit;

namespace Lhamiel.Tests.Unit;

public sealed class AppCommandLineLifecycleTests
{
    [Theory]
    [InlineData("--extract", "Extract")]
    [InlineData("--compress", "Compress")]
    public void ParseCommandLineArgs_ParsesDirectOperation(string argument, string expected)
    {
        var request = App.ParseCommandLineArgs([argument, @"C:\sample.exe"]);

        Assert.Equal(expected, request.Operation.ToString());
        Assert.Equal([@"C:\sample.exe"], request.FilePaths);
    }

    [Theory]
    [InlineData("--extract")]
    [InlineData("--compress")]
    public void ParseCommandLineArgs_OperationOnlyHasNoWorkAndCanOpenMainWindow(string argument)
    {
        var request = App.ParseCommandLineArgs([argument]);

        Assert.Empty(request.FilePaths);
    }

    [Fact]
    public void ParseCommandLineArgs_CombinesFormatAndCompressionRoute()
    {
        var request = App.ParseCommandLineArgs(["--compress", "--format", "7z", @"C:\sample.zip"]);

        Assert.Equal(CommandLineOperation.Compress, request.Operation);
        Assert.Equal("7z", request.CompressionFormat);
        Assert.Equal([@"C:\sample.zip"], request.FilePaths);
    }

    [Fact]
    public void ParseCommandLineArgs_PreservesEveryPlayerSelectionArgument()
    {
        var request = App.ParseCommandLineArgs(
            ["--compress", @"C:\first.txt", @"C:\second.txt", @"C:\folder"]);

        Assert.Equal(CommandLineOperation.Compress, request.Operation);
        Assert.Equal([@"C:\first.txt", @"C:\second.txt", @"C:\folder"], request.FilePaths);
    }

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
