using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

public sealed class AppIconVariantTests
{
    [Theory]
    [InlineData(0, "Lhamiel.lnk", null)]
    [InlineData(1, "Lhamiel展開.lnk", "--extract")]
    [InlineData(2, "Lhamiel圧縮.lnk", "--compress")]
    public void DesktopShortcutDefinition_UsesExpectedNameAndArguments(
        int kind,
        string expectedFileName,
        string? expectedArguments)
    {
        var definition = ShortcutCreator.GetDesktopShortcutDefinition((DesktopShortcutKind)kind);

        Assert.Equal(expectedFileName, definition.FileName);
        Assert.Equal(expectedArguments, definition.Arguments);
    }

    [Fact]
    public void SanitizeAfterLoad_UnknownAppIconVariant_FallsBackToClassic()
    {
        var settings = new Settings { AppIconVariant = "MetalBoxArrow" };

        settings.SanitizeAfterLoad();

        Assert.Equal(Settings.AppIconVariantClassic, settings.AppIconVariant);
    }

    [Theory]
    [InlineData("CRYSTAL", "Crystal")]
    [InlineData("legacy", "Legacy")]
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
                    Program.AppUserModelId,
                    "--extract"));
            Assert.Equal(Program.AppUserModelId, ShellLinkNative.GetAppUserModelId(shortcutPath));
            Assert.Equal("--extract", ShellLinkNative.GetArguments(shortcutPath));
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
