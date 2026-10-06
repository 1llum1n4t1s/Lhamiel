using Cube.FileSystem.SevenZip;
using Lhamiel.Util;
using Xunit;
using LzhArchiveEntry = Kagayoi.UnLhaRe.ArchiveEntry;
using LzhArchiveCreateEntryResult = Kagayoi.UnLhaRe.ArchiveCreateEntryResult;
using LzhArchiveCreateEntryStatus = Kagayoi.UnLhaRe.ArchiveCreateEntryStatus;
using LzhArchiveCreateReport = Kagayoi.UnLhaRe.ArchiveCreateReport;
using LzhArchiveProgress = Kagayoi.UnLhaRe.ArchiveProgress;
using LzhArchiveProgressPhase = Kagayoi.UnLhaRe.ArchiveProgressPhase;
using LzhArchiveSourceEntry = Kagayoi.UnLhaRe.ArchiveSourceEntry;
using LzhCompressionMethod = Kagayoi.UnLhaRe.CompressionMethod;
using LzhErrorKind = Kagayoi.UnLhaRe.ArchiveErrorKind;
using LzhNativeException = Kagayoi.UnLhaRe.ArchiveNativeException;

namespace Lhamiel.Tests.Unit;

/// <summary>Lhamiel の LZH 分岐と Kagayoi.UnLhaRe の結合契約を確認する。</summary>
[Collection("Sequential")]
public sealed class LzhArchiveIntegrationTests
{
    [Theory]
    [InlineData("archive.lzh", true)]
    [InlineData("archive.LHA", true)]
    [InlineData("archive.zip", false)]
    [InlineData("archive.lzh.txt", false)]
    public void IsLzhArchivePath_RecognizesOnlyLzhAndLhaExtensions(string path, bool expected)
    {
        Assert.Equal(expected, ArchiveExtractor.IsLzhArchivePath(path));
    }

    [Fact]
    public void LzhProgressThrottler_RapidExtractFinalizeAlternation_ReportsOnlyFirstOfEachPhase()
    {
        var throttler = new LzhProgressThrottler(reportIntervalMs: 60_000);
        var reported = 0;

        for (var index = 0; index < 10_000; index++)
        {
            var phase = index % 2 == 0
                ? LzhArchiveProgressPhase.ExtractOrVerify
                : LzhArchiveProgressPhase.Finalize;
            if (throttler.ShouldReport(new LzhArchiveProgress(phase, (ulong)index, 10_000)))
                reported++;
        }

        Assert.Equal(2, reported);
    }

    [Fact]
    public async Task CompressThenExtractLzh_RoundTripsUnicodeEmptyDirectorySelectionAndTimestamp()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Kagayoi.UnLhaRe の native asset は Windows 限定");
        using var temp = TestDirectory.Create(nameof(CompressThenExtractLzh_RoundTripsUnicodeEmptyDirectorySelectionAndTimestamp));
        var source = Path.Combine(temp.Path, "入力");
        var emptyDirectory = Path.Combine(source, "空ディレクトリ");
        Directory.CreateDirectory(emptyDirectory);

        var unicodeSource = Path.Combine(source, "こんにちは.txt");
        var skippedSource = Path.Combine(source, "選択除外.txt");
        var ignoredSource = Path.Combine(source, ".DS_Store");
        const string unicodeContents = "日本語の本文と emoji 🌙";
        File.WriteAllText(unicodeSource, unicodeContents);
        File.WriteAllText(skippedSource, "must not be extracted");
        File.WriteAllText(ignoredSource, "system metadata");
        var expectedTimestamp = new DateTime(2024, 6, 7, 8, 9, 10, DateTimeKind.Local);
        var expectedDirectoryTimestamp = new DateTime(2024, 6, 7, 8, 9, 12, DateTimeKind.Local);
        File.SetLastWriteTime(unicodeSource, expectedTimestamp);
        Directory.SetLastWriteTime(emptyDirectory, expectedDirectoryTimestamp);

        var resolvedFiles = new List<(string fullPath, string relativePath)>
        {
            (unicodeSource, "資料/こんにちは.txt"),
            (skippedSource, "除外フォルダ/子.txt"),
            (ignoredSource, ".DS_Store"),
            (emptyDirectory, "資料/空/"),
        };
        var archive = Path.Combine(temp.Path, "日本語書庫.lzh");
        var compressionProgress = new List<ProgressInfo>();

        var skippedCount = await ArchiveCompressor.CompressFilesAsync(
            [source],
            archive,
            Format.Lzh,
            new InlineTestProgress(value => compressionProgress.Add(value)),
            TestContext.Current.CancellationToken,
            resolvedFiles);

        Assert.Equal(0, skippedCount);
        Assert.True(File.Exists(archive));
        Assert.Contains(compressionProgress, value =>
            value.IsIndeterminate && value.Status == App.Text("Progress.Finalizing"));
        Assert.Equal(100, compressionProgress[^1].Percentage);

        var archivedEntries = LzhArchiveBackendProvider.Current.List(archive);
        Assert.Contains(archivedEntries, entry => entry.Name == "資料/こんにちは.txt");
        Assert.Contains(archivedEntries, entry => entry.Name == "除外フォルダ/子.txt");
        Assert.Contains(archivedEntries, entry => entry.Name == ".DS_Store");
        Assert.Contains(archivedEntries, entry => entry.Name == "資料/空" && entry.IsDirectory);

        var structure = ArchiveExtractor.GetArchiveStructureInfo(
            archive,
            cancellationToken: TestContext.Current.CancellationToken);
        var expectedUncompressedSize = new FileInfo(unicodeSource).Length
            + new FileInfo(skippedSource).Length
            + new FileInfo(ignoredSource).Length;
        Assert.False(structure.ShouldSkipFolderCreation);
        Assert.Null(structure.SingleRootItemName);
        Assert.Equal(expectedUncompressedSize, structure.TotalUncompressedSize);
        Assert.Equal(
            ["資料", "除外フォルダ"],
            structure.RootItemNames.Order(StringComparer.Ordinal).ToArray());

        var output = Path.Combine(temp.Path, "展開先");
        var extractionProgress = new List<ProgressInfo>();
        await ArchiveExtractor.ExtractArchive(
            archive,
            output,
            progressCallback: value => extractionProgress.Add(value),
            cancellationToken: TestContext.Current.CancellationToken,
            skipRelativePaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "除外フォルダ",
            });

        var extractedUnicode = Path.Combine(output, "資料", "こんにちは.txt");
        Assert.Equal(unicodeContents, File.ReadAllText(extractedUnicode));
        Assert.True(Directory.Exists(Path.Combine(output, "資料", "空")));
        Assert.False(File.Exists(Path.Combine(output, "除外フォルダ", "子.txt")));
        Assert.False(File.Exists(Path.Combine(output, ".DS_Store")));
        Assert.Contains(extractionProgress, value =>
            value.IsIndeterminate && value.Status == App.Text("Progress.Finalizing"));
        Assert.Equal(100, extractionProgress[^1].Percentage);

        var actualTimestamp = File.GetLastWriteTime(extractedUnicode);
        Assert.InRange(Math.Abs((actualTimestamp - expectedTimestamp).TotalSeconds), 0, 2);
        var actualDirectoryTimestamp = Directory.GetLastWriteTime(Path.Combine(output, "資料", "空"));
        Assert.InRange(Math.Abs((actualDirectoryTimestamp - expectedDirectoryTimestamp).TotalSeconds), 0, 2);
    }

    [Theory]
    [InlineData("LH0", "-lh0-")]
    [InlineData("LH5", "-lh5-")]
    [InlineData("LH6", "-lh6-")]
    [InlineData("LH7", "-lh7-")]
    public async Task CompressLzh_SelectedMethod_WritesExpectedHeaderAndRoundTrips(
        string selectedMethod,
        string expectedHeaderMethod)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Kagayoi.UnLhaRe の native asset は Windows 限定");
        using var temp = TestDirectory.Create($"{nameof(CompressLzh_SelectedMethod_WritesExpectedHeaderAndRoundTrips)}_{selectedMethod}");
        var source = Path.Combine(temp.Path, "source.txt");
        var contents = string.Concat(Enumerable.Repeat("日本語の LZH 圧縮方式別ラウンドトリップ\n", 4096));
        File.WriteAllText(source, contents);
        var archive = Path.Combine(temp.Path, $"{selectedMethod}.lzh");

        await ArchiveCompressor.CompressFilesAsync(
            [source],
            archive,
            Format.Lzh,
            cancellationToken: TestContext.Current.CancellationToken,
            resolvedFiles: [(source, "source.txt")],
            settingsOverride: new Settings { LzhCompressionMethod = selectedMethod });

        var entry = Assert.Single(LzhArchiveBackendProvider.Current.List(archive));
        Assert.Equal(expectedHeaderMethod, entry.Method);

        var output = Path.Combine(temp.Path, "output");
        LzhArchiveBackendProvider.Current.Extract(
            archive,
            output,
            selectedNames: null,
            progress: null,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(contents, File.ReadAllText(Path.Combine(output, "source.txt")));
    }

    [Fact]
    public async Task CompressLzh_WithExistingOutput_PreservesOriginalBytes()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Kagayoi.UnLhaRe の native asset は Windows 限定");
        using var temp = TestDirectory.Create(nameof(CompressLzh_WithExistingOutput_PreservesOriginalBytes));
        var source = Path.Combine(temp.Path, "source.txt");
        File.WriteAllText(source, "source");
        var archive = Path.Combine(temp.Path, "existing.lzh");
        var originalBytes = new byte[] { 0x4c, 0x48, 0x41, 0x2d, 0x4b, 0x45, 0x45, 0x50 };
        File.WriteAllBytes(archive, originalBytes);

        await Assert.ThrowsAsync<LzhNativeException>(() => ArchiveCompressor.CompressFilesAsync(
            [source],
            archive,
            Format.Lzh,
            cancellationToken: TestContext.Current.CancellationToken,
            resolvedFiles: [(source, "source.txt")]));

        Assert.Equal(originalBytes, File.ReadAllBytes(archive));
    }

    [Fact]
    public async Task CompressLzh_FromWindowsScan_NormalizesNestedEntrySeparators()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Kagayoi.UnLhaRe の native asset は Windows 限定");
        using var temp = TestDirectory.Create(nameof(CompressLzh_FromWindowsScan_NormalizesNestedEntrySeparators));
        var source = Path.Combine(temp.Path, "source");
        var nested = Path.Combine(source, "nested");
        var empty = Path.Combine(source, "empty");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(empty);
        File.WriteAllText(Path.Combine(nested, "file.txt"), "nested contents");
        var archive = Path.Combine(temp.Path, "scanned.lzh");

        var skipped = await ArchiveCompressor.CompressFilesAsync(
            [source],
            archive,
            Format.Lzh,
            cancellationToken: TestContext.Current.CancellationToken,
            settingsOverride: new Settings
            {
                DirectoryStructureMode = DirectoryStructureMode.ExcludeRoot,
            });

        Assert.Equal(0, skipped);
        var entries = LzhArchiveBackendProvider.Current.List(archive);
        Assert.Contains(entries, entry => entry.Name == "nested/file.txt");
        Assert.Contains(entries, entry => entry.Name == "empty" && entry.IsDirectory);
        Assert.DoesNotContain(entries, entry => entry.Name.Contains('\\'));
    }

    [Fact]
    public async Task CompressLzh_WithExclusivelyLockedFile_SkipsOnlyLockedEntry()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Kagayoi.UnLhaRe の native asset は Windows 限定");
        using var temp = TestDirectory.Create(nameof(CompressLzh_WithExclusivelyLockedFile_SkipsOnlyLockedEntry));
        var readable = Path.Combine(temp.Path, "readable.txt");
        var locked = Path.Combine(temp.Path, "locked.txt");
        File.WriteAllText(readable, "readable");
        File.WriteAllText(locked, "locked");
        var archive = Path.Combine(temp.Path, "partial.lzh");

        using var lockHandle = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var skipped = await ArchiveCompressor.CompressFilesAsync(
            [readable, locked],
            archive,
            Format.Lzh,
            cancellationToken: TestContext.Current.CancellationToken,
            resolvedFiles:
            [
                (readable, "readable.txt"),
                (locked, "locked.txt"),
            ]);

        Assert.Equal(1, skipped);
        var entries = LzhArchiveBackendProvider.Current.List(archive);
        Assert.Contains(entries, entry => entry.Name == "readable.txt");
        Assert.DoesNotContain(entries, entry => entry.Name == "locked.txt");
    }

    [Fact]
    public async Task CompressLzh_WithOnlyExclusivelyLockedFile_DoesNotPublishEmptyArchive()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Kagayoi.UnLhaRe の native asset は Windows 限定");
        using var temp = TestDirectory.Create(nameof(CompressLzh_WithOnlyExclusivelyLockedFile_DoesNotPublishEmptyArchive));
        var locked = Path.Combine(temp.Path, "locked.txt");
        File.WriteAllText(locked, "locked");
        var archive = Path.Combine(temp.Path, "empty-must-not-be-published.lzh");

        using var lockHandle = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var exception = await Assert.ThrowsAsync<LzhNativeException>(() =>
            ArchiveCompressor.CompressFilesAsync(
                [locked],
                archive,
                Format.Lzh,
                cancellationToken: TestContext.Current.CancellationToken,
                resolvedFiles: [(locked, "locked.txt")]));

        Assert.Equal(LzhErrorKind.InvalidArgument, exception.Kind);
        Assert.False(File.Exists(archive));
    }

    [Fact]
    public async Task ExtractLzh_MultipleRootsDirectlyIntoExistingFolder_PreservesUnrelatedFiles()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Kagayoi.UnLhaRe の native asset は Windows 限定");
        using var temp = TestDirectory.Create(nameof(ExtractLzh_MultipleRootsDirectlyIntoExistingFolder_PreservesUnrelatedFiles));
        var first = Path.Combine(temp.Path, "first-source.txt");
        var second = Path.Combine(temp.Path, "second-source.txt");
        File.WriteAllText(first, "first");
        File.WriteAllText(second, "second");
        var archive = Path.Combine(temp.Path, "multiple.lzh");
        await ArchiveCompressor.CompressFilesAsync(
            [first, second],
            archive,
            Format.Lzh,
            cancellationToken: TestContext.Current.CancellationToken,
            resolvedFiles:
            [
                (first, "first.txt"),
                (second, "second.txt"),
            ]);

        var output = Path.Combine(temp.Path, "output");
        Directory.CreateDirectory(output);
        var unrelated = Path.Combine(output, "keep.txt");
        File.WriteAllText(unrelated, "keep");

        var (outputPath, _) = await ArchiveProcessor.ExtractArchiveAsync(
            archive,
            output,
            outputToSameDirectory: false,
            progressWindow: null,
            cancellationToken: TestContext.Current.CancellationToken,
            settingsSnapshot: new Settings { CreateArchiveNameFolder = false });

        Assert.Equal(output, outputPath);
        Assert.Equal("keep", File.ReadAllText(unrelated));
        Assert.Equal("first", File.ReadAllText(Path.Combine(output, "first.txt")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(output, "second.txt")));
    }

    [Fact]
    public async Task CompressLzh_WithPassword_IsRejectedBeforeCreatingOutput()
    {
        using var temp = TestDirectory.Create(nameof(CompressLzh_WithPassword_IsRejectedBeforeCreatingOutput));
        var source = Path.Combine(temp.Path, "source.txt");
        File.WriteAllText(source, "source");
        var archive = Path.Combine(temp.Path, "password.lzh");

        await Assert.ThrowsAsync<InvalidOperationException>(() => ArchiveCompressor.CompressFilesAsync(
            [source],
            archive,
            Format.Lzh,
            cancellationToken: TestContext.Current.CancellationToken,
            resolvedFiles: [(source, "source.txt")],
            password: "not-supported"));

        Assert.False(File.Exists(archive));
    }

    [Fact]
    public async Task CompressLzh_SourceExceedingPerEntryLimit_IsRejectedBeforeBackendCreate()
    {
        using var temp = TestDirectory.Create(nameof(CompressLzh_SourceExceedingPerEntryLimit_IsRejectedBeforeBackendCreate));
        var source = Path.Combine(temp.Path, "oversized.bin");
        await using (var stream = new FileStream(source, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.SetLength(checked((long)LzhArchiveBackendProvider.CreateLimits.MaxEntryBytes + 1));
        var archive = Path.Combine(temp.Path, "oversized.lzh");
        var backend = new CancellingCreateBackend();
        var previous = LzhArchiveBackendProvider.Current;
        try
        {
            LzhArchiveBackendProvider.Current = backend;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ArchiveCompressor.CompressFilesAsync(
                    [source],
                    archive,
                    Format.Lzh,
                    cancellationToken: TestContext.Current.CancellationToken,
                    resolvedFiles: [(source, "oversized.bin")]));

            Assert.Equal(
                App.Text("Error.LzhEntryTooLarge", "oversized.bin", 256UL),
                exception.Message);
            Assert.False(backend.CreateCalled);
            Assert.False(File.Exists(archive));
        }
        finally
        {
            LzhArchiveBackendProvider.Current = previous;
        }
    }

    [Fact]
    public async Task ExtractLzh_UsesStreamingVisitorWhenTimestampMetadataIsUnavailable()
    {
        using var temp = TestDirectory.Create(nameof(ExtractLzh_UsesStreamingVisitorWhenTimestampMetadataIsUnavailable));
        var archive = Path.Combine(temp.Path, "no-metadata.lzh");
        File.WriteAllBytes(archive, [0x00]);
        var output = Path.Combine(temp.Path, "output");
        var backend = new ExtractingBackend();
        var previous = LzhArchiveBackendProvider.Current;
        try
        {
            LzhArchiveBackendProvider.Current = backend;
            await ArchiveExtractor.ExtractArchive(
                archive,
                output,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(ExtractingBackend.Contents, File.ReadAllText(Path.Combine(output, "本文.txt")));
            Assert.True(backend.VisitEntriesCalled);
            Assert.Equal(["本文.txt"], backend.SelectedNames);
        }
        finally
        {
            LzhArchiveBackendProvider.Current = previous;
        }
    }

    [Fact]
    public void ListLzh_WithCancelledToken_PropagatesCancellation()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellation.Cancel();

        var exception = Assert.ThrowsAny<OperationCanceledException>(() =>
            new UnLhaReArchiveBackend().List("cancelled.lzh", cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task VisitEntriesLzh_CancelledFromVisitor_PropagatesCancellation()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Kagayoi.UnLhaRe の native asset は Windows 限定");
        using var temp = TestDirectory.Create(nameof(VisitEntriesLzh_CancelledFromVisitor_PropagatesCancellation));
        var first = Path.Combine(temp.Path, "first.txt");
        var second = Path.Combine(temp.Path, "second.txt");
        File.WriteAllText(first, "first");
        File.WriteAllText(second, "second");
        var archive = Path.Combine(temp.Path, "cancel-during-visit.lzh");
        await ArchiveCompressor.CompressFilesAsync(
            [first, second],
            archive,
            Format.Lzh,
            cancellationToken: TestContext.Current.CancellationToken,
            resolvedFiles:
            [
                (first, "first.txt"),
                (second, "second.txt"),
            ]);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var visited = 0;
        var exception = Assert.ThrowsAny<OperationCanceledException>(() =>
            new UnLhaReArchiveBackend().VisitEntries(
                archive,
                _ =>
                {
                    visited++;
                    cancellation.Cancel();
                },
                cancellation.Token));

        Assert.Equal(1, visited);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public void GetArchiveStructureInfo_Lzh_UsesStreamingEntryVisitor()
    {
        using var temp = TestDirectory.Create(nameof(GetArchiveStructureInfo_Lzh_UsesStreamingEntryVisitor));
        var archive = Path.Combine(temp.Path, "streaming.lzh");
        File.WriteAllBytes(archive, [0x00]);
        var backend = new StreamingOnlyBackend(
        [
            new LzhArchiveEntry("streaming/本文.txt", "-lh0-", 17, 17, false, 0, 2),
            new LzhArchiveEntry("streaming/空", "-lhd-", 0, 0, true, 0, 2),
        ]);
        var previous = LzhArchiveBackendProvider.Current;
        try
        {
            LzhArchiveBackendProvider.Current = backend;

            var structure = ArchiveExtractor.GetArchiveStructureInfo(
                archive,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(backend.VisitEntriesCalled);
            Assert.True(structure.ShouldSkipFolderCreation);
            Assert.Equal("streaming", structure.SingleRootItemName);
            Assert.Equal(17, structure.TotalUncompressedSize);
            Assert.Equal(["streaming"], structure.RootItemNames);
        }
        finally
        {
            LzhArchiveBackendProvider.Current = previous;
        }
    }

    [Fact]
    public void GetArchiveStructureInfo_LzhNativeFailure_PreservesStableErrorKind()
    {
        using var temp = TestDirectory.Create(nameof(GetArchiveStructureInfo_LzhNativeFailure_PreservesStableErrorKind));
        var archive = Path.Combine(temp.Path, "broken.lzh");
        File.WriteAllBytes(archive, [0x00]);
        var expected = new LzhNativeException(-1, LzhErrorKind.Format, "invalid level-2 header");
        var previous = LzhArchiveBackendProvider.Current;
        try
        {
            LzhArchiveBackendProvider.Current = new ThrowingVisitBackend(expected);

            var actual = Assert.Throws<LzhNativeException>(() =>
                ArchiveExtractor.GetArchiveStructureInfo(
                    archive,
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.Same(expected, actual);
            Assert.Equal(LzhErrorKind.Format, actual.Kind);
        }
        finally
        {
            LzhArchiveBackendProvider.Current = previous;
        }
    }

    [Fact]
    public void GetArchiveUncompressedSize_Lzh_CancellationDuringListIsNotSwallowed()
    {
        using var temp = TestDirectory.Create(nameof(GetArchiveUncompressedSize_Lzh_CancellationDuringListIsNotSwallowed));
        var archive = Path.Combine(temp.Path, "during-list.lzh");
        File.WriteAllBytes(archive, [0x00]);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var previous = LzhArchiveBackendProvider.Current;
        try
        {
            LzhArchiveBackendProvider.Current = new CancellingListBackend(cancellation);

            var exception = Assert.ThrowsAny<OperationCanceledException>(() =>
                DiskSpaceChecker.GetArchiveUncompressedSize(archive, cancellation.Token));

            Assert.Equal(cancellation.Token, exception.CancellationToken);
        }
        finally
        {
            LzhArchiveBackendProvider.Current = previous;
        }
    }

    [Fact]
    public async Task ExtractLzh_UnsafeEntryIsRejectedBeforeBackendWrites()
    {
        using var temp = TestDirectory.Create(nameof(ExtractLzh_UnsafeEntryIsRejectedBeforeBackendWrites));
        var archive = Path.Combine(temp.Path, "unsafe.lzh");
        File.WriteAllBytes(archive, [0x00]);
        var output = Path.Combine(temp.Path, "output");
        var backend = new UnsafeEntryBackend();
        var previous = LzhArchiveBackendProvider.Current;
        try
        {
            LzhArchiveBackendProvider.Current = backend;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ArchiveExtractor.ExtractArchive(
                    archive,
                    output,
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.False(backend.ExtractCalled);
            Assert.False(File.Exists(Path.Combine(temp.Path, "outside.txt")));
        }
        finally
        {
            LzhArchiveBackendProvider.Current = previous;
        }
    }

    [Fact]
    public async Task ExtractLzh_WhenStreamingListFailsAfterFirstEntry_DoesNotWrite()
    {
        using var temp = TestDirectory.Create(nameof(ExtractLzh_WhenStreamingListFailsAfterFirstEntry_DoesNotWrite));
        var archive = Path.Combine(temp.Path, "late-header-error.lzh");
        File.WriteAllBytes(archive, [0x00]);
        var output = Path.Combine(temp.Path, "output");
        var backend = new LateFailingVisitBackend();
        var previous = LzhArchiveBackendProvider.Current;
        try
        {
            LzhArchiveBackendProvider.Current = backend;

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ArchiveExtractor.ExtractArchive(
                    archive,
                    output,
                    cancellationToken: TestContext.Current.CancellationToken));

            Assert.False(backend.ExtractCalled);
            Assert.False(Directory.Exists(output));
        }
        finally
        {
            LzhArchiveBackendProvider.Current = previous;
        }
    }

    [Fact]
    public async Task CompressLzh_CancelledFromFinalizingProgress_ForwardsTokenAndDoesNotPublish()
    {
        using var temp = TestDirectory.Create(nameof(CompressLzh_CancelledFromFinalizingProgress_ForwardsTokenAndDoesNotPublish));
        var source = Path.Combine(temp.Path, "source.txt");
        File.WriteAllText(source, "source");
        var archive = Path.Combine(temp.Path, "cancelled.lzh");
        var backend = new CancellingCreateBackend();
        var previous = LzhArchiveBackendProvider.Current;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        try
        {
            LzhArchiveBackendProvider.Current = backend;
            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                ArchiveCompressor.CompressFilesAsync(
                    [source],
                    archive,
                    Format.Lzh,
                    new InlineTestProgress(value =>
                    {
                        if (value.Status == App.Text("Progress.Finalizing"))
                            cancellation.Cancel();
                    }),
                    cancellation.Token,
                    [(source, "source.txt")]));

            Assert.Equal(cancellation.Token, exception.CancellationToken);
            Assert.True(backend.CreateCalled);
            Assert.Equal(LzhCompressionMethod.Lh5, backend.Method);
            Assert.Equal("source.txt", Assert.Single(backend.Entries).Name);
            Assert.False(File.Exists(archive));
        }
        finally
        {
            LzhArchiveBackendProvider.Current = previous;
        }
    }

    private sealed class InlineTestProgress(Action<ProgressInfo> report) : IProgress<ProgressInfo>
    {
        public void Report(ProgressInfo value) => report(value);
    }

    private sealed class CancellingCreateBackend : ILzhArchiveBackend
    {
        internal bool CreateCalled { get; private set; }

        internal IReadOnlyList<LzhArchiveSourceEntry> Entries { get; private set; } = [];

        internal LzhCompressionMethod Method { get; private set; }

        public IReadOnlyList<LzhArchiveEntry> List(
            string archive,
            CancellationToken cancellationToken = default,
            IProgress<LzhArchiveProgress>? progress = null) => [];

        public void Extract(
            string archive,
            string destination,
            IReadOnlyList<string>? selectedNames,
            IProgress<LzhArchiveProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public LzhArchiveCreateReport Create(
            string output,
            IReadOnlyList<LzhArchiveSourceEntry> entries,
            LzhCompressionMethod method,
            IProgress<LzhArchiveProgress>? progress,
            CancellationToken cancellationToken)
        {
            CreateCalled = true;
            Entries = entries;
            Method = method;
            progress?.Report(new LzhArchiveProgress(LzhArchiveProgressPhase.Finalize, 0, 0));
            cancellationToken.ThrowIfCancellationRequested();
            return new LzhArchiveCreateReport(
                entries.Select(entry => new LzhArchiveCreateEntryResult(
                    entry.Name,
                    LzhArchiveCreateEntryStatus.Written,
                    Error: null)).ToArray());
        }
    }

    private sealed class StreamingOnlyBackend(IReadOnlyList<LzhArchiveEntry> entries) : ILzhArchiveBackend
    {
        internal bool VisitEntriesCalled { get; private set; }

        public IReadOnlyList<LzhArchiveEntry> List(
            string archive,
            CancellationToken cancellationToken = default,
            IProgress<LzhArchiveProgress>? progress = null) =>
            throw new InvalidOperationException("構造集計で全件 List を使用してはいけません。");

        public void VisitEntries(
            string archive,
            Action<LzhArchiveEntry> visitor,
            CancellationToken cancellationToken = default,
            IProgress<LzhArchiveProgress>? progress = null)
        {
            VisitEntriesCalled = true;
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                visitor(entry);
            }
        }

        public void Extract(
            string archive,
            string destination,
            IReadOnlyList<string>? selectedNames,
            IProgress<LzhArchiveProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public LzhArchiveCreateReport Create(
            string output,
            IReadOnlyList<LzhArchiveSourceEntry> entries,
            LzhCompressionMethod method,
            IProgress<LzhArchiveProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingVisitBackend(LzhNativeException exception) : ILzhArchiveBackend
    {
        public IReadOnlyList<LzhArchiveEntry> List(
            string archive,
            CancellationToken cancellationToken = default,
            IProgress<LzhArchiveProgress>? progress = null) =>
            throw new InvalidOperationException("構造解析で全件 List を使用してはいけません。");

        public void VisitEntries(
            string archive,
            Action<LzhArchiveEntry> visitor,
            CancellationToken cancellationToken = default,
            IProgress<LzhArchiveProgress>? progress = null) => throw exception;

        public void Extract(
            string archive,
            string destination,
            IReadOnlyList<string>? selectedNames,
            IProgress<LzhArchiveProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public LzhArchiveCreateReport Create(
            string output,
            IReadOnlyList<LzhArchiveSourceEntry> entries,
            LzhCompressionMethod method,
            IProgress<LzhArchiveProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CancellingListBackend(CancellationTokenSource cancellation) : ILzhArchiveBackend
    {
        public IReadOnlyList<LzhArchiveEntry> List(
            string archive,
            CancellationToken cancellationToken = default,
            IProgress<LzhArchiveProgress>? progress = null)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return [];
        }

        public void Extract(
            string archive,
            string destination,
            IReadOnlyList<string>? selectedNames,
            IProgress<LzhArchiveProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public LzhArchiveCreateReport Create(
            string output,
            IReadOnlyList<LzhArchiveSourceEntry> entries,
            LzhCompressionMethod method,
            IProgress<LzhArchiveProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ExtractingBackend : ILzhArchiveBackend
    {
        internal const string Contents = "timestamp metadata is optional";

        internal bool VisitEntriesCalled { get; private set; }

        internal IReadOnlyList<string>? SelectedNames { get; private set; }

        public IReadOnlyList<LzhArchiveEntry> List(
            string archive,
            CancellationToken cancellationToken = default,
            IProgress<LzhArchiveProgress>? progress = null) =>
            throw new InvalidOperationException("実展開で全件 List を使用してはいけません。");

        public void VisitEntries(
            string archive,
            Action<LzhArchiveEntry> visitor,
            CancellationToken cancellationToken = default,
            IProgress<LzhArchiveProgress>? progress = null)
        {
            VisitEntriesCalled = true;
            cancellationToken.ThrowIfCancellationRequested();
            visitor(new LzhArchiveEntry(
                "本文.txt",
                "-lh0-",
                (ulong)Contents.Length,
                (ulong)Contents.Length,
                false,
                0,
                2));
        }

        public void Extract(
            string archive,
            string destination,
            IReadOnlyList<string>? selectedNames,
            IProgress<LzhArchiveProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SelectedNames = selectedNames;
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "本文.txt"), Contents);
        }

        public LzhArchiveCreateReport Create(
            string output,
            IReadOnlyList<LzhArchiveSourceEntry> entries,
            LzhCompressionMethod method,
            IProgress<LzhArchiveProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnsafeEntryBackend : ILzhArchiveBackend
    {
        internal bool ExtractCalled { get; private set; }

        public IReadOnlyList<LzhArchiveEntry> List(
            string archive,
            CancellationToken cancellationToken = default,
            IProgress<LzhArchiveProgress>? progress = null) =>
        [
            new LzhArchiveEntry("../outside.txt", "-lh0-", 1, 1, false, 0, 2),
        ];

        public void Extract(
            string archive,
            string destination,
            IReadOnlyList<string>? selectedNames,
            IProgress<LzhArchiveProgress>? progress,
            CancellationToken cancellationToken) => ExtractCalled = true;

        public LzhArchiveCreateReport Create(
            string output,
            IReadOnlyList<LzhArchiveSourceEntry> entries,
            LzhCompressionMethod method,
            IProgress<LzhArchiveProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class LateFailingVisitBackend : ILzhArchiveBackend
    {
        internal bool ExtractCalled { get; private set; }

        public IReadOnlyList<LzhArchiveEntry> List(
            string archive,
            CancellationToken cancellationToken = default,
            IProgress<LzhArchiveProgress>? progress = null) =>
            throw new InvalidOperationException("実展開で全件 List を使用してはいけません。");

        public void VisitEntries(
            string archive,
            Action<LzhArchiveEntry> visitor,
            CancellationToken cancellationToken = default,
            IProgress<LzhArchiveProgress>? progress = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            visitor(new LzhArchiveEntry("safe.txt", "-lh0-", 1, 1, false, 0, 2));
            throw new InvalidDataException("後続ヘッダーが破損しています。");
        }

        public void Extract(
            string archive,
            string destination,
            IReadOnlyList<string>? selectedNames,
            IProgress<LzhArchiveProgress>? progress,
            CancellationToken cancellationToken) => ExtractCalled = true;

        public LzhArchiveCreateReport Create(
            string output,
            IReadOnlyList<LzhArchiveSourceEntry> entries,
            LzhCompressionMethod method,
            IProgress<LzhArchiveProgress>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
