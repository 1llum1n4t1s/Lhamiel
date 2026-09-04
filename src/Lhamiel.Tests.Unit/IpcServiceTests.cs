using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// IpcService の Named Pipe 通信テスト。
/// 実際のパイプを使った統合テストで、送受信の正常系・異常系を検証。
/// <para>
/// すべて同一 PipeName (Lhamiel_IpcPipe_S{SessionId}) を共有するため、並列実行すると
/// NamedPipeServerStream(maxNumberOfServerInstances=1) で `All pipe instances are busy` が発生する。
/// `[Collection("Sequential")]` で xUnit の並列実行を抑止して flaky を防止する。
/// </para>
/// </summary>
[Collection("Sequential")]
public class IpcServiceTests
{
    private const int TestTimeoutMs = 10_000;

    [Fact]
    public async Task SendAndReceive_SelectionToken_ConsumesWholeBatchInReceiver()
    {
        var token = Guid.NewGuid().ToString("N");
        var path = ShellSelectionFile.GetPath(token);
        var paths = Enumerable.Range(0, 2000).Select(i => $@"C:\日本語の選択\file {i}.txt").ToArray();
        using var cts = new CancellationTokenSource(TestTimeoutMs);
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Task.Run(() => IpcService.StartServerAsync(args =>
            received.TrySetResult(App.ParseCommandLineArgs(args).FilePaths), cts.Token));
        try
        {
            File.WriteAllBytes(path, System.Text.Encoding.Unicode.GetBytes(string.Join('\0', paths) + '\0'));
            Assert.True(await IpcService.SendArgsToExistingInstanceAsync(
                ["--compress", ShellSelectionFile.Argument, token], cts.Token));
            Assert.Equal(paths, await received.Task.WaitAsync(cts.Token));
            Assert.False(File.Exists(path));
        }
        finally
        {
            await cts.CancelAsync();
            try { await server; } catch (OperationCanceledException) { }
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SendAndReceive_RoundTrip_DeliversArgs()
    {
        using var cts = new CancellationTokenSource(TestTimeoutMs);
        var received = new TaskCompletionSource<string[]>();

        var serverTask = Task.Run(async () =>
        {
            await IpcService.StartServerAsync(args =>
            {
                received.TrySetResult(args);
            }, cts.Token);
        }, cts.Token);

        await Task.Delay(100, cts.Token);

        var sent = new[] { @"C:\test\file.zip", "--extract" };
        var success = await IpcService.SendArgsToExistingInstanceAsync(sent, cts.Token);

        Assert.True(success);

        var result = await received.Task.WaitAsync(cts.Token);
        Assert.Equal(sent, result);

        await cts.CancelAsync();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task SendArgs_CancelledToken_ReturnsFalse()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await IpcService.SendArgsToExistingInstanceAsync(
            ["test.zip"], cts.Token);

        Assert.False(result);
    }

    [Fact]
    public async Task SendArgs_NoServer_ReturnsFalseAfterTimeout()
    {
        // テスト用の短い待機で失敗を確認（実際の ConnectTotalTimeoutMs は長いため、即キャンセルで代替）
        using var cts = new CancellationTokenSource(500);

        var result = await IpcService.SendArgsToExistingInstanceAsync(
            ["test.zip"], cts.Token);

        Assert.False(result);
    }

    [Fact]
    public async Task Server_CancelledToken_ExitsGracefully()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await IpcService.StartServerAsync(_ => { }, cts.Token);
    }

    [Fact]
    public async Task SendAndReceive_EmptyArgs_DeliversEmptyArray()
    {
        using var cts = new CancellationTokenSource(TestTimeoutMs);
        var received = new TaskCompletionSource<string[]>();

        var serverTask = Task.Run(async () =>
        {
            await IpcService.StartServerAsync(args =>
            {
                received.TrySetResult(args);
            }, cts.Token);
        }, cts.Token);

        await Task.Delay(100, cts.Token);

        var success = await IpcService.SendArgsToExistingInstanceAsync([], cts.Token);
        Assert.True(success);

        var result = await received.Task.WaitAsync(cts.Token);
        Assert.Empty(result);

        await cts.CancelAsync();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task SendAndReceive_UnicodeArgs_PreservesEncoding()
    {
        using var cts = new CancellationTokenSource(TestTimeoutMs);
        var received = new TaskCompletionSource<string[]>();

        var serverTask = Task.Run(async () =>
        {
            await IpcService.StartServerAsync(args =>
            {
                received.TrySetResult(args);
            }, cts.Token);
        }, cts.Token);

        await Task.Delay(100, cts.Token);

        var sent = new[] { @"C:\テスト\ファイル.zip", "日本語パス", "émojis🎉" };
        var success = await IpcService.SendArgsToExistingInstanceAsync(sent, cts.Token);
        Assert.True(success);

        var result = await received.Task.WaitAsync(cts.Token);
        Assert.Equal(sent, result);

        await cts.CancelAsync();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task SendAndReceive_MultipleSends_AllDelivered()
    {
        using var cts = new CancellationTokenSource(TestTimeoutMs);
        var allReceived = new List<string[]>();
        var receiveCount = new TaskCompletionSource();
        var expectedCount = 3;

        var serverTask = Task.Run(async () =>
        {
            await IpcService.StartServerAsync(args =>
            {
                lock (allReceived)
                {
                    allReceived.Add(args);
                    if (allReceived.Count >= expectedCount)
                        receiveCount.TrySetResult();
                }
            }, cts.Token);
        }, cts.Token);

        await Task.Delay(100, cts.Token);

        for (var i = 0; i < expectedCount; i++)
        {
            var success = await IpcService.SendArgsToExistingInstanceAsync([$"file{i}.zip"], cts.Token);
            Assert.True(success);
        }

        await receiveCount.Task.WaitAsync(cts.Token);
        Assert.Equal(expectedCount, allReceived.Count);

        await cts.CancelAsync();
        try { await serverTask; } catch (OperationCanceledException) { }
    }
}
