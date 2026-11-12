using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// DiagnosticsCollector の機微情報マスクのテスト。
/// 旧 VelopackIntegrationHappyTests から内容別に分離した。
/// </summary>
public class DiagnosticsCollectorMaskingTests
{
    /// <summary>
    /// MaskSensitiveValues が token/secret/password 系のキーを SensitivePatternRegex 経由でマスクすること。
    /// _sensitiveKeys 空配列でも regex フォールバックが効くことを検証。
    /// </summary>
    [Fact]
    public void DiagnosticsCollector_MaskSensitiveValues_MasksTokenKeysViaRegex()
    {
        // Arrange
        var input = """{"AccessToken":"abc123","ApiKey":"xyz789","Password":"p@ss","NormalField":"keep-me"}""";

        // Act
        var masked = DiagnosticsCollector.MaskSensitiveValues(input);

        // Assert
        Assert.DoesNotContain("abc123", masked);
        Assert.DoesNotContain("xyz789", masked);
        Assert.DoesNotContain("p@ss", masked);
        Assert.Contains("keep-me", masked);
    }
}
