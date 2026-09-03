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
        Assert.Equal("ZIP", settings.CompressionFormat);
        Assert.False(settings.ExtractionOutputToSameDirectory);
        Assert.False(settings.CompressionOutputToSameDirectory);
        Assert.Equal("https://lhamiel.kagayoi.com", settings.UpdateBaseUrl);
        Assert.Equal("release", settings.UpdateChannel);
        Assert.False(settings.AddExtractToContextMenu);
        Assert.False(settings.AddCompressToContextMenu);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MigrateLegacyContextMenuSettings_CopiesOldValueToBothSettings(bool legacyValue)
    {
        var settings = new Settings();
        var json = $$"""{ "AddToContextMenu": {{legacyValue.ToString().ToLowerInvariant()}} }""";

        Assert.True(Settings.MigrateLegacyContextMenuSettings(settings, json));

        Assert.Equal(legacyValue, settings.AddExtractToContextMenu);
        Assert.Equal(legacyValue, settings.AddCompressToContextMenu);
    }

    [Fact]
    public void MigrateLegacyContextMenuSettings_PreservesExplicitNewValue()
    {
        var settings = new Settings { AddExtractToContextMenu = false };
        const string json = """{ "AddToContextMenu": true, "AddExtractToContextMenu": false }""";

        Assert.True(Settings.MigrateLegacyContextMenuSettings(settings, json));

        Assert.False(settings.AddExtractToContextMenu);
        Assert.True(settings.AddCompressToContextMenu);
    }

    [Fact]
    public void ContextMenuSettings_SerializationUsesOnlyNewKeys()
    {
        var settings = new Settings
        {
            AddExtractToContextMenu = true,
            AddCompressToContextMenu = false,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(settings, AppJsonContext.Default.Settings);

        Assert.Contains("\"AddExtractToContextMenu\"", json, StringComparison.Ordinal);
        Assert.Contains("\"AddCompressToContextMenu\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"AddToContextMenu\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_NewDefaultValues_AreCorrect()
    {
        // v1.0.102 で追加されたプロパティのデフォルト値を検証
        var settings = new Settings();

        Assert.Equal("System", settings.Theme);
        Assert.Equal("", settings.Locale);
        Assert.True(settings.CompressMultipleAsOne);
        Assert.Equal(5, settings.ZipCompressionLevel);
        Assert.Equal(5, settings.SevenZipCompressionLevel);
        Assert.True(settings.OpenExtractionOutputFolder);
        Assert.True(settings.OpenCompressionOutputFolder);
        Assert.True(settings.IncludeHiddenAndSystemEntries);
        Assert.False(settings.RespectNestedGitignore);
        Assert.Equal([".gitignore"], settings.SourceIgnoreFileNames);
        Assert.Equal(10, settings.LogMaxSizeMB);
        Assert.Equal(7, settings.LogRetentionDays);
    }

    [Fact]
    public void LhaignoreFile_DefaultContent_ContainsExpectedPatterns()
    {
        // 旧 ExcludedFilePatterns プロパティの代替: .lhaignore のデフォルト内容に
        // 主要なシステムパターンが含まれることを確認する。
        var content = LhaignoreFile.CreateDefaultContent();

        Assert.Contains(".DS_Store", content, StringComparison.Ordinal);
        Assert.Contains("Thumbs.db", content, StringComparison.Ordinal);
        Assert.Contains("__MACOSX", content, StringComparison.Ordinal);
        Assert.Contains("desktop.ini", content, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceIgnoreFileNames_NormalizePreservesPriorityAndDeduplicates()
    {
        var success = Settings.TryNormalizeSourceIgnoreFileNames(
            [" .lhamielignore ", ".gitignore", ".LHAMIELIGNORE", ""],
            out var result);

        Assert.True(success);
        Assert.Equal([".lhamielignore", ".gitignore"], result);
    }

    [Theory]
    [InlineData("../rules")]
    [InlineData("folder/.gitignore")]
    [InlineData("*.ignore")]
    [InlineData("CON")]
    [InlineData("CON.rules.txt")]
    [InlineData(".")]
    [InlineData("rules.")]
    public void SourceIgnoreFileNames_InvalidNameIsRejected(string invalidName)
    {
        Assert.False(Settings.TryNormalizeSourceIgnoreFileNames([invalidName], out _));
    }

    [Fact]
    public void SanitizeAfterLoad_InvalidSourceIgnoreFileNamesRestoresGitignoreDefault()
    {
        var settings = new Settings { SourceIgnoreFileNames = ["../outside"] };

        settings.SanitizeAfterLoad();

        Assert.Equal([".gitignore"], settings.SourceIgnoreFileNames);
    }

    [Fact]
    public void Snapshot_SourceIgnoreFileNamesAreDeepCopied()
    {
        var settings = new Settings { SourceIgnoreFileNames = [".lhamielignore", ".gitignore"] };

        var snapshot = settings.Snapshot();
        settings.SourceIgnoreFileNames[0] = ".changed";

        Assert.Equal([".lhamielignore", ".gitignore"], snapshot.SourceIgnoreFileNames);
    }

    [Fact]
    public void ResetToDefaults_RestoresDefaultValues()
    {
        // Arrange
        // UpdateBaseUrl はセキュリティ上ハードコード固定のため初期化子で書き換え不可（読み取り専用）。
        var settings = new Settings
        {
            CompressionFormat = "7z",
            ExtractionOutputToSameDirectory = true,
            CompressionOutputToSameDirectory = true,
            UpdateChannel = "beta",
            AddExtractToContextMenu = true,
            AddCompressToContextMenu = true
        };

        // Act
        settings.ResetToDefaults();

        // Assert
        Assert.Equal("ZIP", settings.CompressionFormat);
        Assert.False(settings.ExtractionOutputToSameDirectory);
        Assert.False(settings.CompressionOutputToSameDirectory);
        Assert.Equal("https://lhamiel.kagayoi.com", settings.UpdateBaseUrl);
        Assert.Equal("release", settings.UpdateChannel);
        Assert.False(settings.AddExtractToContextMenu);
        Assert.False(settings.AddCompressToContextMenu);
        Assert.Equal([".gitignore"], settings.SourceIgnoreFileNames);
    }

    [Fact]
    public void UpdateBaseUrl_IsHardcodedAndImmutable()
    {
        // 自動更新の配信元 URL は settings.json で書き換えできない（固定）。
        // 悪意あるユーザーが攻撃者ホスト (R2 / 自前サーバ等) に誘導できないことを担保する回帰テスト。
        var settings = new Settings();
        Assert.Equal("https://lhamiel.kagayoi.com", settings.UpdateBaseUrl);

        // setter が物理的に存在しないことも保証
        var prop = typeof(Settings).GetProperty(nameof(Settings.UpdateBaseUrl));
        Assert.NotNull(prop);
        Assert.False(prop!.CanWrite);
    }

    [Fact]
    public void ResetToDefaults_RestoresNewPropertyValues()
    {
        // v1.0.102 で追加されたプロパティのリセットを検証
        var settings = new Settings
        {
            Theme = "Dark",
            Locale = "ja_JP",
            CompressMultipleAsOne = true,
            ZipCompressionLevel = 9,
            SevenZipCompressionLevel = 0,
            IncludeHiddenAndSystemEntries = false,
            OpenExtractionOutputFolder = false,
            OpenCompressionOutputFolder = false,
            LogMaxSizeMB = 50,
            LogRetentionDays = 30
        };

        // Act
        settings.ResetToDefaults();

        // Assert
        Assert.Equal("System", settings.Theme);
        Assert.Equal("", settings.Locale);
        Assert.True(settings.CompressMultipleAsOne);
        Assert.Equal(5, settings.ZipCompressionLevel);
        Assert.Equal(5, settings.SevenZipCompressionLevel);
        Assert.True(settings.IncludeHiddenAndSystemEntries);
        Assert.True(settings.OpenExtractionOutputFolder);
        Assert.True(settings.OpenCompressionOutputFolder);
        Assert.Equal(10, settings.LogMaxSizeMB);
        Assert.Equal(7, settings.LogRetentionDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(9)]
    public void CompressionLevel_AcceptsValidValues(int level)
    {
        var settings = new Settings { ZipCompressionLevel = level, SevenZipCompressionLevel = level };
        Assert.Equal(level, settings.ZipCompressionLevel);
        Assert.Equal(level, settings.SevenZipCompressionLevel);
    }

    [Theory]
    [InlineData("System")]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Theme_AcceptsValidValues(string theme)
    {
        var settings = new Settings { Theme = theme };
        Assert.Equal(theme, settings.Theme);
    }

    [Theory]
    [InlineData("ZIP", true)]
    [InlineData("7z", true)]
    [InlineData("TAR", true)]
    [InlineData("RAR", false)]
    [InlineData("GZ", false)]
    [InlineData("unknown", false)]
    public void SupportedCompressionFormats_ContainsExpectedFormats(string format, bool shouldBeSupported)
    {
        // Act
        var isSupported = Settings.SupportedCompressionFormats.Contains(format);

        // Assert
        Assert.Equal(shouldBeSupported, isSupported);
    }

    [Theory]
    [InlineData("ZIP", true)]
    [InlineData("7z", true)]
    [InlineData("RAR", true)]
    [InlineData("unknown", false)]
    public void SupportedExtractionFormats_ContainsExpectedFormats(string format, bool shouldBeSupported)
    {
        // Act
        var isSupported = Settings.SupportedExtractionFormats.Contains(format);

        // Assert
        Assert.Equal(shouldBeSupported, isSupported);
    }

    [Theory]
    [InlineData("RAR", true)]
    [InlineData("ARJ", true)]
    [InlineData("Z", true)]
    [InlineData("ZIP", false)]
    [InlineData("7z", false)]
    public void ExtractOnlyFormats_ContainsExpectedFormats(string format, bool shouldBeExtractOnly)
    {
        // Act
        var isExtractOnly = Settings.ExtractOnlyFormats.Contains(format);

        // Assert
        Assert.Equal(shouldBeExtractOnly, isExtractOnly);
    }
}
