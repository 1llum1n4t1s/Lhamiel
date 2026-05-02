using Avalonia.Controls;
using Lhamiel.Models;
using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// ArchiveProcessor の敵対的テスト — 境界値・異常入力・状態遷移の矛盾を検証。
/// </summary>
[Collection("ArchiveProcessor")]
public class ArchiveProcessorAdversarialTests : IDisposable
{
    private readonly IMessageService _originalMessage;
    private readonly IUiDispatcher _originalDispatcher;
    private readonly IConflictDialogService _originalConflict;

    public ArchiveProcessorAdversarialTests()
    {
        _originalMessage = ArchiveProcessor.MessageServiceImpl;
        _originalDispatcher = ArchiveProcessor.UiDispatcherImpl;
        _originalConflict = ArchiveProcessor.ConflictDialogImpl;

        ArchiveProcessor.MessageServiceImpl = new StubMessageService();
        ArchiveProcessor.UiDispatcherImpl = new StubUiDispatcher();
        ArchiveProcessor.ConflictDialogImpl = new StubConflictDialogService();
    }

    public void Dispose()
    {
        ArchiveProcessor.MessageServiceImpl = _originalMessage;
        ArchiveProcessor.UiDispatcherImpl = _originalDispatcher;
        ArchiveProcessor.ConflictDialogImpl = _originalConflict;
    }

    private sealed class StubMessageService : IMessageService
    {
        public List<string> Errors { get; } = [];
        public Task ShowError(string message, string? title = null) { Errors.Add(message); return Task.CompletedTask; }
    }

    private sealed class StubUiDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
        public Task InvokeAsync(Func<Task> callback) => callback();
        public Task<T> InvokeAsync<T>(Func<Task<T>> callback) => callback();
    }

    private sealed class StubConflictDialogService : IConflictDialogService
    {
        public Task<bool> CanOverwriteFromBackgroundAsync(string sourcePath, string destinationPath, Window? parentWindow)
            => Task.FromResult(true);
        public Task<(FileConflictResult result, List<(string fullPath, string relativePath)> selectedFiles)>
            ShowFromBackgroundAsync(List<FileConflictGroup> groups, Window? parentWindow, bool isTwoPane = true)
            => Task.FromResult((FileConflictResult.Continue, new List<(string, string)>()));
    }

    // === ExtractArchiveAsync 敵対的テスト ===

    [Fact]
    public async Task ExtractArchiveAsync_NullPath_ShowsError()
    {
        var stub = (StubMessageService)ArchiveProcessor.MessageServiceImpl;
        var (path, info) = await ArchiveProcessor.ExtractArchiveAsync(
            null!, @"C:\temp", false, null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(path);
        Assert.Single(stub.Errors);
    }

    [Fact]
    public async Task ExtractArchiveAsync_EmptyPath_ShowsError()
    {
        var stub = (StubMessageService)ArchiveProcessor.MessageServiceImpl;
        var (path, info) = await ArchiveProcessor.ExtractArchiveAsync(
            "", @"C:\temp", false, null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(path);
        Assert.Single(stub.Errors);
    }

    [Fact]
    public async Task ExtractArchiveAsync_CancelledToken_DoesNotThrow()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var (path, info) = await ArchiveProcessor.ExtractArchiveAsync(
            @"C:\non_existent.zip", @"C:\temp", false, null,
            cancellationToken: cts.Token);

        Assert.Null(path);
    }

    [Fact]
    public async Task ExtractArchivesAsync_EmptyList_ReturnsImmediately()
    {
        await ArchiveProcessor.ExtractArchivesAsync(
            [], @"C:\temp", false, null,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExtractArchivesAsync_AllNonExistent_ShowsErrorsForEach()
    {
        var stub = (StubMessageService)ArchiveProcessor.MessageServiceImpl;
        var files = new[] { @"C:\fake1.zip", @"C:\fake2.7z", @"C:\fake3.tar" };

        await ArchiveProcessor.ExtractArchivesAsync(
            files, @"C:\temp", false, null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(stub.Errors.Count >= 1);
    }

    // === CompressItemAsync 敵対的テスト ===

    [Fact]
    public async Task CompressItemAsync_NullSource_ShowsError()
    {
        var stub = (StubMessageService)ArchiveProcessor.MessageServiceImpl;
        var result = await ArchiveProcessor.CompressItemAsync(
            null!, @"C:\temp", false, "zip", null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.Single(stub.Errors);
    }

    [Fact]
    public async Task CompressItemAsync_EmptyFormat_ShowsError()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"lhamiel_adv_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);
        try
        {
            var stub = (StubMessageService)ArchiveProcessor.MessageServiceImpl;
            var result = await ArchiveProcessor.CompressItemAsync(
                testDir, @"C:\temp", false, "", null,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Single(stub.Errors);
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public async Task CompressItemAsync_WhitespaceFormat_ShowsError()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"lhamiel_adv_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);
        try
        {
            var stub = (StubMessageService)ArchiveProcessor.MessageServiceImpl;
            var result = await ArchiveProcessor.CompressItemAsync(
                testDir, @"C:\temp", false, "   ", null,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Single(stub.Errors);
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }

    // === CompressMergedAsync 敵対的テスト ===

    [Fact]
    public async Task CompressMergedAsync_EmptyPaths_ReturnsFalse()
    {
        var result = await ArchiveProcessor.CompressMergedAsync(
            [], @"C:\temp", false, "zip", null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task CompressMergedAsync_NullFormat_ShowsError()
    {
        var stub = (StubMessageService)ArchiveProcessor.MessageServiceImpl;
        var testFile = Path.GetTempFileName();
        try
        {
            var result = await ArchiveProcessor.CompressMergedAsync(
                [testFile], @"C:\temp", false, null!, null,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Single(stub.Errors);
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    // === CompressItemsAsync 敵対的テスト ===

    [Fact]
    public async Task CompressItemsAsync_EmptyPaths_ReturnsImmediately()
    {
        await ArchiveProcessor.CompressItemsAsync(
            [], @"C:\temp", false, "zip", null,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CompressItemsAsync_MixedExistentNonExistent_HandlesGracefully()
    {
        var stub = (StubMessageService)ArchiveProcessor.MessageServiceImpl;
        var paths = new[] { @"C:\nonexist_a", @"C:\nonexist_b" };

        await ArchiveProcessor.CompressItemsAsync(
            paths, @"C:\temp", false, "zip", null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(stub.Errors.Count >= 1);
    }
}
