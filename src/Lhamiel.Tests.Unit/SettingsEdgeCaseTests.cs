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
        var reset = new Settings
        {
            CompressionFormat = "7z",
            Theme = "Dark",
            CreateArchiveNameFolder = false,
            DirectoryStructureMode = DirectoryStructureMode.Flat,
        };
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
        // v1.0.160: ResetToDefaults の漏れ修正を検証
        Assert.Equal(fresh.CreateArchiveNameFolder, reset.CreateArchiveNameFolder);
        Assert.Equal(fresh.DirectoryStructureMode, reset.DirectoryStructureMode);
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

    // === SanitizeAfterLoad（v1.0.160 で追加） ===

    [Fact]
    public void SanitizeAfterLoad_UnknownUpdateChannel_FallsBackToRelease()
    {
        var settings = new Settings { UpdateChannel = "../../../evil" };
        settings.SanitizeAfterLoad();
        Assert.Equal("release", settings.UpdateChannel);
    }

    [Theory]
    [InlineData("release", "release")]
    [InlineData("prerelease", "prerelease")]
    [InlineData("RELEASE", "release")]      // 大文字混ざりは canonical ケースに正規化される
    [InlineData("PreRelease", "prerelease")] // 同上
    public void SanitizeAfterLoad_AllowListedUpdateChannel_NormalizedToCanonicalCase(string input, string expected)
    {
        var settings = new Settings { UpdateChannel = input };
        settings.SanitizeAfterLoad();
        Assert.Equal(expected, settings.UpdateChannel);
    }

    [Theory]
    [InlineData("DARK", "Dark")]
    [InlineData("light", "Light")]
    [InlineData("system", "System")]
    public void SanitizeAfterLoad_Theme_NormalizedToCanonicalCase(string input, string expected)
    {
        var settings = new Settings { Theme = input };
        settings.SanitizeAfterLoad();
        Assert.Equal(expected, settings.Theme);
    }

    [Fact]
    public void SanitizeAfterLoad_NonExistentOutputDirectory_FallsBackToDesktop()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var settings = new Settings { ExtractionOutputDirectory = @"Z:\NonExistent\Path\xyz_not_real" };
        settings.SanitizeAfterLoad();
        Assert.Equal(desktop, settings.ExtractionOutputDirectory);
    }

    [Fact]
    public void SanitizeAfterLoad_EmptyOutputDirectory_FallsBackToDesktop()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var settings = new Settings { CompressionOutputDirectory = "" };
        settings.SanitizeAfterLoad();
        Assert.Equal(desktop, settings.CompressionOutputDirectory);
    }

    [Fact]
    public void SanitizeAfterLoad_DesktopAsOutputDirectory_PreservedNotOverwritten()
    {
        // ユーザーが意図的に出力先として選んだ Desktop が、起動のたびに
        // SanitizeAfterLoad でリセットされない（= 設定の永続化が壊れない）ことを保証する。
        // PathValidator.IsProtectedDirectory は Desktop を保護対象に含むが、
        // SanitizeAfterLoad は IsSystemCriticalDirectory（より厳格）を使う設計。
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!Directory.Exists(desktop))
            return; // テスト環境に Desktop が無いプラットフォーム（CI 等）はスキップ
        var settings = new Settings { ExtractionOutputDirectory = desktop };
        settings.SanitizeAfterLoad();
        Assert.Equal(desktop, settings.ExtractionOutputDirectory);
    }

    [Fact]
    public void SanitizeAfterLoad_SystemCriticalDirectory_FallsBackToDesktop()
    {
        // 改竄耐性: settings.json を書き換えて Windows / Program Files を出力先にされても
        // 起動時に Desktop へフォールバックすることを保証する。
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!Directory.Exists(windows))
            return;
        var settings = new Settings { ExtractionOutputDirectory = windows };
        settings.SanitizeAfterLoad();
        Assert.Equal(desktop, settings.ExtractionOutputDirectory);
    }

    [Fact]
    public void SanitizeAfterLoad_UnknownTheme_FallsBackToSystem()
    {
        var settings = new Settings { Theme = "HackerGreen" };
        settings.SanitizeAfterLoad();
        Assert.Equal("System", settings.Theme);
    }

    [Theory]
    [InlineData("System")]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void SanitizeAfterLoad_SupportedTheme_Preserved(string theme)
    {
        var settings = new Settings { Theme = theme };
        settings.SanitizeAfterLoad();
        Assert.Equal(theme, settings.Theme);
    }

    [Fact]
    public void SanitizeAfterLoad_UnknownCompressionFormat_FallsBackToZip()
    {
        var settings = new Settings { CompressionFormat = "RAR" }; // Lhamiel は RAR を圧縮できない
        settings.SanitizeAfterLoad();
        Assert.Equal("ZIP", settings.CompressionFormat);
    }

    [Fact]
    public void SupportedThemes_ContainsAllThreeValues()
    {
        Assert.Contains("System", Settings.SupportedThemes);
        Assert.Contains("Dark", Settings.SupportedThemes);
        Assert.Contains("Light", Settings.SupportedThemes);
        Assert.Equal(3, Settings.SupportedThemes.Length);
    }
}
