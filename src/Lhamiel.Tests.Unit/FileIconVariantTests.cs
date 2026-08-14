using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

public sealed class FileIconVariantTests
{
    [Fact]
    public void Settings_DefaultFileIconVariant_IsClassic()
    {
        Assert.Equal(Settings.FileIconVariantClassic, new Settings().FileIconVariant);
    }

    [Fact]
    public void ResetToDefaults_RestoresClassicFileIconVariant()
    {
        var settings = new Settings { FileIconVariant = Settings.FileIconVariantIce };

        settings.ResetToDefaults();

        Assert.Equal(Settings.FileIconVariantClassic, settings.FileIconVariant);
    }

    [Fact]
    public void SupportedFileIconVariants_ContainsAllSelectableVariants()
    {
        Assert.Equal(
            [
                Settings.FileIconVariantClassic,
                Settings.FileIconVariantFolder,
                Settings.FileIconVariantCute,
                Settings.FileIconVariantIce
            ],
            Settings.SupportedFileIconVariants);
    }

    [Theory]
    [InlineData("Classic", "file.ico")]
    [InlineData("Folder", "file_folder.ico")]
    [InlineData("Cute", "file_cute.ico")]
    [InlineData("Ice", "file_ice.ico")]
    [InlineData("cute", "file_cute.ico")]
    [InlineData("unknown", "file.ico")]
    [InlineData(null, "file.ico")]
    public void GetPreferredFileIconFileName_MapsVariantToExpectedAsset(string? variant, string expected)
    {
        Assert.Equal(expected, FileAssociation.GetPreferredFileIconFileName(variant));
    }
}
