using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// UpdateChecker のテスト。
/// Velopack の UpdateManager は開発実行（非インストール環境）では NotInstalled を返すため、
/// 例外を出さずに結果を返す境界条件を検証。
/// </summary>
public class UpdateCheckerTests
{
    [Fact]
    public async Task CheckAndDownload_DevEnvironment_ReturnsNotInstalled()
    {
        var result = await UpdateChecker.CheckAndDownloadAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // 開発環境ではインストールされていないため NotInstalled, NotConfigured, または Error
        Assert.True(
            result.Result is UpdateChecker.UpdateResult.NotInstalled
                or UpdateChecker.UpdateResult.NotConfigured
                or UpdateChecker.UpdateResult.Error,
            $"Expected NotInstalled/NotConfigured/Error, got {result.Result}");

        // 利用者へ見せる状態メッセージが空でないこと
        Assert.NotNull(result.Message);
        Assert.NotEmpty(result.Message);
    }
}
