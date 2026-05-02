using Avalonia.Controls;
using Lhamiel.Models;
using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// ArchiveProcessor のテスト可能化検証（Phase 1: ハッピーパスのみ）。
/// internal static プロパティでスタブに差し替え、ArchiveProcessor が
/// 正しいタイミングでインターフェース経由で呼び出すことを確認する。
/// </summary>
[Collection("ArchiveProcessor")]
public class ArchiveProcessorTests : IDisposable
{
    private readonly IMessageService _originalMessage;
    private readonly IUiDispatcher _originalDispatcher;
    private readonly IConflictDialogService _originalConflict;

    public ArchiveProcessorTests()
    {
        _originalMessage = ArchiveProcessor.MessageServiceImpl;
        _originalDispatcher = ArchiveProcessor.UiDispatcherImpl;
        _originalConflict = ArchiveProcessor.ConflictDialogImpl;
    }

    public void Dispose()
    {
        ArchiveProcessor.MessageServiceImpl = _originalMessage;
        ArchiveProcessor.UiDispatcherImpl = _originalDispatcher;
        ArchiveProcessor.ConflictDialogImpl = _originalConflict;
    }

    // --- スタブ実装 ---

    private sealed class StubMessageService : IMessageService
    {
        public List<string> Errors { get; } = [];
        public Task ShowError(string message, string? title = null)
        {
            Errors.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class StubUiDispatcher : IUiDispatcher
    {
        public int PostCallCount { get; private set; }
        public void Post(Action action)
        {
            PostCallCount++;
            action();
        }
        public Task<T> InvokeAsync<T>(Func<Task<T>> callback) => callback();
    }

    private sealed class StubConflictDialogService : IConflictDialogService
    {
        public bool CanOverwriteResult { get; set; } = true;
        public FileConflictResult ShowResult { get; set; } = FileConflictResult.Continue;
        public List<(string fullPath, string relativePath)> SelectedFiles { get; set; } = [];

        public Task<bool> CanOverwriteFromBackgroundAsync(string sourcePath, string destinationPath, Window? parentWindow)
            => Task.FromResult(CanOverwriteResult);

        public Task<(FileConflictResult result, List<(string fullPath, string relativePath)> selectedFiles)>
            ShowFromBackgroundAsync(List<FileConflictGroup> groups, Window? parentWindow, bool isTwoPane = true)
            => Task.FromResult((ShowResult, SelectedFiles));
    }

    // --- テスト ---

    [Fact]
    public async Task ExtractArchiveAsync_NonExistentFile_ShowsError()
    {
        var stub = new StubMessageService();
        ArchiveProcessor.MessageServiceImpl = stub;

        var (path, info) = await ArchiveProcessor.ExtractArchiveAsync(
            @"C:\non_existent_file_12345.zip", @"C:\temp", false, null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(path);
        Assert.Null(info);
        Assert.Single(stub.Errors);
    }

    [Fact]
    public async Task CompressItemAsync_NonExistentSource_ShowsError()
    {
        var stub = new StubMessageService();
        ArchiveProcessor.MessageServiceImpl = stub;

        var result = await ArchiveProcessor.CompressItemAsync(
            @"C:\non_existent_folder_12345", @"C:\temp", false, "zip", null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.Single(stub.Errors);
    }

    [Fact]
    public async Task CompressItemAsync_UnsupportedFormat_ShowsError()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"lhamiel_proc_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var stub = new StubMessageService();
            ArchiveProcessor.MessageServiceImpl = stub;

            var result = await ArchiveProcessor.CompressItemAsync(
                testDir, @"C:\temp", false, "rar", null,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Single(stub.Errors);
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public async Task CompressMergedAsync_UnsupportedFormat_ShowsError()
    {
        var stub = new StubMessageService();
        ArchiveProcessor.MessageServiceImpl = stub;

        var testFile = Path.GetTempFileName();
        try
        {
            var result = await ArchiveProcessor.CompressMergedAsync(
                [testFile], @"C:\temp", false, "rar", null,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Single(stub.Errors);
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public void ServiceContracts_DefaultImplementations_AreNotNull()
    {
        Assert.NotNull(ArchiveProcessor.MessageServiceImpl);
        Assert.NotNull(ArchiveProcessor.UiDispatcherImpl);
        Assert.NotNull(ArchiveProcessor.ConflictDialogImpl);
    }

    [Fact]
    public void StubDispatcher_Post_ExecutesAction()
    {
        var stub = new StubUiDispatcher();
        var executed = false;
        stub.Post(() => executed = true);
        Assert.True(executed);
        Assert.Equal(1, stub.PostCallCount);
    }

    [Fact]
    public async Task StubDispatcher_InvokeAsync_ReturnsResult()
    {
        var stub = new StubUiDispatcher();
        var result = await stub.InvokeAsync(async () =>
        {
            await Task.Yield();
            return 42;
        });
        Assert.Equal(42, result);
    }
}
