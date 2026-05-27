using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// ShellOpener のユニットテスト。
/// Issue #54 (Process.Start UI スレッド blocking) 対策で導入されたヘルパが、
/// DryRun 経由でプロセス起動を抑止しつつ Task.Run 化されていることを保証する。
/// </summary>
[Collection("ShellOpener")]
public class ShellOpenerTests : IDisposable
{
    public ShellOpenerTests()
    {
        ShellOpener.DryRun = true;
    }

    public void Dispose()
    {
        ShellOpener.DryRun = false;
    }

    [Fact]
    public async Task OpenWithDefaultHandlerAsync_DryRun_CompletesWithoutProcessStart()
    {
        // DryRun=true なら Process.Start は呼ばれず、即座に Task が完了する
        var task = ShellOpener.OpenWithDefaultHandlerAsync("https://example.com/");
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task OpenInExplorerAsync_DryRun_CompletesWithoutProcessStart()
    {
        var task = ShellOpener.OpenInExplorerAsync(@"C:\Windows");
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void OpenWithDefaultHandlerAsync_ReturnsTaskRunInstance_NotSyncCompleted()
    {
        // DryRun=true でも Task.Run の Task が返ることを保証する (UI スレッドから async に逃がす保証)。
        // Issue #54 対策: 戻り値が同期完了 Task ではなく、別スレッドで走るタスクであること。
        ShellOpener.DryRun = true;
        var task = ShellOpener.OpenWithDefaultHandlerAsync("https://example.com/");
        // Task.Run で生成された Task は完了済みかもしれないが、CompletedTask の参照ではない
        Assert.NotSame(Task.CompletedTask, task);
    }

    [Fact]
    public void OpenInExplorerAsync_ReturnsTaskRunInstance_NotSyncCompleted()
    {
        ShellOpener.DryRun = true;
        var task = ShellOpener.OpenInExplorerAsync(@"C:\Windows");
        Assert.NotSame(Task.CompletedTask, task);
    }
}
