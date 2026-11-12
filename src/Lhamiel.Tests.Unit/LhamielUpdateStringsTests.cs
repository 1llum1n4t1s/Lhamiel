using System.ComponentModel;
using Lhamiel.Models;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// LhamielUpdateStrings（更新ダイアログ用の文言提供）のテスト。
/// 旧 VelopackIntegrationHappyTests / VelopackIntegrationAdversarialTests から内容別に分離した。
/// </summary>
public class LhamielUpdateStringsTests
{
    /// <summary>
    /// NotifyLocaleChanged() を呼ぶと PropertyName=null (全プロパティ更新シグナル) で PropertyChanged が発火する。
    /// XAML バインディングの一括再評価を要求する INPC 契約。
    /// </summary>
    [Fact]
    public void LhamielUpdateStrings_NotifyLocaleChanged_RaisesPropertyChangedWithNullName()
    {
        // Arrange
        var strings = LhamielUpdateStrings.Instance;
        string? raisedName = "not-raised";
        var raised = false;
        PropertyChangedEventHandler handler = (s, e) =>
        {
            raised = true;
            raisedName = e.PropertyName;
        };
        strings.PropertyChanged += handler;

        try
        {
            // Act
            strings.NotifyLocaleChanged();

            // Assert
            Assert.True(raised);
            Assert.Null(raisedName); // null = 全プロパティ更新
        }
        finally
        {
            strings.PropertyChanged -= handler;
        }
    }

    /// <summary>
    /// LhamielUpdateStrings の全 getter が non-null string を返す型契約。
    /// Reflection でインターフェース実装プロパティを列挙し網羅検証。
    /// </summary>
    [Fact]
    public void LhamielUpdateStrings_AllGetters_ReturnNonNullStringsViaReflection()
    {
        // Arrange
        var instance = LhamielUpdateStrings.Instance;
        var interfaceType = typeof(LhamielUpdateStrings).GetInterface("IUpdateDialogStrings");
        Assert.NotNull(interfaceType);

        var stringProps = interfaceType!.GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .ToArray();
        Assert.NotEmpty(stringProps);

        // Act & Assert
        foreach (var prop in stringProps)
        {
            var value = prop.GetValue(instance);
            Assert.NotNull(value);
            Assert.IsType<string>(value);
            Assert.False(string.IsNullOrEmpty((string)value!),
                $"{prop.Name} が空/null を返した");
        }
    }

    /// <summary>
    /// LhamielUpdateStrings.NotifyLocaleChanged は PropertyChanged 未購読でも NRE しない。
    /// </summary>
    [Fact]
    public void LhamielUpdateStrings_NotifyLocaleChanged_WithoutSubscribers_DoesNotThrow()
    {
        // Arrange
        var instance = LhamielUpdateStrings.Instance;

        // Act
        var ex = Record.Exception(() => instance.NotifyLocaleChanged());

        // Assert
        Assert.Null(ex);
    }
}
