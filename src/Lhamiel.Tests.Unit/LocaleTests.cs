using Lhamiel.ViewModels;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// ロケール関連のテスト
/// </summary>
public class LocaleTests
{
    [Fact]
    public void SupportedLocales_AllHaveDisplayNames()
    {
        // 全サポートロケールに対応する表示名が定義されていること
        foreach (var locale in App.SupportedLocales)
        {
            Assert.True(
                App.LocaleDisplayNames.ContainsKey(locale),
                $"ロケール '{locale}' の表示名が LocaleDisplayNames に定義されていません");
        }
    }

    [Fact]
    public void LocaleDisplayNames_AllAreInSupportedLocales()
    {
        // LocaleDisplayNames に定義されたキーがすべて SupportedLocales に含まれること
        foreach (var key in App.LocaleDisplayNames.Keys)
        {
            Assert.Contains(key, App.SupportedLocales);
        }
    }

    [Fact]
    public void SupportedLocales_CountMatchesDisplayNames()
    {
        Assert.Equal(App.SupportedLocales.Length, App.LocaleDisplayNames.Count);
    }

    [Fact]
    public void SupportedLocales_ContainsExpectedLocales()
    {
        // 主要ロケールが含まれていること
        Assert.Contains("en_US", App.SupportedLocales);
        Assert.Contains("ja_JP", App.SupportedLocales);
        Assert.Contains("zh_CN", App.SupportedLocales);
        Assert.Contains("ko_KR", App.SupportedLocales);
        Assert.Contains("la_VA", App.SupportedLocales);
        Assert.Contains("sa_IN", App.SupportedLocales);
    }

    [Fact]
    public void SupportedLocales_HasNoDuplicates()
    {
        var distinct = App.SupportedLocales.Distinct().ToArray();
        Assert.Equal(App.SupportedLocales.Length, distinct.Length);
    }

    [Theory]
    [InlineData("en_US", "English")]
    [InlineData("ja_JP", "日本語")]
    [InlineData("la_VA", "Latina")]
    [InlineData("sa_IN", "संस्कृतम्")]
    [InlineData("fil_PH", "Tagalog")]
    public void LocaleDisplayNames_ReturnsCorrectName(string locale, string expectedName)
    {
        Assert.Equal(expectedName, App.LocaleDisplayNames[locale]);
    }

    [Theory]
    [InlineData("en_US")]
    [InlineData("ja_JP")]
    [InlineData("zh_CN")]
    [InlineData("zh_TW")]
    [InlineData("de_DE")]
    [InlineData("fr_FR")]
    [InlineData("es_ES")]
    [InlineData("it_IT")]
    [InlineData("pt_BR")]
    [InlineData("ru_RU")]
    [InlineData("uk_UA")]
    [InlineData("id_ID")]
    [InlineData("fil_PH")]
    [InlineData("ta_IN")]
    [InlineData("ko_KR")]
    [InlineData("la_VA")]
    [InlineData("sa_IN")]
    public void SupportedLocales_FollowsNamingConvention(string locale)
    {
        // ロケールコードが xx_XX 形式であること
        Assert.Matches(@"^[a-z]{2,3}_[A-Z]{2}$", locale);
    }

    [Fact]
    public void LocaleOptions_MatchesSupportedLocales()
    {
        // MainWindowViewModel.LocaleOptions が SupportedLocales と一致すること
        var localeOptionKeys = MainWindowViewModel.LocaleOptions.Select(o => o.Key).ToArray();
        Assert.Equal(App.SupportedLocales, localeOptionKeys);
    }

    [Fact]
    public void LocaleOptions_DisplayNamesNotEmpty()
    {
        foreach (var option in MainWindowViewModel.LocaleOptions)
        {
            Assert.False(string.IsNullOrWhiteSpace(option.DisplayName),
                $"ロケール '{option.Key}' の表示名が空です");
        }
    }

    [Fact]
    public void DetectDefaultLocale_ReturnsSupportedLocale()
    {
        // DetectDefaultLocale は常に SupportedLocales の中から返すこと
        var detected = App.DetectDefaultLocale();
        Assert.Contains(detected, App.SupportedLocales);
    }
}
