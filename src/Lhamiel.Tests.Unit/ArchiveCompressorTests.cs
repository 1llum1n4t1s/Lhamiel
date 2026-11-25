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

    // === ParseFormat / WritableFormats ===

    /// <summary>
    /// ParseFormat は未知値を無言で ZIP にするため、書込み可能一覧だけが増えると
    /// 中身が ZIP の .lzh などを作る。一覧への包含と ParseFormat の戻り値を対で確かめる。
    /// </summary>
    [Theory]
    [InlineData("ZIP", Format.Zip)]
    [InlineData("7z", Format.SevenZip)]
    [InlineData("TAR", Format.Tar)]
    [InlineData("LZH", Format.Lzh)]
    [InlineData("lha", Format.Lzh)]
    [InlineData("gz", Format.GZip)]
    [InlineData("bz2", Format.BZip2)]
    [InlineData("xz", Format.XZ)]
    public void ParseFormat_AndWritableFormats_AgreeForEachWritableFormat(string input, Format expected)
    {
        Assert.Contains(input, ArchiveCompressor.WritableFormats);
        Assert.Equal(expected, ArchiveCompressor.ParseFormat(input));
    }

    // === GetCompressedFileName ===

    /// <summary>
    /// ドットファイル名フォルダ（.cursor）は GetFileName がそのまま使われ、".zip" だけの名前にならない
    /// </summary>
    [Fact]
    public void GetCompressedFileName_DotFolder_UsesFullName()
    {
        var result = ArchiveCompressor.GetCompressedFileName(@"C:\project\.cursor", "zip", @"C:\output");
        Assert.Equal(Path.Combine(@"C:\output", ".cursor.zip"), result);
    }

    // === DetectConflicts ===

    /// <summary>
    /// 大文字小文字のみが異なる相対パスは、同じ格納名として衝突扱いになる（OrdinalIgnoreCase）
    /// </summary>
    [Fact]
    public void DetectConflicts_CaseDifferenceOnly_DetectedAsConflict()
    {
        var files = new List<(string fullPath, string relativePath)>
        {
            (@"C:\a\File.TXT", "File.TXT"),
            (@"C:\b\file.txt", "file.txt"),
        };

        var groups = ArchiveCompressor.DetectConflicts(files);
        Assert.Single(groups);
        Assert.Equal(2, groups[0].Entries.Count);
    }

    // === ShouldExcludeFile (gitignore semantics) ===

    [Fact]
    public void ShouldExcludeFile_WithMatchingDirectorySegment_ReturnsTrue()
    {
        // rootDir を指定すれば中間ディレクトリ名 (__MACOSX) のセグメントマッチが効く
        var matcher = GitignoreMatcher.Compile(["__MACOSX"]);
        Assert.True(ArchiveCompressor.ShouldExcludeFile(@"C:\root\__MACOSX\file.txt", matcher, rootDir: @"C:\root"));
    }

    [Fact]
    public void ShouldExcludeFile_GitignoreGlobPattern_MatchesExtension()
    {
        // gitignore 互換: *.log は任意の .log ファイルにマッチ
        var matcher = GitignoreMatcher.Compile(["*.log"]);
        Assert.True(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\debug.log", matcher));
        Assert.False(ArchiveCompressor.ShouldExcludeFile(@"C:\folder\debug.txt", matcher));
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
    public async Task CompressFilesAsync_WhenAbortedWhileSaving_DeletesPartialOutput()
    {
        // writer.Save が出力を開いた後に中断（キャンセル／7z 例外）されると、書きかけの outputPath が
        // 残る不具合の回帰テスト。以前は存在しないドライブへ出力しており、Save に到達する前に失敗して
        // File.Exists が常に false となるため何も検証していなかった。
        // ここでは Save 中に届くバイト進捗でキャンセルし、
        // 実際に Save へ入ったこと（cancelledDuringSave）を確認したうえで出力が残らないことを検証する。
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_test_{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(testRoot, "src");
        try
        {
            Directory.CreateDirectory(sourceDir);
            var payload = new byte[4 * 1024 * 1024];
            new Random(42).NextBytes(payload);
            File.WriteAllBytes(Path.Combine(sourceDir, "a.bin"), payload);
            var outputPath = Path.Combine(testRoot, "out.zip");

            using var cts = new CancellationTokenSource();
            var cancelledDuringSave = false;
            var progress = new SyncRecordingProgress(info =>
            {
                if (cancelledDuringSave) return;
                // バイト進捗（Status が空）は writer.Save の中（outputCreated 設定後）でだけ届く。
                if (!info.IsIndeterminate && info.Status.Length == 0)
                {
                    cancelledDuringSave = true;
                    cts.Cancel();
                }
            });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                ArchiveCompressor.CompressFilesAsync(
                    [sourceDir], outputPath, Cube.FileSystem.SevenZip.Format.Zip,
                    progress: progress, cancellationToken: cts.Token));

            Assert.True(cancelledDuringSave, "Save 中の進捗に到達せずキャンセルが発火していない（テストが何も検証していない）");
            Assert.False(File.Exists(outputPath), $"部分ファイルが残っている: {outputPath}");
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }
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
