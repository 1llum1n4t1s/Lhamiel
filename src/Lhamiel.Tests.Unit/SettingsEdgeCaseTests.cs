using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// Settings の追加エッジケーステスト
/// </summary>
public class SettingsEdgeCaseTests
{
    [Fact]
    public void NewSettings_AndResetToDefaults_ProduceSameValues()
    {
        var fresh = new Settings();
        var reset = new Settings { CompressionFormat = "7z", Theme = "Dark" };
        reset.ResetToDefaults();

        Assert.Equal(fresh.CompressionFormat, reset.CompressionFormat);
        Assert.Equal(fresh.Theme, reset.Theme);
        Assert.Equal(fresh.Locale, reset.Locale);
        Assert.Equal(fresh.CompressMultipleAsOne, reset.CompressMultipleAsOne);
        Assert.Equal(fresh.ZipCompressionLevel, reset.ZipCompressionLevel);
        Assert.Equal(fresh.SevenZipCompressionLevel, reset.SevenZipCompressionLevel);
        Assert.Equal(fresh.OpenExtractionOutputFolder, reset.OpenExtractionOutputFolder);
        Assert.Equal(fresh.OpenCompressionOutputFolder, reset.OpenCompressionOutputFolder);
        Assert.Equal(fresh.LogMaxSizeMB, reset.LogMaxSizeMB);
        Assert.Equal(fresh.LogRetentionDays, reset.LogRetentionDays);
        Assert.Equal(fresh.ExtractionOutputToSameDirectory, reset.ExtractionOutputToSameDirectory);
        Assert.Equal(fresh.CompressionOutputToSameDirectory, reset.CompressionOutputToSameDirectory);
    }

    [Fact]
    public void ExcludedFilePatterns_DefaultContainsAllIgnoredItems()
    {
        var settings = new Settings();
        foreach (var file in ArchiveExtractor.IgnoredSystemFiles)
            Assert.Contains(file, settings.ExcludedFilePatterns);
        foreach (var dir in ArchiveExtractor.IgnoredSystemDirectories)
            Assert.Contains(dir, settings.ExcludedFilePatterns);
    }

    [Fact]
    public void ExcludedFilePatterns_DefaultHasNoDuplicates()
    {
        var settings = new Settings();
        var distinct = settings.ExcludedFilePatterns.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.Equal(settings.ExcludedFilePatterns.Count, distinct);
    }

    [Fact]
    public void ExcludedFilePatterns_ResetRestoresDefaults()
    {
        var settings = new Settings();
        settings.ExcludedFilePatterns.Clear();
        settings.ExcludedFilePatterns.Add("custom_pattern");
        settings.ResetToDefaults();

        Assert.DoesNotContain("custom_pattern", settings.ExcludedFilePatterns);
        Assert.Contains(".DS_Store", settings.ExcludedFilePatterns);
    }

    [Fact]
    public void SupportedCompressionFormats_IsSubsetOfExtractionFormats()
    {
        foreach (var format in Settings.SupportedCompressionFormats)
            Assert.Contains(format, Settings.SupportedExtractionFormats);
    }

    [Fact]
    public void ExtractOnlyFormats_NotInSupportedCompressionFormats()
    {
        foreach (var format in Settings.ExtractOnlyFormats)
            Assert.DoesNotContain(format, Settings.SupportedCompressionFormats);
    }

    [Fact]
    public void ExtractOnlyFormats_AreInSupportedExtractionFormats()
    {
        foreach (var format in Settings.ExtractOnlyFormats)
            Assert.Contains(format, Settings.SupportedExtractionFormats);
    }

    [Fact]
    public void DefaultCompressionFormat_IsInSupportedCompressionFormats()
    {
        var settings = new Settings();
        Assert.Contains(settings.CompressionFormat, Settings.SupportedCompressionFormats);
    }

    [Fact]
    public void LogMaxSizeMB_DefaultIsPositive()
    {
        Assert.True(new Settings().LogMaxSizeMB > 0);
    }

    [Fact]
    public void LogRetentionDays_DefaultIsPositive()
    {
        Assert.True(new Settings().LogRetentionDays > 0);
    }

    [Fact]
    public void Locale_ResetRestoresEmpty()
    {
        var settings = new Settings { Locale = "ja_JP" };
        settings.ResetToDefaults();
        Assert.Equal("", settings.Locale);
    }
}
