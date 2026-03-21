using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// Settings の追加エッジケーステスト（実装を信用しない）
/// </summary>
public class SettingsEdgeCaseTests
{
    // === デフォルト値の整合性 ===

    [Fact]
    public void NewSettings_AndResetToDefaults_ProduceSameValues()
    {
        // new Settings() と ResetToDefaults() の結果が一致すべき
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
        Assert.Equal(fresh.EnableShortcutCreation, reset.EnableShortcutCreation);
        Assert.Equal(fresh.ExtractionOutputToSameDirectory, reset.ExtractionOutputToSameDirectory);
        Assert.Equal(fresh.CompressionOutputToSameDirectory, reset.CompressionOutputToSameDirectory);
    }

    // === ExcludedFilePatterns ===

    [Fact]
    public void ExcludedFilePatterns_DefaultContainsAllIgnoredItems()
    {
        var settings = new Settings();
        // ArchiveExtractor のリストと一致すること
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

    // === 圧縮レベルの境界値 ===

    [Fact]
    public void CompressionLevel_ZeroIsValid()
    {
        var settings = new Settings { ZipCompressionLevel = 0 };
        Assert.Equal(0, settings.ZipCompressionLevel);
    }

    [Fact]
    public void CompressionLevel_NineIsValid()
    {
        var settings = new Settings { ZipCompressionLevel = 9 };
        Assert.Equal(9, settings.ZipCompressionLevel);
    }

    [Fact]
    public void CompressionLevel_NegativeIsAccepted()
    {
        // プロパティ自体はバリデーションしないので受け入れるはず
        var settings = new Settings { ZipCompressionLevel = -1 };
        Assert.Equal(-1, settings.ZipCompressionLevel);
    }

    [Fact]
    public void CompressionLevel_OverNineIsAccepted()
    {
        // プロパティ自体はバリデーションしないので受け入れるはず
        var settings = new Settings { ZipCompressionLevel = 100 };
        Assert.Equal(100, settings.ZipCompressionLevel);
    }

    // === SupportedFormats の整合性 ===

    [Fact]
    public void SupportedCompressionFormats_IsSubsetOfExtractionFormats()
    {
        // 圧縮できる形式はすべて展開もできるべき
        foreach (var format in Settings.SupportedCompressionFormats)
        {
            Assert.Contains(format, Settings.SupportedExtractionFormats);
        }
    }

    [Fact]
    public void ExtractOnlyFormats_NotInSupportedCompressionFormats()
    {
        // 展開専用形式は圧縮フォーマットに含まれないべき
        foreach (var format in Settings.ExtractOnlyFormats)
        {
            Assert.DoesNotContain(format, Settings.SupportedCompressionFormats);
        }
    }

    [Fact]
    public void ExtractOnlyFormats_AreInSupportedExtractionFormats()
    {
        // 展開専用形式は展開フォーマットには含まれるべき
        foreach (var format in Settings.ExtractOnlyFormats)
        {
            Assert.Contains(format, Settings.SupportedExtractionFormats);
        }
    }

    [Fact]
    public void DefaultCompressionFormat_IsInSupportedCompressionFormats()
    {
        var settings = new Settings();
        Assert.Contains(settings.CompressionFormat, Settings.SupportedCompressionFormats);
    }

    // === ログ設定の妥当性 ===

    [Fact]
    public void LogMaxSizeMB_DefaultIsPositive()
    {
        var settings = new Settings();
        Assert.True(settings.LogMaxSizeMB > 0);
    }

    [Fact]
    public void LogRetentionDays_DefaultIsPositive()
    {
        var settings = new Settings();
        Assert.True(settings.LogRetentionDays > 0);
    }

    // === テーマの妥当性 ===

    [Fact]
    public void Theme_DefaultIsSystem()
    {
        var settings = new Settings();
        Assert.Equal("System", settings.Theme);
    }

    [Fact]
    public void Theme_AcceptsArbitraryString()
    {
        // バリデーションなしなので何でも受け入れる
        var settings = new Settings { Theme = "CustomTheme" };
        Assert.Equal("CustomTheme", settings.Theme);
    }

    // === Locale ===

    [Fact]
    public void Locale_DefaultIsEmpty()
    {
        var settings = new Settings();
        Assert.Equal("", settings.Locale);
    }

    [Fact]
    public void Locale_ResetRestoresEmpty()
    {
        var settings = new Settings { Locale = "ja_JP" };
        settings.ResetToDefaults();
        Assert.Equal("", settings.Locale);
    }

    // === UpdateChannel ===

    [Fact]
    public void UpdateChannel_DefaultIsRelease()
    {
        var settings = new Settings();
        Assert.Equal("release", settings.UpdateChannel);
    }

    [Fact]
    public void UpdateRepoOwner_DefaultIs1llum1n4t1s()
    {
        var settings = new Settings();
        Assert.Equal("1llum1n4t1s", settings.UpdateRepoOwner);
    }
}
