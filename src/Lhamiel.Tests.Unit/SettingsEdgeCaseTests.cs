using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// Settings の追加エッジケーステスト
/// </summary>
public class SettingsEdgeCaseTests
{
    /// <summary>
    /// 既定値の正本。新規インスタンスとリセット後の両方が、この値であること。
    /// （旧 DefaultValues 系・ResetToDefaults 系テストをここへ集約した。）
    /// </summary>
    private static void AssertDefaultValues(Settings s)
    {
        Assert.Equal("ZIP", s.CompressionFormat);
        Assert.Equal("System", s.Theme);
        Assert.Equal("", s.Locale);
        Assert.Equal("release", s.UpdateChannel);
        Assert.Equal("https://lhamiel.kagayoi.com", s.UpdateBaseUrl);
        Assert.True(s.Check4UpdatesOnStartup);
        Assert.Equal(string.Empty, s.IgnoreUpdateTag);
        Assert.False(s.ExtractionOutputToSameDirectory);
        Assert.False(s.CompressionOutputToSameDirectory);
        Assert.False(s.AddExtractToContextMenu);
        Assert.False(s.AddCompressToContextMenu);
        Assert.True(s.CompressMultipleAsOne);
        Assert.Equal(5, s.ZipCompressionLevel);
        Assert.Equal(5, s.SevenZipCompressionLevel);
        Assert.Equal("LH5", s.LzhCompressionMethod);
        Assert.True(s.OpenExtractionOutputFolder);
        Assert.True(s.OpenCompressionOutputFolder);
        Assert.True(s.IncludeHiddenAndSystemEntries);
        Assert.False(s.RespectNestedGitignore);
        Assert.Equal([".gitignore"], s.SourceIgnoreFileNames);
        Assert.Equal(10, s.LogMaxSizeMB);
        Assert.Equal(7, s.LogRetentionDays);
        Assert.Equal(Settings.AppIconVariantClassic, s.AppIconVariant);
        Assert.Equal(Settings.FileIconVariantClassic, s.FileIconVariant);
        Assert.False(s.IsPasswordProtectionEnabled);
        Assert.Equal("PromptEachTime", s.PasswordMode);
        Assert.Null(s.EncryptedCompressionPassword);
        Assert.True(s.EncryptFileNames);
    }

    [Fact]
    public void NewSettings_AndResetToDefaults_ProduceSameValues()
    {
        var fresh = new Settings();
        var reset = new Settings
        {
            CompressionFormat = "7z",
            Theme = "Dark",
            Locale = "ja_JP",
            UpdateChannel = "beta",
            Check4UpdatesOnStartup = false,
            IgnoreUpdateTag = "v9.9.9",
            CreateArchiveNameFolder = false,
            DirectoryStructureMode = DirectoryStructureMode.Flat,
            ExtractionOutputToSameDirectory = true,
            CompressionOutputToSameDirectory = true,
            AddExtractToContextMenu = true,
            AddCompressToContextMenu = true,
            CompressMultipleAsOne = false,
            ZipCompressionLevel = 9,
            SevenZipCompressionLevel = 0,
            LzhCompressionMethod = "LH7",
            OpenExtractionOutputFolder = false,
            OpenCompressionOutputFolder = false,
            IncludeHiddenAndSystemEntries = false,
            RespectNestedGitignore = true,
            LogMaxSizeMB = 50,
            LogRetentionDays = 30,
            SourceIgnoreFileNames = [".lhamielignore", ".gitignore"],
            AppIconVariant = Settings.AppIconVariantCrystal,
            FileIconVariant = Settings.FileIconVariantIce,
            IsPasswordProtectionEnabled = true,
            PasswordMode = "Remember",
            EncryptedCompressionPassword = [0x01, 0x02, 0x03],
            EncryptFileNames = false,
        };
        reset.ResetToDefaults();

        AssertDefaultValues(fresh);
        AssertDefaultValues(reset);
        Assert.Equal(fresh.CompressionFormat, reset.CompressionFormat);
        Assert.Equal(fresh.Theme, reset.Theme);
        Assert.Equal(fresh.Locale, reset.Locale);
        Assert.Equal(fresh.CompressMultipleAsOne, reset.CompressMultipleAsOne);
        Assert.Equal(fresh.ZipCompressionLevel, reset.ZipCompressionLevel);
        Assert.Equal(fresh.SevenZipCompressionLevel, reset.SevenZipCompressionLevel);
        Assert.Equal(fresh.LzhCompressionMethod, reset.LzhCompressionMethod);
        Assert.Equal(fresh.OpenExtractionOutputFolder, reset.OpenExtractionOutputFolder);
        Assert.Equal(fresh.OpenCompressionOutputFolder, reset.OpenCompressionOutputFolder);
        Assert.Equal(fresh.IncludeHiddenAndSystemEntries, reset.IncludeHiddenAndSystemEntries);
        Assert.Equal(fresh.LogMaxSizeMB, reset.LogMaxSizeMB);
        Assert.Equal(fresh.LogRetentionDays, reset.LogRetentionDays);
        Assert.Equal(fresh.ExtractionOutputToSameDirectory, reset.ExtractionOutputToSameDirectory);
        Assert.Equal(fresh.CompressionOutputToSameDirectory, reset.CompressionOutputToSameDirectory);
        // v1.0.160 で導入 → 同 ver 取り下げ → 再リリースで再導入: ResetToDefaults の漏れ修正を検証
        Assert.Equal(fresh.CreateArchiveNameFolder, reset.CreateArchiveNameFolder);
        Assert.Equal(fresh.DirectoryStructureMode, reset.DirectoryStructureMode);
        Assert.Equal(fresh.AddExtractToContextMenu, reset.AddExtractToContextMenu);
        Assert.Equal(fresh.AddCompressToContextMenu, reset.AddCompressToContextMenu);
        Assert.Equal(fresh.SourceIgnoreFileNames, reset.SourceIgnoreFileNames);
        Assert.Equal(fresh.AppIconVariant, reset.AppIconVariant);
    }

    [Fact]
    public void Settings_LegacyExcludedFilePatterns_EmptyArrayIsPreserved()
    {
        // 旧 settings.json で `ExcludedFilePatterns: []` (意図的に空) を持つユーザーが
        // アップグレードした際、空配列が null 扱いされてデフォルト除外パターンに置き換わると
        // 「ユーザーが意図的に除外なし」設定を壊してしまうので、setter は空配列も保持する。
        // (Codex P1 指摘の回帰テスト)
        var s = new Settings();
        s.ExcludedFilePatternsLegacy = new List<string>();
        Assert.NotNull(s._legacyExcludedFilePatterns);
        Assert.Empty(s._legacyExcludedFilePatterns!);
    }

    // 対応形式カタログ間の集合関係と既定圧縮形式の所属は
    // SettingsTests.SupportedFormatCatalogs_SetRelationsAreConsistent に集約。

    // === SanitizeAfterLoad（v1.0.160 で追加 → 同 ver 取り下げ → 再リリースで再導入） ===

    [Fact]
    public void SanitizeAfterLoad_UnknownUpdateChannel_FallsBackToRelease()
    {
        var settings = new Settings { UpdateChannel = "../../../evil" };
        settings.SanitizeAfterLoad();
        Assert.Equal("release", settings.UpdateChannel);
    }

    [Fact]
    public void SanitizeAfterLoad_Theme_NormalizedToCanonicalCase()
    {
        // 大文字小文字違いは canonical 表記へ。アイコン種別も同じ経路で確認する。
        var settings = new Settings { Theme = "DARK", FileIconVariant = "ICE" };
        settings.SanitizeAfterLoad();
        Assert.Equal("Dark", settings.Theme);
        Assert.Equal("Ice", settings.FileIconVariant);
    }

    [Fact]
    public void TryRecoverFromJsonDocument_PreservesLzhCompressionMethodWhenAnotherPropertyIsInvalid()
    {
        const string json = """{ "Theme": 123, "LzhCompressionMethod": "lh6" }""";

        var restored = Settings.TryRecoverFromJsonDocument(json);

        Assert.NotNull(restored);
        restored.SanitizeAfterLoad();
        Assert.Equal("LH6", restored.LzhCompressionMethod);
    }

    [Theory]
    [InlineData(999, 9)]
    [InlineData(-1, 0)]
    [InlineData(int.MinValue, 0)] // Load 時オーバーフロー防御 (改竄 settings.json の最悪値)
    [InlineData(2, 1)]
    [InlineData(7, 7)] // 有効値はそのまま
    public void SanitizeAfterLoad_OutOfRangeCompressionLevel_SnappedToValid(int input, int expected)
    {
        // settings.json 改竄/旧ビルド/手書きで範囲外値が入っても、ArchiveCompressor の
        // (CompressionLevel)int 直キャストに渡る前に有効値へ矯正されることを保証する
        // (VM を通さない CLI/シェル圧縮経路の防御線)。
        var settings = new Settings { ZipCompressionLevel = input, SevenZipCompressionLevel = input };
        settings.SanitizeAfterLoad();
        Assert.Equal(expected, settings.ZipCompressionLevel);
        Assert.Equal(expected, settings.SevenZipCompressionLevel);
        Assert.Contains(settings.ZipCompressionLevel, Settings.ValidCompressionLevels);
        Assert.Contains(settings.SevenZipCompressionLevel, Settings.ValidCompressionLevels);
    }

    [Fact]
    public void SanitizeAfterLoad_UnmountedDriveOutputDirectory_PreservedNotOverwritten()
    {
        // 回帰防止: ユーザーが NAS / USB / リムーバブルドライブ等を出力先に設定しており、
        // 起動時にそのドライブが未接続でも、設定値はサニタイズで上書きされず保持されること。
        // 旧実装は Directory.Exists で false → Desktop に強制リセットしていたが、
        // その後 AutoSave で settings.json も上書きされて永続化破壊（ドライブ再接続後も
        // 元の設定が失われる）の経路があった。
        // 構文妥当性のみ検証し、Directory.Exists は実書き込み時に行う設計に変更。
        var path = @"Z:\NonExistent\Path\xyz_not_real";
        var settings = new Settings { ExtractionOutputDirectory = path };
        settings.SanitizeAfterLoad();
        Assert.Equal(path, settings.ExtractionOutputDirectory);
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
        Assert.SkipUnless(Directory.Exists(desktop), "テスト環境に Desktop フォルダが存在しないためスキップ");
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
        Assert.SkipUnless(Directory.Exists(windows), "テスト環境に Windows フォルダが存在しないためスキップ");
        var settings = new Settings { ExtractionOutputDirectory = windows };
        settings.SanitizeAfterLoad();
        Assert.Equal(desktop, settings.ExtractionOutputDirectory);
    }

    [Fact]
    public void SanitizeAfterLoad_UnknownCompressionFormat_FallsBackToZip()
    {
        // 未知値はすべて既定値へ矯正される（形式・テーマ・アイコン種別・LZH 方式）。
        var settings = new Settings
        {
            CompressionFormat = "RAR", // Lhamiel は RAR を圧縮できない
            Theme = "HackerGreen",
            FileIconVariant = "MetalBoxArrow",
            LzhCompressionMethod = "LH4",
        };
        settings.SanitizeAfterLoad();
        Assert.Equal("ZIP", settings.CompressionFormat);
        Assert.Equal("System", settings.Theme);
        Assert.Equal(Settings.FileIconVariantClassic, settings.FileIconVariant);
        Assert.Equal("LH5", settings.LzhCompressionMethod);
    }
}
