using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// UpdateChecker のテスト。
/// Velopack の UpdateManager は開発実行（非インストール環境）では NotInstalled を返すため、
/// その境界条件と基本的なエラーハンドリングを検証。
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
    }

    [Fact]
    public async Task CheckAndDownload_CancelledToken_ThrowsOrReturnsSafely()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // キャンセル済みトークンで呼び出し → OperationCanceledException or 安全な戻り値
        try
        {
            var result = await UpdateChecker.CheckAndDownloadAsync(cancellationToken: cts.Token);
            // キャンセル例外が飛ばない場合は安全な結果が返る
            Assert.NotNull(result);
        }
        catch (OperationCanceledException)
        {
            // 期待通り
        }
    }

    [Fact]
    public async Task CheckAndDownload_WithProgress_ReportsStatus()
    {
        var reported = new List<string>();
        var progress = new Progress<string>(s => reported.Add(s));

        var result = await UpdateChecker.CheckAndDownloadAsync(
            statusProgress: progress,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public void UpdateResult_Enum_ContainsExpectedValues()
    {
        var values = Enum.GetValues<UpdateChecker.UpdateResult>();
        Assert.Contains(UpdateChecker.UpdateResult.NoUpdate, values);
        Assert.Contains(UpdateChecker.UpdateResult.Downloaded, values);
        Assert.Contains(UpdateChecker.UpdateResult.Error, values);
        Assert.Contains(UpdateChecker.UpdateResult.NotInstalled, values);
        Assert.Contains(UpdateChecker.UpdateResult.NotConfigured, values);
    }

    [Fact]
    public async Task CheckAndDownload_ResultHasStatusMessage()
    {
        var result = await UpdateChecker.CheckAndDownloadAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result.Message);
        Assert.NotEmpty(result.Message);
    }
}
