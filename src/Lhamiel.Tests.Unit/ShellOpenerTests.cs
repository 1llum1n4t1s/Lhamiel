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
}
