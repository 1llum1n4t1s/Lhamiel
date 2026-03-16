using Lhamiel.ViewModels;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// ViewModel で使用する record 型のテスト
/// </summary>
public class RecordTypeTests
{
    [Fact]
    public void ThemeItem_EqualityByValue()
    {
        var a = new ThemeItem("Dark", "Settings.Theme.Dark");
        var b = new ThemeItem("Dark", "Settings.Theme.Dark");
        Assert.Equal(a, b);
    }

    [Fact]
    public void ThemeItem_InequalityByKey()
    {
        var a = new ThemeItem("Dark", "Settings.Theme.Dark");
        var b = new ThemeItem("Light", "Settings.Theme.Light");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void CompressionLevelItem_EqualityByValue()
    {
        var a = new CompressionLevelItem(5, "CompressionLevel.Normal");
        var b = new CompressionLevelItem(5, "CompressionLevel.Normal");
        Assert.Equal(a, b);
    }

    [Fact]
    public void CompressionLevelItem_InequalityByLevel()
    {
        var a = new CompressionLevelItem(5, "CompressionLevel.Normal");
        var b = new CompressionLevelItem(9, "CompressionLevel.Ultra");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void CompressionLevelItem_LevelProperty()
    {
        var item = new CompressionLevelItem(7, "CompressionLevel.Maximum");
        Assert.Equal(7, item.Level);
        Assert.Equal("CompressionLevel.Maximum", item.ResourceKey);
    }

    [Fact]
    public void LocaleItem_EqualityByValue()
    {
        var a = new LocaleItem("ja_JP", "日本語");
        var b = new LocaleItem("ja_JP", "日本語");
        Assert.Equal(a, b);
    }

    [Fact]
    public void LocaleItem_InequalityByKey()
    {
        var a = new LocaleItem("ja_JP", "日本語");
        var b = new LocaleItem("en_US", "English");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void LocaleItem_PropertiesAccessible()
    {
        var item = new LocaleItem("ko_KR", "한국어");
        Assert.Equal("ko_KR", item.Key);
        Assert.Equal("한국어", item.DisplayName);
    }

    [Fact]
    public void ThemeItem_PropertiesAccessible()
    {
        var item = new ThemeItem("System", "Settings.Theme.System");
        Assert.Equal("System", item.Key);
        Assert.Equal("Settings.Theme.System", item.ResourceKey);
    }
}
