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

    // === InitializeLhaignore（旧 ExcludedFilePatterns の .lhaignore 移行）===
    // 実ユーザーの .lhaignore に触れないよう、必ず一時ディレクトリのパスを渡す。

    [Fact]
    public void InitializeLhaignore_WhenCreated_ClearsLegacyPatterns()
    {
        using var temp = TestDirectory.Create("Lhamiel_settings_lhaignore");
        var path = Path.Combine(temp.Path, ".lhaignore");
        var settings = new Settings { ExcludedFilePatternsLegacy = ["legacy.txt"] };

        Settings.InitializeLhaignore(settings, path);

        Assert.True(File.Exists(path));
        Assert.Contains("legacy.txt", File.ReadAllLines(path));
        Assert.Null(settings.ExcludedFilePatternsLegacy);
    }

    [Fact]
    public void InitializeLhaignore_WhenCreationFails_KeepsLegacyPatterns()
    {
        // 親パスがファイルで .lhaignore を作れない失敗経路。次回 Save で旧値が消えて復元不能に
        // ならないよう、レガシー値を温存すること。
        using var temp = TestDirectory.Create("Lhamiel_settings_lhaignore");
        var blocker = Path.Combine(temp.Path, "blocker");
        File.WriteAllText(blocker, "x");
        var path = Path.Combine(blocker, ".lhaignore");
        List<string> legacy = ["keep-me.txt"];
        var settings = new Settings { ExcludedFilePatternsLegacy = legacy };

        Settings.InitializeLhaignore(settings, path);

        Assert.False(File.Exists(path));
        Assert.Same(legacy, settings.ExcludedFilePatternsLegacy);
    }

    [Fact]
    public void InitializeLhaignore_WhenFileAlreadyExists_DoesNotTouchFileOrLegacy()
    {
        // 既存の .lhaignore があれば何もしない（従来どおり）。内容は上書きされない。
        using var temp = TestDirectory.Create("Lhamiel_settings_lhaignore");
        var path = Path.Combine(temp.Path, ".lhaignore");
        File.WriteAllText(path, "user-rule/");
        List<string> legacy = ["legacy.txt"];
        var settings = new Settings { ExcludedFilePatternsLegacy = legacy };

        Settings.InitializeLhaignore(settings, path);

        Assert.Equal("user-rule/", File.ReadAllText(path));
        Assert.Same(legacy, settings.ExcludedFilePatternsLegacy);
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

    // ═══════════════════════════════════════════════════
    // 対応形式カタログ（圧縮 / 展開 / 展開専用）の分類
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 代表的な形式名が、圧縮・展開・展開専用の各カタログに期待どおり含まれる（含まれない）こと。
    /// 旧 SupportedCompressionFormats_ / SupportedExtractionFormats_ / ExtractOnlyFormats_ContainsExpectedFormats の
    /// 3 Theory の全入力をこの 1 つに統合した。照合は大文字小文字を区別する（カタログは canonical 表記を持つ）。
    /// </summary>
    [Theory]
    // 引数: 形式名, 圧縮カタログに含まれるか, 展開カタログに含まれるか, 展開専用カタログに含まれるか
    [InlineData("ZIP", true, true, false)]
    [InlineData("7z", true, true, false)]
    [InlineData("TAR", true, true, false)]
    [InlineData("LZH", true, true, false)]
    [InlineData("LHA", false, true, false)]  // LHA は展開のみ（圧縮は LZH 表記に統一）
    [InlineData("GZ", false, true, false)]  // 単体圧縮形式は展開のみだが展開専用カタログには載せない
    [InlineData("RAR", false, true, true)]
    [InlineData("ARJ", false, true, true)]
    [InlineData("Z", false, true, true)]
    [InlineData("unknown", false, false, false)]
    public void SupportedFormatCatalogs_ClassifyFormatAsExpected(
        string format, bool compressible, bool extractable, bool extractOnly)
    {
        Assert.Equal(compressible, Settings.SupportedCompressionFormats.Contains(format));
        Assert.Equal(extractable, Settings.SupportedExtractionFormats.Contains(format));
        Assert.Equal(extractOnly, Settings.ExtractOnlyFormats.Contains(format));
    }

    /// <summary>
    /// カタログ間の集合関係と既定圧縮形式の整合性。
    /// 上の代表値 Theory では検出できない「カタログへの形式追加時の分類漏れ」を全要素で検出する。
    /// （旧 SettingsEdgeCaseTests の SupportedCompressionFormats_IsSubsetOfExtractionFormats /
    /// ExtractOnlyFormats_NotInSupportedCompressionFormats / ExtractOnlyFormats_AreInSupportedExtractionFormats /
    /// DefaultCompressionFormat_IsInSupportedCompressionFormats を統合）
    /// </summary>
    [Fact]
    public void SupportedFormatCatalogs_SetRelationsAreConsistent()
    {
        // 圧縮できる形式はすべて展開もできる
        foreach (var format in Settings.SupportedCompressionFormats)
            Assert.Contains(format, Settings.SupportedExtractionFormats);

        // 展開専用形式は圧縮カタログに含まれず、展開カタログには含まれる
        foreach (var format in Settings.ExtractOnlyFormats)
        {
            Assert.DoesNotContain(format, Settings.SupportedCompressionFormats);
            Assert.Contains(format, Settings.SupportedExtractionFormats);
        }

        // 既定の圧縮形式は圧縮カタログ内の値である
        var settings = new Settings();
        Assert.Contains(settings.CompressionFormat, Settings.SupportedCompressionFormats);
    }
}
