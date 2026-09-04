using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// Velopack のインストール／アンインストールライフサイクル連携テスト。
/// </summary>
public class ProgramLifecycleTests
{
    [Fact]
    public void RestoreSelectedApplicationShortcutIcons_AppliesNormalizedSelectedVariant()
    {
        string? appliedVariant = null;

        Program.RestoreSelectedApplicationShortcutIcons(
            variant => appliedVariant = variant,
            "crystal");

        Assert.Equal(Settings.AppIconVariantCrystal, appliedVariant);
    }

    [Fact]
    public void RestoreSelectedApplicationShortcutIcons_UnknownVariantFallsBackToClassic()
    {
        string? appliedVariant = null;

        Program.RestoreSelectedApplicationShortcutIcons(
            variant => appliedVariant = variant,
            "unknown");

        Assert.Equal(Settings.AppIconVariantClassic, appliedVariant);
    }

    [Fact]
    public void ShortcutIconRestorePending_RestoresOnceAndClearsMarker()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"Lhamiel-IconRestore-{Guid.NewGuid():N}");
        var markerPath = Path.Combine(tempDirectory, "pending");
        string? appliedVariant = null;
        try
        {
            Assert.True(Program.MarkShortcutIconRestorePending(markerPath));
            Assert.True(File.Exists(markerPath));

            Assert.True(Program.RestoreSelectedApplicationShortcutIconsIfPending(
                markerPath,
                variant => appliedVariant = variant,
                Settings.AppIconVariantCrystal));

            Assert.Equal(Settings.AppIconVariantCrystal, appliedVariant);
            Assert.False(File.Exists(markerPath));
            Assert.False(Program.RestoreSelectedApplicationShortcutIconsIfPending(
                markerPath,
                _ => throw new InvalidOperationException("二重復元されました"),
                Settings.AppIconVariantClassic));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void CleanupBeforeUninstall_RemovesStartupContextMenuAndFileAssociations()
    {
        var startupUnregisterCount = 0;
        var contextMenuRemoved = false;
        var fileAssociationsRemoved = false;

        Program.CleanupBeforeUninstall(
            () => startupUnregisterCount++,
            () =>
            {
                contextMenuRemoved = true;
                return true;
            },
            () =>
            {
                fileAssociationsRemoved = true;
                return true;
            });

        Assert.Equal(1, startupUnregisterCount);
        Assert.True(contextMenuRemoved);
        Assert.True(fileAssociationsRemoved);
    }
}
