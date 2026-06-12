using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// AcrylicFallbackHelper の不透明背景フォールバック判定ロジックのテスト。
/// Windows は透過効果 OFF / リモートセッションでも AcrylicBlur を「適用済み」と
/// 報告するため、OS 状態による補正が正しく効くことを確認する。
/// </summary>
public class AcrylicFallbackHelperTests
{
    [Theory]
    // アクリル不許可ならプラットフォーム問わず常にフォールバック
    [InlineData(false, true, false, true, true)]
    [InlineData(false, false, false, true, true)]
    // Windows + アクリル許可 + 透過 ON + ローカル → アクリルのまま
    [InlineData(true, true, false, true, false)]
    // Windows + アクリル許可でも透過 OFF ならフォールバック（ライトテーマ灰色化対策の本丸）
    [InlineData(true, true, false, false, true)]
    // Windows + アクリル許可でもリモートセッションならフォールバック
    [InlineData(true, true, true, true, true)]
    [InlineData(true, true, true, false, true)]
    // 非 Windows はアクリル許可ならそのまま（OS 補正は Windows 限定）
    [InlineData(true, false, false, true, false)]
    public void ShouldUseOpaqueBackgroundCore_ReturnsExpected(
        bool acrylicGranted, bool isWindows, bool isRemoteSession, bool transparencyEnabled, bool expected)
    {
        var actual = AcrylicFallbackHelper.ShouldUseOpaqueBackgroundCore(
            acrylicGranted, isWindows, isRemoteSession, transparencyEnabled);

        Assert.Equal(expected, actual);
    }
}
