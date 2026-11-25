using System.Globalization;
using System.Text.Json;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// Settings の更新関連プロパティ（IgnoreUpdateTag / UpdateChannel / UpdateBaseUrl）の
/// SanitizeAfterLoad・JSON 読込の正規化契約のテスト。
/// 既定値とリセットは SettingsEdgeCaseTests.NewSettings_AndResetToDefaults_ProduceSameValues に集約している。
/// </summary>
public class SettingsUpdatePropertiesTests
{
    /// <summary>
    /// SanitizeAfterLoad() が IgnoreUpdateTag の前後の空白を Trim すること。
    /// </summary>
    [Fact]
    public void Settings_SanitizeAfterLoad_TrimsIgnoreUpdateTag()
    {
        var settings = new Settings
        {
            IgnoreUpdateTag = "   v1.2.3   "
        };

        settings.SanitizeAfterLoad();

        Assert.Equal("v1.2.3", settings.IgnoreUpdateTag);
    }

    /// <summary>
    /// IgnoreUpdateTag に null が代入されても SanitizeAfterLoad が "" に正規化する (NRE 防止)。
    /// </summary>
    [Fact]
    public void Settings_IgnoreUpdateTagNull_NormalizedToEmpty()
    {
        // Reflection で non-nullable string プロパティに null を強制代入
        var settings = new Settings();
        typeof(Settings)
            .GetProperty(nameof(Settings.IgnoreUpdateTag))!
            .SetValue(settings, null);

        settings.SanitizeAfterLoad();

        Assert.NotNull(settings.IgnoreUpdateTag);
        Assert.Equal(string.Empty, settings.IgnoreUpdateTag);
    }

    /// <summary>
    /// IgnoreUpdateTag の長さ境界。256 は保持、257 から破棄される off-by-one 検出。
    /// </summary>
    [Theory]
    [InlineData(256, true)]   // ぎりぎり保持
    [InlineData(257, false)]  // 一文字超過で破棄
    public void Settings_IgnoreUpdateTag_BoundaryLength_BehaviorMatrix(int length, bool shouldKeep)
    {
        var tag = new string('v', length);
        var settings = new Settings { IgnoreUpdateTag = tag };

        settings.SanitizeAfterLoad();

        Assert.Equal(shouldKeep ? tag : string.Empty, settings.IgnoreUpdateTag);
    }

    /// <summary>
    /// IgnoreUpdateTag に制御文字 (\0〜\x1F) が含まれる場合は空に正規化される (ログインジェクション防御)。
    /// 範囲の両端を確認する。
    /// </summary>
    [Theory]
    [InlineData("v1.0.166\0")]
    [InlineData("v1.0.166\x1F")]
    public void Settings_IgnoreUpdateTagControlCharacters_AlwaysCleared(string input)
    {
        var settings = new Settings { IgnoreUpdateTag = input };

        settings.SanitizeAfterLoad();

        Assert.Equal(string.Empty, settings.IgnoreUpdateTag);
    }

    /// <summary>
    /// UpdateBaseUrl は [JsonIgnore] + getter-only でハードコード固定。
    /// JSON 経由で書き換え不可 (悪意ある攻撃者ホストへの誘導の防御)。
    /// </summary>
    [Fact]
    public void Settings_JsonInjectionForUpdateBaseUrl_IgnoredByJsonIgnore()
    {
        const string json = """{"UpdateBaseUrl": "https://evil-attacker.example.com"}""";

        var s = JsonSerializer.Deserialize(json, AppJsonContext.Default.Settings);

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
    /// 通常ロケールでの大文字小文字の正規化（RELEASE → release）も同じ入力で確認する。
    /// </summary>
    [Fact]
    public void Settings_TurkishLocale_PrereleaseChannelNormalizedCorrectly()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var settings = new Settings { UpdateChannel = "RELEASE" };
            settings.SanitizeAfterLoad();
            Assert.Equal("release", settings.UpdateChannel);

            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            settings = new Settings { UpdateChannel = "PRERELEASE" };
            settings.SanitizeAfterLoad();

            // tr-TR でも canonical 小文字に正規化される
            Assert.Equal("prerelease", settings.UpdateChannel);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
