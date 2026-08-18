using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

public sealed class AppIconVariantTests
{
    [Fact]
    public void Settings_DefaultAppIconVariant_IsClassic()
    {
        Assert.Equal(Settings.AppIconVariantClassic, new Settings().AppIconVariant);
    }

    [Fact]
    public void ResetToDefaults_RestoresClassicAppIconVariant()
    {
        var settings = new Settings { AppIconVariant = Settings.AppIconVariantCrystal };

        settings.ResetToDefaults();

        Assert.Equal(Settings.AppIconVariantClassic, settings.AppIconVariant);
    }

    [Fact]
    public void SupportedAppIconVariants_ContainsBothSelectableVariants()
    {
        Assert.Equal(
            [Settings.AppIconVariantClassic, Settings.AppIconVariantCrystal],
            Settings.SupportedAppIconVariants);
    }

    [Theory]
    [InlineData("Classic", "app_classic.ico")]
    [InlineData("classic", "app_classic.ico")]
    [InlineData("Crystal", "app_crystal.ico")]
    [InlineData("crystal", "app_crystal.ico")]
    [InlineData("unknown", "app_classic.ico")]
    [InlineData(null, "app_classic.ico")]
    public void GetIconFileName_MapsVariantToExpectedAsset(string? variant, string expected)
    {
        Assert.Equal(expected, AppIconManager.GetIconFileName(variant));
    }

    [Theory]
    [InlineData("Classic", "avares://Lhamiel/icon/app_icon_classic.png")]
    [InlineData("Crystal", "avares://Lhamiel/icon/app_icon_crystal.png")]
    [InlineData("unknown", "avares://Lhamiel/icon/app_icon_classic.png")]
    public void GetPreviewResourceUri_MapsVariantToExpectedAsset(string? variant, string expected)
    {
        Assert.Equal(expected, AppIconManager.GetPreviewResourceUri(variant));
    }

    [Fact]
    public void SanitizeAfterLoad_UnknownAppIconVariant_FallsBackToClassic()
    {
        var settings = new Settings { AppIconVariant = "MetalBoxArrow" };

        settings.SanitizeAfterLoad();

        Assert.Equal(Settings.AppIconVariantClassic, settings.AppIconVariant);
    }

    [Theory]
    [InlineData("classic", "Classic")]
    [InlineData("CRYSTAL", "Crystal")]
    public void SanitizeAfterLoad_AppIconVariant_NormalizedToCanonicalCase(string input, string expected)
    {
        var settings = new Settings { AppIconVariant = input };

        settings.SanitizeAfterLoad();

        Assert.Equal(expected, settings.AppIconVariant);
    }

    [Fact]
    public void ShellLinkNative_CanCreateAndUpdateShortcutIcon()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var targetPath = Environment.ProcessPath;
        Assert.False(string.IsNullOrEmpty(targetPath));

        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var initialIconPath = Path.Combine(systemDirectory, "shell32.dll");
        var updatedIconPath = Path.Combine(systemDirectory, "imageres.dll");
        Assert.True(File.Exists(initialIconPath));
        Assert.True(File.Exists(updatedIconPath));

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"Lhamiel-AppIcon-{Guid.NewGuid():N}");
        var shortcutPath = Path.Combine(tempDirectory, "Lhamiel.lnk");
        var creatorShortcutPath = Path.Combine(tempDirectory, "Lhamiel-Creator.lnk");
        var legacyShortcutPath = Path.Combine(tempDirectory, "Lhamiel-Legacy.lnk");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            Assert.True(
                ShellLinkNative.CreateShortcut(
                    targetPath!,
                    shortcutPath,
                    "Lhamiel icon test",
                    initialIconPath,
                    Program.AppUserModelId));
            Assert.Equal(Program.AppUserModelId, ShellLinkNative.GetAppUserModelId(shortcutPath));
            Assert.True(ShellLinkNative.UpdateIconLocation(shortcutPath, updatedIconPath, Program.AppUserModelId));
            Assert.Equal(Program.AppUserModelId, ShellLinkNative.GetAppUserModelId(shortcutPath));

            Assert.True(ShortcutCreator.CreateShortcut(
                targetPath!,
                creatorShortcutPath,
                "Lhamiel creator test",
                initialIconPath));
            Assert.Equal(Program.AppUserModelId, ShellLinkNative.GetAppUserModelId(creatorShortcutPath));

            Assert.True(ShellLinkNative.CreateShortcut(
                targetPath!,
                legacyShortcutPath,
                "Lhamiel legacy test",
                initialIconPath));
            Assert.Null(ShellLinkNative.GetAppUserModelId(legacyShortcutPath));
            Assert.True(ShellLinkNative.UpdateIconLocation(
                legacyShortcutPath,
                updatedIconPath,
                Program.AppUserModelId));
            Assert.Equal(Program.AppUserModelId, ShellLinkNative.GetAppUserModelId(legacyShortcutPath));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
