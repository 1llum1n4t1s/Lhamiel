using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// Velopack のインストール／アンインストールライフサイクル連携テスト。
/// </summary>
public class ProgramLifecycleTests
{
    [Fact]
    public void CleanupBeforeUninstall_RemovesStartupContextMenuAndFileAssociations()
    {
        var startupUnregisterCount = 0;
        bool? contextMenuEnabled = null;
        var fileAssociationsRemoved = false;

        Program.CleanupBeforeUninstall(
            () => startupUnregisterCount++,
            enabled =>
            {
                contextMenuEnabled = enabled;
                return true;
            },
            () =>
            {
                fileAssociationsRemoved = true;
                return true;
            });

        Assert.Equal(1, startupUnregisterCount);
        Assert.False(contextMenuEnabled);
        Assert.True(fileAssociationsRemoved);
    }
}
