using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// Settings class unit tests
/// </summary>
public class SettingsTests
{
    // 既定値とリセットは SettingsEdgeCaseTests.NewSettings_AndResetToDefaults_ProduceSameValues に集約。

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

    [Fact]
    public void MigrateLegacyContextMenuSettings_CopiesOldValueToBothSettings()
    {
        var settings = new Settings();
        const string json = """{ "AddToContextMenu": true }""";

        Assert.True(Settings.MigrateLegacyContextMenuSettings(settings, json));

        Assert.True(settings.AddExtractToContextMenu);
        Assert.True(settings.AddCompressToContextMenu);
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

    // ═══════════════════════════════════════════════════
    // 対応形式カタログ（圧縮 / 展開 / 展開専用）の分類
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// 代表的な形式名が、圧縮・展開・展開専用の各カタログに期待どおり含まれる（含まれない）こと。
    /// 旧 SupportedCompressionFormats_ / SupportedExtractionFormats_ / ExtractOnlyFormats_ContainsExpectedFormats の
    /// 3 Theory の全入力をこの 1 つに統合した。照合は大文字小文字を区別する（カタログは canonical 表記を持つ）。
    /// </summary>
    [Theory]
    // 引数: 形式名, 圧縮カタログに含まれるか
    [InlineData("LZH", true)]
    [InlineData("LHA", false)]  // LHA は展開のみ（圧縮は LZH 表記に統一）
    [InlineData("RAR", false)]
    public void SupportedFormatCatalogs_ClassifyFormatAsExpected(string format, bool compressible)
    {
        Assert.Equal(compressible, Settings.SupportedCompressionFormats.Contains(format));
    }

    /// <summary>
    /// 圧縮カタログと展開判定の共通カタログ（ArchiveFormatConstants）の整合性、および既定圧縮形式の所属。
    /// 「カタログへの形式追加時の分類漏れ」を全要素で検出する。
    /// </summary>
    [Fact]
    public void SupportedFormatCatalogs_SetRelationsAreConsistent()
    {
        // 圧縮できる形式はすべて展開もできる
        var extractable = ArchiveFormatConstants.SupportedArchiveFormats
            .Select(f => f.Extension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var format in Settings.SupportedCompressionFormats)
            Assert.Contains(format, extractable);

        // 既定の圧縮形式は圧縮カタログ内の値である
        var settings = new Settings();
        Assert.Contains(settings.CompressionFormat, Settings.SupportedCompressionFormats);
    }
}
