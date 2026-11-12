using Lhamiel.Util;
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

    [Theory]
    [InlineData("LZH")]
    [InlineData("lha")]
    public void ParseCommandLineArgs_AcceptsLzhAliases(string format)
    {
        var request = App.ParseCommandLineArgs(
            ["--compress", "--format", format, @"C:\sample.txt"]);

        Assert.Equal(CommandLineOperation.Compress, request.Operation);
        Assert.Equal("LZH", request.CompressionFormat);
    }

    [Fact]
    public void ParseCommandLineArgs_PreservesEveryPlayerSelectionArgument()
    {
        var request = App.ParseCommandLineArgs(
            ["--compress", @"C:\first.txt", @"C:\second.txt", @"C:\folder"]);

        Assert.Equal(CommandLineOperation.Compress, request.Operation);
        Assert.Equal([@"C:\first.txt", @"C:\second.txt", @"C:\folder"], request.FilePaths);
    }

    // ── IPC 転送前のパス引数絶対化（2 つ目のインスタンス → 既存インスタンス） ──
    // 受信側は自分のカレントディレクトリでパスを解決するため、送信側で絶対化してから送る。

    private const string SenderDirectory = @"C:\sender\work";

    [Fact]
    public void AbsolutizeForwardedPathArgs_RelativePath_IsResolvedAgainstSenderDirectory()
    {
        var result = App.AbsolutizeForwardedPathArgs(
            ["--compress", "data.txt", @"sub\folder", @"..\up.zip"], SenderDirectory);

        Assert.Equal(
            ["--compress", @"C:\sender\work\data.txt", @"C:\sender\work\sub\folder", @"C:\sender\up.zip"],
            result);
    }

    [Fact]
    public void AbsolutizeForwardedPathArgs_AbsolutePath_IsUnchanged()
    {
        string[] args = [@"D:\archive\a.zip", @"\\server\share\b.7z", @"\\?\C:\long\c.lzh"];

        var result = App.AbsolutizeForwardedPathArgs(args, SenderDirectory);

        Assert.Equal(args, result);
    }

    [Fact]
    public void AbsolutizeForwardedPathArgs_OptionValues_AreUnchanged()
    {
        // --format の値・--shell-selection のトークン・その他オプションは変えない。
        var result = App.AbsolutizeForwardedPathArgs(
            ["--compress", "--format", "7z", ShellSelectionFile.Argument, "token123", "--extract", "rel.zip"],
            SenderDirectory);

        Assert.Equal(
            ["--compress", "--format", "7z", ShellSelectionFile.Argument, "token123", "--extract", @"C:\sender\work\rel.zip"],
            result);
    }

    [Fact]
    public void AbsolutizeForwardedPathArgs_TrailingFormatWithoutValue_FollowsParserRule()
    {
        // ParseCommandLineArgs は値の無い末尾 --format を単なるオプションとして無視する。同じ規則で扱う。
        var result = App.AbsolutizeForwardedPathArgs(["rel.zip", "--format"], SenderDirectory);

        Assert.Equal([@"C:\sender\work\rel.zip", "--format"], result);
    }

    [Fact]
    public void AbsolutizeForwardedPathArgs_DoesNotMutateInputAndKeepsParseResultConsistent()
    {
        string[] args = ["--compress", "--format", "zip", "rel.txt"];

        var result = App.AbsolutizeForwardedPathArgs(args, SenderDirectory);

        Assert.Equal("rel.txt", args[3]);
        var request = App.ParseCommandLineArgs(result);
        Assert.Equal(CommandLineOperation.Compress, request.Operation);
        Assert.Equal("ZIP", request.CompressionFormat, ignoreCase: true);
        Assert.Equal([@"C:\sender\work\rel.txt"], request.FilePaths);
    }

    [Fact]
    public void AbsolutizeForwardedPathArgs_EmptyArgs_ReturnsEmpty()
    {
        Assert.Empty(App.AbsolutizeForwardedPathArgs([], SenderDirectory));
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
