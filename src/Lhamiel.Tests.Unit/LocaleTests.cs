using System.Xml.Linq;
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

    // ═══════════════════════════════════════════════════
    // ローカライズキー整合性テスト
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// ロケールファイルのディレクトリを取得する。
    /// テストプロジェクトから相対パスで src/Lhamiel/Resources/Locales/ を探す。
    /// </summary>
    private static string GetLocalesDirectory()
    {
        // テスト実行ディレクトリから遡って src/Lhamiel/Resources/Locales/ を探す
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "src", "Lhamiel", "Resources", "Locales");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Locales ディレクトリが見つかりません");
    }

    /// <summary>
    /// axaml ファイルから x:Key 属性の値をすべて取得する。
    /// </summary>
    private static HashSet<string> ExtractKeys(string axamlPath)
    {
        var doc = XDocument.Load(axamlPath);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return doc.Descendants()
            .Select(e => e.Attribute(x + "Key")?.Value)
            .Where(k => k != null)
            .ToHashSet()!;
    }

    [Fact]
    public void AllLocales_HaveSameKeyCount()
    {
        var localesDir = GetLocalesDirectory();
        var files = Directory.GetFiles(localesDir, "*.axaml").OrderBy(f => f).ToArray();
        Assert.True(files.Length >= 2, "ロケールファイルが2件未満です");

        var keyCountsByFile = files
            .Select(f => (file: Path.GetFileName(f), count: ExtractKeys(f).Count))
            .ToList();

        var expectedCount = keyCountsByFile[0].count;
        var mismatches = keyCountsByFile.Where(x => x.count != expectedCount).ToList();

        Assert.True(mismatches.Count == 0,
            $"キー数がバラついています（基準: {keyCountsByFile[0].file} = {expectedCount}件）:\n" +
            string.Join("\n", keyCountsByFile.Select(x =>
                $"  {x.file}: {x.count}件{(x.count != expectedCount ? " ⚠️" : "")}")));
    }

    [Fact]
    public void AllLocales_HaveExactlySameKeys()
    {
        var localesDir = GetLocalesDirectory();
        var files = Directory.GetFiles(localesDir, "*.axaml").OrderBy(f => f).ToArray();
        Assert.True(files.Length >= 2, "ロケールファイルが2件未満です");

        // 基準: 全ファイルのキーの和集合
        var allKeys = new HashSet<string>();
        var keysByFile = new Dictionary<string, HashSet<string>>();
        foreach (var file in files)
        {
            var keys = ExtractKeys(file);
            keysByFile[Path.GetFileName(file)] = keys;
            allKeys.UnionWith(keys);
        }

        var errors = new List<string>();
        foreach (var (fileName, keys) in keysByFile)
        {
            var missing = allKeys.Except(keys).OrderBy(k => k).ToList();
            if (missing.Count > 0)
                errors.Add($"  {fileName}: 不足 {missing.Count}件 → {string.Join(", ", missing)}");
        }

        Assert.True(errors.Count == 0,
            $"ロケール間でキーが不一致です:\n{string.Join("\n", errors)}");
    }

    [Fact]
    public void AllLocales_HaveNoEmptyValues()
    {
        var localesDir = GetLocalesDirectory();
        var files = Directory.GetFiles(localesDir, "*.axaml").OrderBy(f => f).ToArray();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var errors = new List<string>();
        foreach (var file in files)
        {
            var doc = XDocument.Load(file);
            var emptyKeys = doc.Descendants()
                .Where(e =>
                {
                    var key = e.Attribute(x + "Key")?.Value;
                    if (key == null) return false;
                    var value = e.Value;
                    return string.IsNullOrWhiteSpace(value);
                })
                .Select(e => e.Attribute(x + "Key")!.Value)
                .ToList();

            if (emptyKeys.Count > 0)
                errors.Add($"  {Path.GetFileName(file)}: 空値 {emptyKeys.Count}件 → {string.Join(", ", emptyKeys)}");
        }

        Assert.True(errors.Count == 0,
            $"空のローカライズ値があります:\n{string.Join("\n", errors)}");
    }

    [Fact]
    public void AllLocales_HaveNoDuplicateKeys()
    {
        var localesDir = GetLocalesDirectory();
        var files = Directory.GetFiles(localesDir, "*.axaml").OrderBy(f => f).ToArray();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var errors = new List<string>();
        foreach (var file in files)
        {
            var doc = XDocument.Load(file);
            var keys = doc.Descendants()
                .Select(e => e.Attribute(x + "Key")?.Value)
                .Where(k => k != null)
                .ToList();

            var duplicates = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key!).ToList();
            if (duplicates.Count > 0)
                errors.Add($"  {Path.GetFileName(file)}: 重複 → {string.Join(", ", duplicates)}");
        }

        Assert.True(errors.Count == 0,
            $"重複キーがあります:\n{string.Join("\n", errors)}");
    }
}
