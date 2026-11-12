using Cube.FileSystem.SevenZip;
using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// ArchiveCompressor のユニットテスト（実装を信用しないエッジケース重視）
/// ネストした .gitignore / 圧縮元内の除外ファイル候補（SourceIgnoreFileNames）に関するスキャンのテストは
/// ArchiveCompressorNestedIgnoreTests.cs（同じ partial クラス）に置く。
/// </summary>
public partial class ArchiveCompressorTests
{
    [Fact]
    public async Task ScanSourceFiles_EmptyTree_ReportsProgressAndHonorsCancellation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lhamiel_scan_{Guid.NewGuid():N}");
        using var cts = new CancellationTokenSource();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "empty", "child"));
            var progress = new SyncRecordingProgress(_ => cts.Cancel());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ArchiveCompressor.ScanSourceFiles(
                [root], GitignoreMatcher.Empty, cts.Token,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                respectNestedGitignore: true, progress: progress));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_ManySiblingRules_KeepScopesAndPruneNestedDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lhamiel_scan_{Guid.NewGuid():N}");
        try
        {
            for (var i = 0; i < 120; i++)
            {
                var repo = Path.Combine(root, $"repo{i}");
                Directory.CreateDirectory(Path.Combine(repo, "ignored", "deep", "child"));
                File.WriteAllText(Path.Combine(repo, ".gitignore"), "ignored/\n*.log\n");
                File.WriteAllText(Path.Combine(repo, "ignored", ".gitignore"), "!rescue.txt\n");
                File.WriteAllText(Path.Combine(repo, "ignored", "rescue.txt"), "excluded");
                for (var j = 0; j < 20; j++)
                    File.WriteAllText(Path.Combine(repo, $"file{j}.txt"), "included");
                File.WriteAllText(Path.Combine(repo, "debug.log"), "excluded");
            }

            var timer = System.Diagnostics.Stopwatch.StartNew();
            var result = await ArchiveCompressor.ScanSourceFiles(
                [root], GitignoreMatcher.Empty, TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                normalizeUnicodeOverride: false, includeHiddenAndSystemEntriesOverride: true,
                respectNestedGitignore: true);
            TestContext.Current.TestOutputHelper!.WriteLine($"走査時間: {timer.ElapsedMilliseconds} ms");
            Assert.Equal(120 * 21, result.Count);
            Assert.DoesNotContain(result, entry => entry.fullPath.Contains("ignored") || entry.fullPath.EndsWith(".log"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // === ParseFormat ===

    [Theory]
    [InlineData("ZIP", Format.Zip)]
    [InlineData("7Z", Format.SevenZip)]
    [InlineData("TAR", Format.Tar)]
    [InlineData("LZH", Format.Lzh)]
    [InlineData("LHA", Format.Lzh)]
    [InlineData("GZ", Format.GZip)]
    [InlineData("BZ2", Format.BZip2)]
    [InlineData("XZ", Format.XZ)]
    public void ParseFormat_WithUpperCase_ReturnsCorrectFormat(string input, Format expected)
    {
        Assert.Equal(expected, ArchiveCompressor.ParseFormat(input));
    }

    [Theory]
    [InlineData("zip", Format.Zip)]
    [InlineData("7z", Format.SevenZip)]
    [InlineData("tar", Format.Tar)]
    [InlineData("lzh", Format.Lzh)]
    [InlineData("lha", Format.Lzh)]
    [InlineData("gz", Format.GZip)]
    [InlineData("bz2", Format.BZip2)]
    [InlineData("xz", Format.XZ)]
    public void ParseFormat_WithLowerCase_ReturnsCorrectFormat(string input, Format expected)
    {
        Assert.Equal(expected, ArchiveCompressor.ParseFormat(input));
    }

    [Theory]
    [InlineData("Zip")]
    [InlineData("zIp")]
    [InlineData("ZiP")]
    public void ParseFormat_WithMixedCase_ReturnsZip(string input)
    {
        Assert.Equal(Format.Zip, ArchiveCompressor.ParseFormat(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("rar")]
    [InlineData("cab")]
    [InlineData("unknown")]
    [InlineData("  zip  ")]
    public void ParseFormat_WithInvalidOrUnsupported_ReturnsZipAsDefault(string input)
    {
        // 不明な形式はデフォルトでZIPにフォールバックすべき
        Assert.Equal(Format.Zip, ArchiveCompressor.ParseFormat(input));
    }

    // === GetCompressedFileName ===

    [Fact]
    public void GetCompressedFileName_WithFile_ReturnsCorrectPath()
    {
        var result = ArchiveCompressor.GetCompressedFileName(@"C:\temp\document.txt", "zip");
        Assert.EndsWith(".zip", result);
        Assert.Contains("document", result);
    }

    [Fact]
    public void GetCompressedFileName_WithFolder_ReturnsCorrectPath()
    {
        var result = ArchiveCompressor.GetCompressedFileName(@"C:\temp\MyProject", "7z");
        Assert.EndsWith(".7z", result);
        Assert.Contains("MyProject", result);
    }

    [Fact]
    public void GetCompressedFileName_WithTrailingSeparator_HandlesCorrectly()
    {
        // フォルダパスの末尾にセパレータがある場合
        var result = ArchiveCompressor.GetCompressedFileName(@"C:\temp\MyProject\", "zip");
        Assert.EndsWith(".zip", result);
        // フォルダ名が正しく取れること（空文字にならないこと）
        Assert.DoesNotContain("..", result);
        Assert.Contains("MyProject", result);
    }

    [Fact]
    public void GetCompressedFileName_WithOutputDirectory_UsesOutputDir()
    {
        var result = ArchiveCompressor.GetCompressedFileName(
            @"C:\source\data.txt", "zip", @"D:\output", outputToSameDirectory: false);
        Assert.StartsWith(@"D:\output", result);
    }

    [Fact]
    public void GetCompressedFileName_WithSameDirectory_UsesSourceDir()
    {
        var result = ArchiveCompressor.GetCompressedFileName(
            @"C:\source\data.txt", "zip", @"D:\output", outputToSameDirectory: true);
        Assert.StartsWith(@"C:\source", result);
    }

    [Fact]
    public void GetCompressedFileName_WithDotFile_HandlesCorrectly()
    {
        // .gitignore のようなドットファイル
        var result = ArchiveCompressor.GetCompressedFileName(@"C:\temp\.gitignore", "zip");
        Assert.EndsWith(".zip", result);
        // ファイル名部分が空にならないこと
        var fileName = Path.GetFileName(result);
        Assert.True(fileName.Length > 4, $"ファイル名が短すぎます: {fileName}");
    }

    [Fact]
    public void GetCompressedFileName_FormatIsLowerCase()
    {
        var result = ArchiveCompressor.GetCompressedFileName(@"C:\temp\file.txt", "ZIP");
        // 拡張子は小文字であるべき
        Assert.EndsWith(".zip", result);
        Assert.DoesNotContain(".ZIP", result);
    }

    // === WritableFormats ===

    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    [InlineData("tar")]
    [InlineData("gz")]
    [InlineData("bz2")]
    [InlineData("xz")]
    [InlineData("lzh")]
    [InlineData("lha")]
    public void WritableFormats_ContainsExpected(string format)
    {
        Assert.Contains(format, ArchiveCompressor.WritableFormats);
    }

    [Theory]
    [InlineData("ZIP")]
    [InlineData("7Z")]
    [InlineData("TAR")]
    [InlineData("LZH")]
    [InlineData("LHA")]
    public void WritableFormats_IsCaseInsensitive(string format)
    {
        Assert.Contains(format, ArchiveCompressor.WritableFormats);
    }

    [Theory]
    [InlineData("rar")]
    [InlineData("cab")]
    [InlineData("arj")]
    [InlineData("")]
    public void WritableFormats_DoesNotContainUnsupported(string format)
    {
        Assert.DoesNotContain(format, ArchiveCompressor.WritableFormats);
    }

    // === ShouldExcludeFile (gitignore semantics) ===

    [Fact]
    public void ShouldExcludeFile_WithMatchingFilenameInSingleFileMode_ReturnsTrue()
    {
        // 単一ファイル（rootDir=null）: ベース名のみで判定
        var matcher = GitignoreMatcher.Compile([".DS_Store", "Thumbs.db"]);
        Assert.True(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\.DS_Store", matcher));
    }

    [Fact]
    public void ShouldExcludeFile_WithMatchingDirectorySegment_ReturnsTrue()
    {
        // rootDir を指定すれば中間ディレクトリ名 (__MACOSX) のセグメントマッチが効く
        var matcher = GitignoreMatcher.Compile(["__MACOSX"]);
        Assert.True(ArchiveCompressor.ShouldExcludeFile(@"C:\root\__MACOSX\file.txt", matcher, rootDir: @"C:\root"));
    }

    [Fact]
    public void ShouldExcludeFile_WithNoMatch_ReturnsFalse()
    {
        var matcher = GitignoreMatcher.Compile([".DS_Store"]);
        Assert.False(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\readme.txt", matcher));
    }

    [Fact]
    public void ShouldExcludeFile_WithEmptyMatcher_ReturnsFalse()
    {
        Assert.False(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\.DS_Store", GitignoreMatcher.Empty));
    }

    [Fact]
    public void ShouldExcludeFile_CaseInsensitive_ReturnsTrue()
    {
        var matcher = GitignoreMatcher.Compile(["thumbs.db"]);
        Assert.True(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\THUMBS.DB", matcher));
    }

    [Fact]
    public void ShouldExcludeFile_PartialMatch_ReturnsFalse()
    {
        // パターンがファイル名の一部にしかマッチしない場合は除外しない
        var matcher = GitignoreMatcher.Compile(["Test"]);
        Assert.False(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\TestFile.txt", matcher));
    }

    [Fact]
    public void ShouldExcludeFile_GitignoreGlobPattern_MatchesExtension()
    {
        // gitignore 互換: *.log は任意の .log ファイルにマッチ
        var matcher = GitignoreMatcher.Compile(["*.log"]);
        Assert.True(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\debug.log", matcher));
        Assert.False(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\debug.txt", matcher));
    }

    [Fact]
    public void ShouldExcludeFile_GitignoreNegationPattern_ReIncludesFile()
    {
        // gitignore 互換: !pattern で除外を取り消し
        var matcher = GitignoreMatcher.Compile(["*.log", "!keep.log"]);
        Assert.True(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\debug.log", matcher));
        Assert.False(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\keep.log", matcher));
    }

    // === ScanSourceFiles: 空ディレクトリ ===

    [Fact]
    public async Task ScanSourceFiles_IncludesEmptyDirectories()
    {
        // 空ディレクトリを含むディレクトリ構造を作成
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "Source");

        try
        {
            // テスト構造: 空ディレクトリ + ファイルを含むディレクトリ
            Directory.CreateDirectory(Path.Combine(sourceDir, "EmptyDir"));
            Directory.CreateDirectory(Path.Combine(sourceDir, "EmptyNested", "SubEmpty"));
            Directory.CreateDirectory(Path.Combine(sourceDir, "HasFiles"));
            File.WriteAllText(Path.Combine(sourceDir, "HasFiles", "test.txt"), "hello");
            File.WriteAllText(Path.Combine(sourceDir, "root.txt"), "world");

            var result = await ArchiveCompressor.ScanSourceFiles(
                [sourceDir],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot);

            // ファイルが含まれること
            Assert.Contains(result, r => r.relativePath.EndsWith("root.txt"));
            Assert.Contains(result, r => r.relativePath.EndsWith("test.txt"));

            // 空ディレクトリが末尾 / 付きで含まれること
            Assert.Contains(result, r => r.relativePath.EndsWith("EmptyDir/"));
            Assert.Contains(result, r => r.relativePath.EndsWith("SubEmpty/"));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_FlatMode_ExcludesEmptyDirectories()
    {
        // Flatモードでは空ディレクトリを含めない
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "Source");

        try
        {
            Directory.CreateDirectory(Path.Combine(sourceDir, "EmptyDir"));
            File.WriteAllText(Path.Combine(sourceDir, "test.txt"), "hello");

            var result = await ArchiveCompressor.ScanSourceFiles(
                [sourceDir],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.Flat);

            // ファイルのみ含まれ、ディレクトリエントリは含まれない
            Assert.Single(result);
            Assert.DoesNotContain(result, r => r.relativePath.EndsWith("/"));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_DirectoryWithOnlyEmptySubdirs_AllIncluded()
    {
        // ファイルが全くないディレクトリでも空サブディレクトリが全て含まれる
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "AllEmpty");

        try
        {
            Directory.CreateDirectory(Path.Combine(sourceDir, "A"));
            Directory.CreateDirectory(Path.Combine(sourceDir, "B", "C"));

            var result = await ArchiveCompressor.ScanSourceFiles(
                [sourceDir],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot);

            // 空ディレクトリのみが含まれる（ファイルなし）
            Assert.All(result, r => Assert.EndsWith("/", r.relativePath));
            Assert.Contains(result, r => r.relativePath.Contains("A"));
            Assert.Contains(result, r => r.relativePath.Contains("B"));
            Assert.Contains(result, r => r.relativePath.Contains("C"));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_IncludeHiddenAndSystemEntries_IncludesHiddenGitDirectory()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "Source");
        var gitDirPath = Path.Combine(sourceDir, ".git");

        try
        {
            var gitDir = Directory.CreateDirectory(gitDirPath);
            File.WriteAllText(Path.Combine(gitDirPath, "config"), "repository");
            gitDir.Attributes |= FileAttributes.Hidden;

            var result = await ArchiveCompressor.ScanSourceFiles(
                [sourceDir],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                includeHiddenAndSystemEntriesOverride: true);

            Assert.Contains(result, r => r.relativePath.Contains(".git") && r.relativePath.EndsWith("config"));
        }
        finally
        {
            if (Directory.Exists(gitDirPath))
                new DirectoryInfo(gitDirPath).Attributes &= ~FileAttributes.Hidden;
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task ScanSourceFiles_ExcludeHiddenAndSystemEntries_SkipsHiddenGitDirectory()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "Source");
        var gitDirPath = Path.Combine(sourceDir, ".git");

        try
        {
            var gitDir = Directory.CreateDirectory(gitDirPath);
            File.WriteAllText(Path.Combine(gitDirPath, "config"), "repository");
            gitDir.Attributes |= FileAttributes.Hidden;

            var result = await ArchiveCompressor.ScanSourceFiles(
                [sourceDir],
                GitignoreMatcher.Empty,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot,
                includeHiddenAndSystemEntriesOverride: false);

            Assert.DoesNotContain(result, r => r.relativePath.Contains(".git") || r.relativePath.EndsWith("config"));
        }
        finally
        {
            if (Directory.Exists(gitDirPath))
                new DirectoryInfo(gitDirPath).Attributes &= ~FileAttributes.Hidden;
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task CompressFilesAsync_WhenSevenZipExceptionThrown_DeletesPartialOutput()
    {
        // Windows 固有: 「存在しないドライブレター」を出力先にして例外を誘発する。
        // 非 Windows ではドライブレターの概念が無いのでスキップ。
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows のドライブレターに依存するテスト");

        // 圧縮対象ファイル → outputPath まで設定済の状態で writer.Save が SevenZipException 相当を
        // 投げると、書きかけの outputPath が残ったままになるバグの回帰テスト。
        // 結果として outputPath が残らない（部分ファイルが残らない）ことを確認する。
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "src");
        try
        {
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "a.txt"), "hello");

            // 存在しないドライブレターを動的に探す。Z:\ などをハードコードすると、テスト環境で
            // 実際にそのドライブが存在すると例外が出なくなり flake になる。
            var fakeDrive = FindUnusedDriveLetter();
            if (fakeDrive is null)
            {
                Assert.Skip("利用可能な未使用ドライブレターが見つからない");
                return;
            }
            var fakePath = $"{fakeDrive}:\\does_not_exist_lhamiel_test\\out.zip";

            await Assert.ThrowsAnyAsync<Exception>(async () =>
                await ArchiveCompressor.CompressFilesAsync(
                    [sourceDir],
                    fakePath,
                    Cube.FileSystem.SevenZip.Format.Zip,
                    cancellationToken: TestContext.Current.CancellationToken));

            // 何らかの例外が出るのが期待挙動 (DirectoryNotFoundException / IOException / SevenZipException など)。
            // 重要なのは出力先に部分ファイルが残らないこと。
            Assert.False(File.Exists(fakePath), $"部分ファイルが残っている: {fakePath}");
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    /// <summary>
    /// テスト用に「現在マウントされていないドライブレター」を 1 つ返す。見つからなければ null。
    /// D〜Z を逆順 (Z 寄り) で探す（A〜C はシステム予約寄りで避ける）。
    /// </summary>
    private static char? FindUnusedDriveLetter()
    {
        var inUse = DriveInfo.GetDrives()
            .Select(d => char.ToUpperInvariant(d.Name[0]))
            .ToHashSet();
        for (var c = 'Z'; c >= 'D'; c--)
        {
            if (!inUse.Contains(c))
                return c;
        }
        return null;
    }

    // === 生成アーカイブの内容検証（スキャン出力ではなく成果物を ArchiveReader.Items で確認） ===

    [Fact]
    public async Task CompressFilesAsync_EmptyDirectoryWithExcludedFiles_DoesNotReviveExcludedFilesInArchive()
    {
        // RTK レビュー #1 (EMPTY-DIR-LEAK) 回帰テスト。
        // スキャンで「空」と判定されたディレクトリ（実体には除外ファイルだけが残る）を
        // writer.Add(realDir, "rel/") で渡すと、ライブラリの AddRecursive がフィルタなしに再走査して
        // 除外ファイルを復活させていた。空マーカー経由の追加でこれが起きないことを、
        // スキャン出力ではなく「生成アーカイブの ArchiveReader.Items」レベルで検証する（#16 SCAN-TEST-GAP）。
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "Source");
        var logsDir = Path.Combine(sourceDir, "logs");
        try
        {
            Directory.CreateDirectory(logsDir);
            File.WriteAllText(Path.Combine(sourceDir, "keep.txt"), "keep me");
            // logs/ の中身は除外対象 (*.log) のみ → スキャン的には「空ディレクトリ」になる。
            File.WriteAllText(Path.Combine(logsDir, "secret.log"), "should NOT be in archive");

            var matcher = GitignoreMatcher.Compile(["*.log"]);

            // スキャンで解決済みリストを得る（*.log は除外、logs/ は空ディレクトリエントリとして残る）。
            var resolved = await ArchiveCompressor.ScanSourceFiles(
                [sourceDir],
                matcher,
                cancellationToken: TestContext.Current.CancellationToken,
                dirModeOverride: DirectoryStructureMode.IncludeRoot);

            // サニティ: スキャン段階で secret.log は除外され、logs/ は空ディレクトリとして残る。
            Assert.DoesNotContain(resolved, r => r.relativePath.EndsWith("secret.log"));
            Assert.Contains(resolved, r => r.relativePath.EndsWith("logs/"));
            Assert.Contains(resolved, r => r.relativePath.EndsWith("keep.txt"));

            // 解決済みリストをそのまま渡して圧縮（内部 .lhaignore 読み込みをバイパスし、上の matcher 結果で固定）。
            var zipPath = Path.Combine(testRoot, "out.zip");
            await ArchiveCompressor.CompressFilesAsync(
                [sourceDir], zipPath, Format.Zip,
                new Progress<ProgressInfo>(),
                TestContext.Current.CancellationToken,
                resolvedFiles: resolved);

            Assert.True(File.Exists(zipPath), "アーカイブが生成されていない");

            // 生成アーカイブの中身を検証する（ここが本丸: 成果物に除外ファイルが復活していないこと）。
            using var reader = new ArchiveReader(zipPath);
            var names = reader.Items.Select(i => (i.FullName ?? string.Empty).Replace('\\', '/')).ToList();

            // 除外した secret.log がアーカイブに復活していないこと（#1 の回帰防止）。
            Assert.DoesNotContain(names, n => n.EndsWith("secret.log"));
            // 含めるべき keep.txt は存在すること。
            Assert.Contains(names, n => n.EndsWith("keep.txt"));
            // 空ディレクトリ logs/ のエントリは保持されること。
            Assert.Contains(names, n => n.TrimEnd('/').EndsWith("logs"));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task CompressFilesAsync_ReportsFinalizingBeforeFinal100()
    {
        // 進捗 100% 張り付き対策の表示契約を検証する (528k ファイル / 60GB の実測で
        // 100% 表示のまま数分固まって見えた問題への回帰テスト):
        // (1) Save 完了後〜writer Dispose の区間で「仕上げ処理中」(IsIndeterminate) が報告される
        // (2) 最終報告は確定 100%
        // (3) 仕上げ表示の後に確定パーセンテージが混ざらない (finalizing フラグの抑止契約)
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "Source");
        try
        {
            Directory.CreateDirectory(sourceDir);
            for (var i = 0; i < 5; i++)
                File.WriteAllText(Path.Combine(sourceDir, $"file{i}.txt"), new string('a', 1000));

            var reports = new List<ProgressInfo>();
            var recorder = new SyncRecordingProgress(info => { lock (reports) reports.Add(info); });

            var zipPath = Path.Combine(testRoot, "out.zip");
            await ArchiveCompressor.CompressFilesAsync(
                [sourceDir], zipPath, Format.Zip,
                recorder,
                TestContext.Current.CancellationToken);

            Assert.True(File.Exists(zipPath), "アーカイブが生成されていない");

            ProgressInfo[] snapshot;
            lock (reports) snapshot = [.. reports];
            Assert.NotEmpty(snapshot);

            // 最終報告は確定 100% (ウィンドウクローズ前にバーを完了状態へ戻す契約)
            var last = snapshot[^1];
            Assert.False(last.IsIndeterminate);
            Assert.Equal(100, last.Percentage);

            // 「仕上げ処理中」の不確定報告が最終 100% より前に必ず存在する
            var finalizingText = App.Text("Progress.Finalizing");
            var finalizingIdx = Array.FindIndex(snapshot, i => i.IsIndeterminate && i.Status == finalizingText);
            Assert.True(finalizingIdx >= 0, "仕上げ表示 (Progress.Finalizing) が報告されていない");
            Assert.True(finalizingIdx < snapshot.Length - 1, "仕上げ表示が最終報告より後にある");

            // 仕上げ表示以降、最終 100% までの間に確定パーセンテージが混ざらない
            for (var i = finalizingIdx + 1; i < snapshot.Length - 1; i++)
                Assert.True(snapshot[i].IsIndeterminate, $"仕上げ表示後に確定進捗が報告された: index={i}");
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    /// <summary>
    /// Progress&lt;T&gt; と違い SynchronizationContext を介さず同期的に記録する
    /// IProgress 実装（報告順序の検証用）。
    /// </summary>
    private sealed class SyncRecordingProgress(Action<ProgressInfo> handler) : IProgress<ProgressInfo>
    {
        public void Report(ProgressInfo value) => handler(value);
    }

    [Fact]
    public async Task CompressFilesAsync_FileLockedWithFileShareNone_SkipsAndContinues()
    {
        // Visual Studio の .vsidx 等、FileShare.None で他プロセスが握っているファイルは
        // ライブラリの ArchiveWriter.AddItem の fail-fast (FileShare.Read → FileShare.ReadWrite|Delete
        // の 2 段試行) で両方失敗して AccessException が飛ぶ。1 ファイル不能で圧縮全体を
        // 死なせず、残りのファイルで圧縮を続行することを検証する。
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "Source");
        try
        {
            Directory.CreateDirectory(sourceDir);
            var readablePath = Path.Combine(sourceDir, "readable.txt");
            var lockedPath = Path.Combine(sourceDir, "locked.bin");
            File.WriteAllText(readablePath, "ok");
            File.WriteAllText(lockedPath, "you cannot read me");

            // FileShare.None で握る（VS の .vsidx 相当の共有モード）。
            using var lockHandle = new FileStream(
                lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var zipPath = Path.Combine(testRoot, "out.zip");
            var skipped = await ArchiveCompressor.CompressFilesAsync(
                [sourceDir], zipPath, Format.Zip,
                new Progress<ProgressInfo>(),
                TestContext.Current.CancellationToken);

            Assert.True(File.Exists(zipPath), "アーカイブが生成されていない");
            // 呼び出し側 (ArchiveProcessor) がパスワード保護圧縮の UI 警告に使う戻り値 (codex P2 #3386876544)
            Assert.Equal(1, skipped);

            // 読めるファイルだけが入っていて、ロック中ファイルは抜けていること。
            using var reader = new ArchiveReader(zipPath);
            var names = reader.Items.Select(i => (i.FullName ?? string.Empty).Replace('\\', '/')).ToList();
            Assert.Contains(names, n => n.EndsWith("readable.txt"));
            Assert.DoesNotContain(names, n => n.EndsWith("locked.bin"));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public async Task CompressFilesAsync_PasswordProtectedWithLockedFile_ReturnsSkippedCount()
    {
        // codex P2 #3386876544: パスワード保護圧縮でロック中ファイルがスキップされた場合、
        // 「アーカイブに含まれず平文のまま残ったファイルがある」ことを呼び出し側
        // (ArchiveProcessor) が UI 警告できるよう、スキップ件数を戻り値で返す。
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "Source");
        try
        {
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "readable.txt"), "ok");
            var lockedPath = Path.Combine(sourceDir, "locked.bin");
            File.WriteAllText(lockedPath, "you cannot read me");

            // スキップが無ければ 0 を返す
            var cleanZip = Path.Combine(testRoot, "clean.zip");
            var noSkip = await ArchiveCompressor.CompressFilesAsync(
                [sourceDir], cleanZip, Format.Zip, null,
                TestContext.Current.CancellationToken, password: "secret-pass");
            Assert.Equal(0, noSkip);

            // FileShare.None で握る（VS の .vsidx 相当の共有モード）
            using var lockHandle = new FileStream(
                lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var zipPath = Path.Combine(testRoot, "out.zip");
            var skipped = await ArchiveCompressor.CompressFilesAsync(
                [sourceDir], zipPath, Format.Zip, null,
                TestContext.Current.CancellationToken, password: "secret-pass");

            Assert.Equal(1, skipped);
            // 残りのファイルでパスワード付きアーカイブ自体は正しく作られていること
            using var reader = new ArchiveReader(zipPath, "secret-pass", new ArchiveOption());
            var names = reader.Items.Select(i => (i.FullName ?? string.Empty).Replace('\\', '/')).ToList();
            Assert.Contains(names, n => n.EndsWith("readable.txt"));
            Assert.DoesNotContain(names, n => n.EndsWith("locked.bin"));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
    }
}
