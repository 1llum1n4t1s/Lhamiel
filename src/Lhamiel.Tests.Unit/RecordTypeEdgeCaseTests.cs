using Lhamiel.ViewModels;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// Record型の追加エッジケーステスト
/// </summary>
public class RecordTypeEdgeCaseTests
{
    // === ThemeItem ===

    [Fact]
    public void ThemeItem_HashCodeConsistency()
    {
        var a = new ThemeItem("Dark", "Settings.Theme.Dark");
        var b = new ThemeItem("Dark", "Settings.Theme.Dark");
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ThemeItem_DifferentValues_DifferentHashCode()
    {
        var a = new ThemeItem("Dark", "Settings.Theme.Dark");
        var b = new ThemeItem("Light", "Settings.Theme.Light");
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ThemeItem_ToString_ContainsKey()
    {
        var item = new ThemeItem("Dark", "Settings.Theme.Dark");
        var str = item.ToString();
        Assert.Contains("Dark", str);
    }

    [Fact]
    public void ThemeItem_NotEqualToNull()
    {
        var item = new ThemeItem("Dark", "Settings.Theme.Dark");
        Assert.False(item.Equals(null));
    }

    [Fact]
    public void ThemeItem_SameKeyDifferentResourceKey_NotEqual()
    {
        var a = new ThemeItem("Dark", "Theme.A");
        var b = new ThemeItem("Dark", "Theme.B");
        Assert.NotEqual(a, b);
    }

    // === CompressionLevelItem ===

    [Fact]
    public void CompressionLevelItem_HashCodeConsistency()
    {
        var a = new CompressionLevelItem(5, "Normal");
        var b = new CompressionLevelItem(5, "Normal");
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void CompressionLevelItem_ZeroLevel_IsValid()
    {
        var item = new CompressionLevelItem(0, "Store");
        Assert.Equal(0, item.Level);
    }

    [Fact]
    public void CompressionLevelItem_NineLevel_IsValid()
    {
        var item = new CompressionLevelItem(9, "Ultra");
        Assert.Equal(9, item.Level);
    }

    [Fact]
    public void CompressionLevelItem_SameLevelDifferentKey_NotEqual()
    {
        var a = new CompressionLevelItem(5, "Normal");
        var b = new CompressionLevelItem(5, "Default");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void CompressionLevelItem_NotEqualToNull()
    {
        var item = new CompressionLevelItem(5, "Normal");
        Assert.False(item.Equals(null));
    }

    // === LocaleItem ===

    [Fact]
    public void LocaleItem_HashCodeConsistency()
    {
        var a = new LocaleItem("ja_JP", "日本語");
        var b = new LocaleItem("ja_JP", "日本語");
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void LocaleItem_SameKeyDifferentDisplayName_NotEqual()
    {
        var a = new LocaleItem("ja_JP", "日本語");
        var b = new LocaleItem("ja_JP", "Japanese");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void LocaleItem_EmptyKey_IsValid()
    {
        var item = new LocaleItem("", "Auto");
        Assert.Equal("", item.Key);
    }

    [Fact]
    public void LocaleItem_ToString_ContainsDisplayName()
    {
        var item = new LocaleItem("ja_JP", "日本語");
        var str = item.ToString();
        Assert.Contains("日本語", str);
    }

    [Fact]
    public void LocaleItem_NotEqualToNull()
    {
        var item = new LocaleItem("ja_JP", "日本語");
        Assert.False(item.Equals(null));
    }

    // === record型の構造的等値性テスト ===

    [Fact]
    public void ThemeItem_CanBeUsedAsDictionaryKey()
    {
        var dict = new Dictionary<ThemeItem, string>
        {
            [new ThemeItem("Dark", "Theme.Dark")] = "dark_value"
        };
        Assert.True(dict.ContainsKey(new ThemeItem("Dark", "Theme.Dark")));
    }

    [Fact]
    public void CompressionLevelItem_CanBeUsedInHashSet()
    {
        var set = new HashSet<CompressionLevelItem>
        {
            new(5, "Normal"),
            new(5, "Normal"), // 重複
            new(9, "Ultra")
        };
        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void LocaleItem_CanBeUsedInHashSet()
    {
        var set = new HashSet<LocaleItem>
        {
            new("ja_JP", "日本語"),
            new("ja_JP", "日本語"), // 重複
            new("en_US", "English")
        };
        Assert.Equal(2, set.Count);
    }
}
