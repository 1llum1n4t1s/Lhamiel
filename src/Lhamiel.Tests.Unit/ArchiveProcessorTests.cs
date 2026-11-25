using Avalonia.Controls;
using Lhamiel.Util;
using System.IO.Compression;
using System.Text;
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
    private readonly Func<string, long, Window?, CancellationToken, Task<bool>> _originalEnsureDiskSpaceAsync;

    public ArchiveProcessorTests()
    {
        _originalMessage = ArchiveProcessor.MessageServiceImpl;
        _originalDispatcher = ArchiveProcessor.UiDispatcherImpl;
        _originalEnsureDiskSpaceAsync = ArchiveProcessor.EnsureDiskSpaceAsyncImpl;
    }

    public void Dispose()
    {
        ArchiveProcessor.MessageServiceImpl = _originalMessage;
        ArchiveProcessor.UiDispatcherImpl = _originalDispatcher;
        ArchiveProcessor.EnsureDiskSpaceAsyncImpl = _originalEnsureDiskSpaceAsync;
    }

    // メッセージサービス・UI ディスパッチャのスタブは TestDoubles.cs の共有実装を使う。

    // --- テスト ---

    // 存在しない入力は項目ごとにエラー通知される（ArchiveProcessorAdversarialTests から移設）。

    [Fact]
    public async Task ExtractArchivesAsync_AllNonExistent_ShowsErrorForEachFile()
    {
        var stub = new RecordingMessageService();
        ArchiveProcessor.MessageServiceImpl = stub;
        ArchiveProcessor.UiDispatcherImpl = new SyncUiDispatcher();
        var files = new[] { @"C:\fake1.zip", @"C:\fake2.7z", @"C:\fake3.tar" };

        await ArchiveProcessor.ExtractArchivesAsync(
            files, @"C:\temp", false, null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(files.Length, stub.Errors.Count);
    }

    [Fact]
    public async Task CompressMergedAsync_EmptyPaths_ReturnsFalse()
    {
        ArchiveProcessor.MessageServiceImpl = new RecordingMessageService();
        ArchiveProcessor.UiDispatcherImpl = new SyncUiDispatcher();

        var result = await ArchiveProcessor.CompressMergedAsync(
            [], @"C:\temp", false, "zip", null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task CompressItemsAsync_AllNonExistent_ShowsErrorForEachPath()
    {
        var stub = new RecordingMessageService();
        ArchiveProcessor.MessageServiceImpl = stub;
        ArchiveProcessor.UiDispatcherImpl = new SyncUiDispatcher();
        var paths = new[] { @"C:\nonexist_a", @"C:\nonexist_b" };

        await ArchiveProcessor.CompressItemsAsync(
            paths, @"C:\temp", false, "zip", null,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(paths.Length, stub.Errors.Count);
    }

    [Fact]
    public async Task ExtractArchiveAsync_NfdRootName_PropagatesMarkOfTheWebToRawExtractedPath()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "ArchiveProcessorMotwTests_" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);
        try
        {
            ArchiveProcessor.UiDispatcherImpl = new SyncUiDispatcher();
            ArchiveProcessor.MessageServiceImpl = new RecordingMessageService();

            var archivePath = Path.Combine(testDir, "nfd.zip");
            var nfdRootName = "é".Normalize(NormalizationForm.FormD);
            using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry($"{nfdRootName}/app.exe");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("test executable");
            }

            const string zoneContent = "[ZoneTransfer]\r\nZoneId=3\r\n";
            File.WriteAllText(archivePath + ":Zone.Identifier", zoneContent);

            var outputDirectory = Path.Combine(testDir, "output");
            var settings = new Settings
            {
                CreateArchiveNameFolder = false,
                NormalizeUnicodeFileNames = true,
                PropagateMarkOfTheWeb = true,
            };

            var (outputPath, _) = await ArchiveProcessor.ExtractArchiveAsync(
                archivePath,
                outputDirectory,
                outputToSameDirectory: false,
                progressWindow: null,
                cancellationToken: TestContext.Current.CancellationToken,
                settingsSnapshot: settings);

            Assert.Equal(outputDirectory, outputPath);
            var extractedFile = Path.Combine(outputDirectory, nfdRootName, "app.exe");
            Assert.True(File.Exists(extractedFile));
            Assert.Equal(zoneContent, File.ReadAllText(extractedFile + ":Zone.Identifier"));
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public async Task ExtractArchiveAsync_StructureOpenStillFailsAfterRetries_DoesNotExtractWithEmptyStructure()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "ArchiveProcessorStructureTests_" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);
        try
        {
            var stub = new RecordingMessageService();
            ArchiveProcessor.MessageServiceImpl = stub;
            ArchiveProcessor.UiDispatcherImpl = new SyncUiDispatcher();

            var archivePath = Path.Combine(testDir, "locked.zip");
            using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("locked/hello.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("hello");
            }

            var holder = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.None);
            var unlocker = Task.Run(async () =>
            {
                try
                {
                    // 構造解析の 3 試行は ±25% ジッタ込みでも最大 750ms
                    //（初回直後 + 150～250ms + 300～500ms）で開始されるため、全試行が
                    // 確実に失敗した後で解除する。従来実装なら、その直後の展開側リトライ中に
                    // ロックが解除され、空の構造情報のまま展開だけ成功する回帰を検出できる。
                    await Task.Delay(1000, CancellationToken.None);
                }
                finally
                {
                    holder.Dispose();
                }
            });

            try
            {
                var outputDirectory = Path.Combine(testDir, "output");
                var (outputPath, structureInfo) = await ArchiveProcessor.ExtractArchiveAsync(
                    archivePath,
                    outputDirectory,
                    outputToSameDirectory: false,
                    progressWindow: null,
                    cancellationToken: TestContext.Current.CancellationToken,
                    settingsSnapshot: new Settings { CreateArchiveNameFolder = false });

                Assert.Null(outputPath);
                Assert.Null(structureInfo);
                Assert.Single(stub.Errors);
                Assert.False(Directory.Exists(outputDirectory));
            }
            finally
            {
                holder.Dispose();
                await unlocker;
            }
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public async Task CompressItemAsync_NonExistentSource_ShowsError()
    {
        var stub = new RecordingMessageService();
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
            var stub = new RecordingMessageService();
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
        var stub = new RecordingMessageService();
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
    public async Task CompressItemAsync_DiskEstimateUsesOnlyFilesSelectedByIgnoreRules()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"lhamiel_proc_capacity_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testDir, "source");
        var outputDir = Path.Combine(testDir, "output");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(outputDir);

        try
        {
            var includedPath = Path.Combine(sourceDir, "included.bin");
            await File.WriteAllBytesAsync(
                includedPath,
                new byte[37],
                TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(
                Path.Combine(sourceDir, "excluded.bin"),
                new byte[4096],
                TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(sourceDir, ".gitignore"),
                "*\n!included.bin\n",
                TestContext.Current.CancellationToken);

            long? requiredBytes = null;
            ArchiveProcessor.EnsureDiskSpaceAsyncImpl = (_, bytes, _, _) =>
            {
                requiredBytes = bytes;
                // 容量確認の直後に中止し、ネイティブ圧縮はこのテストでは実行しない。
                return Task.FromResult(false);
            };

            var settings = new Settings
            {
                DirectoryStructureMode = DirectoryStructureMode.IncludeRoot,
                RespectNestedGitignore = true,
                SourceIgnoreFileNames = [".gitignore"],
                IncludeHiddenAndSystemEntries = true,
                IsPasswordProtectionEnabled = false,
            };

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                ArchiveProcessor.CompressItemAsync(
                    sourceDir,
                    outputDir,
                    outputToSameDirectory: false,
                    format: "zip",
                    progressWindow: null,
                    cancellationToken: TestContext.Current.CancellationToken,
                    settingsSnapshot: settings));

            Assert.Equal(new FileInfo(includedPath).Length, requiredBytes);
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    // 競合解決後に圧縮対象が 0 件になったときは、黙って終わらず通知する（キャンセルは通知しない）。

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompressItemAsync_NoFilesAfterConflictResolution_NotifiesOrStaysSilentOnCancel(bool cancel)
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"lhamiel_proc_nofiles_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testDir, "source");
        var outputDir = Path.Combine(testDir, "output");
        Directory.CreateDirectory(Path.Combine(sourceDir, "a"));
        Directory.CreateDirectory(Path.Combine(sourceDir, "b"));
        Directory.CreateDirectory(outputDir);
        var originalConflict = ArchiveProcessor.ConflictDialogImpl;

        try
        {
            await File.WriteAllTextAsync(Path.Combine(sourceDir, "a", "same.txt"), "a", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(sourceDir, "b", "same.txt"), "b", TestContext.Current.CancellationToken);
            var stub = new RecordingMessageService();
            ArchiveProcessor.MessageServiceImpl = stub;
            ArchiveProcessor.UiDispatcherImpl = new SyncUiDispatcher();
            ArchiveProcessor.ConflictDialogImpl = new NoSelectionConflictDialog(cancel);

            var result = await ArchiveProcessor.CompressItemAsync(
                sourceDir, outputDir, outputToSameDirectory: false, format: "zip", progressWindow: null,
                cancellationToken: TestContext.Current.CancellationToken,
                settingsSnapshot: new Settings
                {
                    DirectoryStructureMode = DirectoryStructureMode.Flat,
                    IsPasswordProtectionEnabled = false,
                });

            Assert.False(result);
            Assert.Equal(cancel ? 0 : 1, stub.Infos.Count);
            Assert.Empty(stub.Errors);
            Assert.Empty(Directory.GetFiles(outputDir));
        }
        finally
        {
            ArchiveProcessor.ConflictDialogImpl = originalConflict;
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompressMergedAsync_NoFilesAfterConflictResolution_NotifiesOrStaysSilentOnCancel(bool cancel)
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"lhamiel_proc_nofiles_m_{Guid.NewGuid():N}");
        var outputDir = Path.Combine(testDir, "output");
        Directory.CreateDirectory(Path.Combine(testDir, "a"));
        Directory.CreateDirectory(Path.Combine(testDir, "b"));
        Directory.CreateDirectory(outputDir);
        var originalConflict = ArchiveProcessor.ConflictDialogImpl;

        try
        {
            var first = Path.Combine(testDir, "a", "same.txt");
            var second = Path.Combine(testDir, "b", "same.txt");
            await File.WriteAllTextAsync(first, "a", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(second, "b", TestContext.Current.CancellationToken);
            var stub = new RecordingMessageService();
            ArchiveProcessor.MessageServiceImpl = stub;
            ArchiveProcessor.UiDispatcherImpl = new SyncUiDispatcher();
            ArchiveProcessor.ConflictDialogImpl = new NoSelectionConflictDialog(cancel);

            var result = await ArchiveProcessor.CompressMergedAsync(
                [first, second], outputDir, outputToSameDirectory: false, format: "zip", progressWindow: null,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result);
            Assert.Equal(cancel ? 0 : 1, stub.Infos.Count);
            Assert.Empty(stub.Errors);
            Assert.Empty(Directory.GetFiles(outputDir));
        }
        finally
        {
            ArchiveProcessor.ConflictDialogImpl = originalConflict;
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    /// <summary>競合ダイアログで 1 件も選ばない（または Cancel を選ぶ）スタブ。</summary>
    private sealed class NoSelectionConflictDialog(bool cancel) : IConflictDialogService
    {
        public Task<bool> CanOverwriteFromBackgroundAsync(
            IReadOnlyList<string> sourcePaths, string destinationPath, Window? parentWindow)
            => Task.FromResult(true);

        public Task<(Models.FileConflictResult result, List<(string fullPath, string relativePath)> selectedFiles)>
            ShowFromBackgroundAsync(List<Models.FileConflictGroup> groups, Window? parentWindow, bool isTwoPane = true)
            => Task.FromResult((
                cancel ? Models.FileConflictResult.Cancel : Models.FileConflictResult.Continue,
                new List<(string fullPath, string relativePath)>()));
    }
}
