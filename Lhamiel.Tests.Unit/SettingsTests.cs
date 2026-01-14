using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// Settings class unit tests
/// </summary>
public class SettingsTests
{
    [Fact]
    public void Settings_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var settings = new Settings();

        // Assert
        Assert.Equal("zip", settings.CompressionFormat);
        Assert.False(settings.ExtractionOutputToSameDirectory);
        Assert.False(settings.CompressionOutputToSameDirectory);
        Assert.True(settings.EnableShortcutCreation);
        Assert.Equal("1llum1n4t1s", settings.UpdateRepoOwner);
        Assert.Equal("Lhamiel", settings.UpdateRepoName);
        Assert.Equal("release", settings.UpdateChannel);
    }

    [Fact]
    public void ResetToDefaults_RestoresDefaultValues()
    {
        // Arrange
        var settings = new Settings
        {
            CompressionFormat = "7z",
            ExtractionOutputToSameDirectory = true,
            CompressionOutputToSameDirectory = true,
            EnableShortcutCreation = false,
            UpdateRepoOwner = "test",
            UpdateRepoName = "test-repo",
            UpdateChannel = "beta"
        };

        // Act
        settings.ResetToDefaults();

        // Assert
        Assert.Equal("zip", settings.CompressionFormat);
        Assert.False(settings.ExtractionOutputToSameDirectory);
        Assert.False(settings.CompressionOutputToSameDirectory);
        Assert.True(settings.EnableShortcutCreation);
        Assert.Equal("1llum1n4t1s", settings.UpdateRepoOwner);
        Assert.Equal("Lhamiel", settings.UpdateRepoName);
        Assert.Equal("release", settings.UpdateChannel);
    }

    [Theory]
    [InlineData("zip", true)]
    [InlineData("7z", true)]
    [InlineData("tar", true)]
    [InlineData("lha", true)]
    [InlineData("rar", false)]
    [InlineData("gz", false)]
    [InlineData("unknown", false)]
    public void SupportedCompressionFormats_ContainsExpectedFormats(string format, bool shouldBeSupported)
    {
        // Act
        var isSupported = Settings.SupportedCompressionFormats.Contains(format);

        // Assert
        Assert.Equal(shouldBeSupported, isSupported);
    }

    [Theory]
    [InlineData("zip", true)]
    [InlineData("7z", true)]
    [InlineData("rar", true)]
    [InlineData("unknown", false)]
    public void SupportedExtractionFormats_ContainsExpectedFormats(string format, bool shouldBeSupported)
    {
        // Act
        var isSupported = Settings.SupportedExtractionFormats.Contains(format);

        // Assert
        Assert.Equal(shouldBeSupported, isSupported);
    }

    [Theory]
    [InlineData("rar", true)]
    [InlineData("arj", true)]
    [InlineData("z", true)]
    [InlineData("zip", false)]
    [InlineData("7z", false)]
    public void ExtractOnlyFormats_ContainsExpectedFormats(string format, bool shouldBeExtractOnly)
    {
        // Act
        var isExtractOnly = Settings.ExtractOnlyFormats.Contains(format);

        // Assert
        Assert.Equal(shouldBeExtractOnly, isExtractOnly);
    }
}
