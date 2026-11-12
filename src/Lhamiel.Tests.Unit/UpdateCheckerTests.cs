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
    public async Task CheckAndDownload_ResultHasStatusMessage()
    {
        var result = await UpdateChecker.CheckAndDownloadAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result.Message);
        Assert.NotEmpty(result.Message);
    }

    /// <summary>
    /// TryBuildUpdateManager は UpdateManager コンストラクタが内部で VelopackLocator.Current を要求するため、
    /// VelopackApp.Build().Run() を呼んでいない単体テスト環境では InvalidOperationException がスローされる。
    /// この挙動は production では Program.cs の VelopackApp.Build().Run() で初期化済みなので問題ない。
    /// テスト環境では「Velopack 未初期化時に投げる」契約を固定する。
    /// </summary>
    [Fact]
    public void UpdateChecker_TryBuildUpdateManager_VelopackNotInitialized_ThrowsInvalidOperationException()
    {
        // Arrange
        var settings = new Settings();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => UpdateChecker.TryBuildUpdateManager(settings));
        Assert.Contains("VelopackLocator", ex.Message);
    }

    /// <summary>
    /// UpdateChecker.TryBuildUpdateManager に null を渡すと ArgumentNullException がスローされる。
    /// </summary>
    [Fact]
    public void UpdateChecker_TryBuildUpdateManager_NullSettings_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => UpdateChecker.TryBuildUpdateManager(null!));
        Assert.Equal("settings", ex.ParamName);
    }
}
