using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// Settings の更新関連プロパティ（Check4UpdatesOnStartup / IgnoreUpdateTag / UpdateChannel / UpdateBaseUrl）と、
/// SanitizeAfterLoad・ResetToDefaults・Snapshot・JSON 直列化の正規化契約のテスト。
/// 旧 VelopackIntegrationHappyTests / VelopackIntegrationAdversarialTests から内容別に分離した。
/// </summary>
public class SettingsUpdatePropertiesTests
{
    /// <summary>
    /// Settings の新規プロパティのデフォルト値: Check4UpdatesOnStartup=true, IgnoreUpdateTag="".
    /// </summary>
    [Fact]
    public void Settings_DefaultValues_Check4UpdatesOnStartupIsTrue_IgnoreUpdateTagIsEmpty()
    {
        // Arrange & Act
        var settings = new Settings();

        // Assert
        Assert.True(settings.Check4UpdatesOnStartup);
        Assert.Equal(string.Empty, settings.IgnoreUpdateTag);
    }

    /// <summary>
    /// SanitizeAfterLoad() が IgnoreUpdateTag の前後の空白を Trim すること。
    /// </summary>
    [Fact]
    public void Settings_SanitizeAfterLoad_TrimsIgnoreUpdateTag()
    {
        // Arrange
        var settings = new Settings
        {
            IgnoreUpdateTag = "   v1.2.3   "
        };

        // Act
        settings.SanitizeAfterLoad();

        // Assert
        Assert.Equal("v1.2.3", settings.IgnoreUpdateTag);
    }

    /// <summary>
    /// ResetToDefaults() で Check4UpdatesOnStartup / IgnoreUpdateTag が既定値に戻ること。
    /// </summary>
    [Fact]
    public void Settings_ResetToDefaults_RestoresCheck4UpdatesAndIgnoreTag()
    {
        // Arrange
        var settings = new Settings
        {
            Check4UpdatesOnStartup = false,
            IgnoreUpdateTag = "v9.9.9"
        };

        // Act
        settings.ResetToDefaults();

        // Assert
        Assert.True(settings.Check4UpdatesOnStartup);
        Assert.Equal(string.Empty, settings.IgnoreUpdateTag);
    }

    /// <summary>
    /// IgnoreUpdateTag に null が代入されても SanitizeAfterLoad が "" に正規化する (NRE 防止)。
    /// </summary>
    [Fact]
    public void Settings_IgnoreUpdateTagNull_NormalizedToEmpty()
    {
        // Arrange: Reflection で non-nullable string プロパティに null を強制代入
        var settings = new Settings();
        typeof(Settings)
            .GetProperty(nameof(Settings.IgnoreUpdateTag))!
            .SetValue(settings, null);

        // Act
        settings.SanitizeAfterLoad();

        // Assert
        Assert.NotNull(settings.IgnoreUpdateTag);
        Assert.Equal(string.Empty, settings.IgnoreUpdateTag);
    }

    /// <summary>
    /// IgnoreUpdateTag の長さ境界 (256 / 257 / 巨大) に対する正規化挙動を網羅。
    /// 256 は保持、257 から破棄される off-by-one 検出。
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(255, true)]
    [InlineData(256, true)]   // ぎりぎり保持
    [InlineData(257, false)]  // 一文字超過で破棄
    [InlineData(100_000, false)]
    public void Settings_IgnoreUpdateTag_BoundaryLength_BehaviorMatrix(int length, bool shouldKeep)
    {
        // Arrange
        var tag = new string('v', length);
        var settings = new Settings { IgnoreUpdateTag = tag };

        // Act
        settings.SanitizeAfterLoad();

        // Assert
        if (shouldKeep)
            Assert.Equal(tag, settings.IgnoreUpdateTag);
        else
            Assert.Equal(string.Empty, settings.IgnoreUpdateTag);
    }

    /// <summary>
    /// IgnoreUpdateTag に制御文字 (\0〜\x1F) が含まれる場合は空に正規化される (ログインジェクション防御)。
    /// </summary>
    [Theory]
    [InlineData("v1.0.166\0", true)]
    [InlineData("v1.0.166\x01", true)]
    [InlineData("v1.0.166\x1F", true)]
    [InlineData("v1.0.166\r\n", true)]
    [InlineData("v1.0.166\t", true)]
    [InlineData("\x1Fv1.0.166", true)]
    [InlineData("v1.0.166 ", false)]  // 空白は制御範囲外、Trim される
    public void Settings_IgnoreUpdateTagControlCharacters_AlwaysCleared(string input, bool expectEmpty)
    {
        // Arrange
        var settings = new Settings { IgnoreUpdateTag = input };

        // Act
        settings.SanitizeAfterLoad();

        // Assert
        if (expectEmpty)
            Assert.Equal(string.Empty, settings.IgnoreUpdateTag);
        else
            Assert.False(string.IsNullOrEmpty(settings.IgnoreUpdateTag));
    }

    /// <summary>
    /// サロゲートペア・RTL マーク・ゼロ幅スペースは制御範囲外なので保持される (過剰防御回避)。
    /// </summary>
    [Theory]
    [InlineData("v1.0.166🎯")]  // サロゲートペア (🎯)
    [InlineData("v1.0.166​")]         // ゼロ幅スペース
    [InlineData("v1.0.166‮")]         // RTL Override
    public void Settings_IgnoreUpdateTag_UnicodeNonControl_Preserved(string input)
    {
        // Arrange
        var settings = new Settings { IgnoreUpdateTag = input };

        // Act
        settings.SanitizeAfterLoad();

        // Assert
        Assert.Equal(input.Trim(), settings.IgnoreUpdateTag);
    }

    /// <summary>
    /// 空 JSON `{}` は JsonSerializer で成功し全プロパティがデフォルト値になる。
    /// </summary>
    [Fact]
    public void Settings_EmptyJsonObject_AllPropertiesAtDefaults()
    {
        // Arrange
        const string json = "{}";

        // Act
        var s = JsonSerializer.Deserialize(json, AppJsonContext.Default.Settings);

        // Assert
        Assert.NotNull(s);
        var defaults = new Settings();
        Assert.Equal(defaults.Theme, s!.Theme);
        Assert.Equal(defaults.IgnoreUpdateTag, s.IgnoreUpdateTag);
        Assert.Equal(defaults.Check4UpdatesOnStartup, s.Check4UpdatesOnStartup);
        Assert.Equal(defaults.UpdateChannel, s.UpdateChannel);
    }

    /// <summary>
    /// UpdateBaseUrl は [JsonIgnore] + getter-only でハードコード固定。
    /// JSON 経由で書き換え不可 (悪意ある攻撃者ホストへの誘導の防御)。
    /// </summary>
    [Fact]
    public void Settings_JsonInjectionForUpdateBaseUrl_IgnoredByJsonIgnore()
    {
        // Arrange
        const string json = """{"UpdateBaseUrl": "https://evil-attacker.example.com"}""";

        // Act
        var s = JsonSerializer.Deserialize(json, AppJsonContext.Default.Settings);

        // Assert
        Assert.NotNull(s);
        Assert.Equal("https://lhamiel.kagayoi.com", s!.UpdateBaseUrl);

        // setter が物理的に存在しないことも保証
        var prop = typeof(Settings).GetProperty(nameof(Settings.UpdateBaseUrl));
        Assert.NotNull(prop);
        Assert.False(prop!.CanWrite);
    }

    /// <summary>
    /// Turkish ロケール (tr-TR) で Settings.SanitizeAfterLoad の UpdateChannel 比較が
    /// OrdinalIgnoreCase で動くこと (Turkish I 問題退行検知)。
    /// UpdateChecker.TryBuildUpdateManager 自体は VelopackLocator 未初期化で投げるためここでは呼ばない。
    /// </summary>
    [Fact]
    public void Settings_TurkishLocale_PrereleaseChannelNormalizedCorrectly()
    {
        // Arrange
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            var settings = new Settings { UpdateChannel = "PRERELEASE" };

            // Act
            settings.SanitizeAfterLoad();

            // Assert: tr-TR でも canonical 小文字に正規化される
            Assert.Equal("prerelease", settings.UpdateChannel);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>
    /// Settings JSON シリアライズが Invariant Culture で数値を出力すること (fr-FR / ar-SA 等で壊れない)。
    /// </summary>
    [Fact]
    public void Settings_JsonSerialize_InvariantAcrossLocales()
    {
        // Arrange
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var loc in new[] { "fr-FR", "ar-SA", "ja-JP", "en-US" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(loc);

                var s = new Settings
                {
                    LogMaxSizeMB = 12345,
                    LogRetentionDays = 99,
                    ZipCompressionLevel = 7,
                };

                // Act
                var json = JsonSerializer.Serialize(s, AppJsonContext.Default.Settings);

                // Assert: Invariant 形式 (桁区切り無し)
                Assert.Contains("12345", json);
                Assert.DoesNotContain("12,345", json);
                Assert.DoesNotContain("12.345", json);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>
    /// SanitizeAfterLoad → ResetToDefaults と ResetToDefaults → SanitizeAfterLoad が同じ defaults に収束。
    /// </summary>
    [Fact]
    public void Settings_SanitizeAndReset_OrderIndependent()
    {
        // Arrange: Path A
        var pathA = new Settings
        {
            Theme = "HackerGreen",
            UpdateChannel = "evil",
            CompressionFormat = "RAR",
            IgnoreUpdateTag = new string('x', 1024),
        };
        // Path B
        var pathB = new Settings
        {
            Theme = "HackerGreen",
            UpdateChannel = "evil",
            CompressionFormat = "RAR",
            IgnoreUpdateTag = new string('x', 1024),
        };

        // Act
        pathA.SanitizeAfterLoad();
        pathA.ResetToDefaults();

        pathB.ResetToDefaults();
        pathB.SanitizeAfterLoad();

        // Assert
        Assert.Equal("System", pathA.Theme);
        Assert.Equal("System", pathB.Theme);
        Assert.Equal("release", pathA.UpdateChannel);
        Assert.Equal("release", pathB.UpdateChannel);
        Assert.Equal("", pathA.IgnoreUpdateTag);
        Assert.Equal("", pathB.IgnoreUpdateTag);
    }

    /// <summary>
    /// Snapshot 取得後に元 Settings の値型プロパティを変更しても snapshot 側が不変であることを検証する。
    /// （除外パターンは .lhaignore ファイルに移行済みのため Settings の状態ではなくなった）
    /// </summary>
    [Fact]
    public void Settings_Snapshot_IsolatedFromSubsequentMutation()
    {
        // Arrange
        var original = new Settings
        {
            Theme = "Dark",
            IgnoreUpdateTag = "v1.0.0",
        };

        // Act
        var snap = original.Snapshot();
        original.Theme = "Light";
        original.IgnoreUpdateTag = "v2.0.0";

        // Assert: snapshot は影響を受けない
        Assert.Equal("Dark", snap.Theme);
        Assert.Equal("v1.0.0", snap.IgnoreUpdateTag);
    }
}

/// <summary>
/// IgnoreUpdateTag の巨大入力に対する正規化時間のテスト。
/// 時間計測を伴うため、分割前と同じく [Collection("Sequential")] で他テストと並列実行しない。
/// </summary>
[Collection("Sequential")]
public class SettingsUpdateTagResourceTests
{
    /// <summary>
    /// IgnoreUpdateTag に 1MB の巨大文字列を入れても SanitizeAfterLoad が O(1) length check で即正規化 ("" に)。
    /// </summary>
    [Fact]
    public void Settings_SanitizeAfterLoad_With1MBIgnoreUpdateTag_NormalizesToEmptyQuickly()
    {
        // Arrange
        var settings = new Settings
        {
            IgnoreUpdateTag = new string('A', 1_000_000)
        };

        // Act
        var sw = Stopwatch.StartNew();
        settings.SanitizeAfterLoad();
        sw.Stop();

        // Assert
        Assert.Equal("", settings.IgnoreUpdateTag);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1),
            $"SanitizeAfterLoad が遅すぎる: {sw.Elapsed}");
    }
}
