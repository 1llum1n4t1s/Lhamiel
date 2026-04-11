using Cube.FileSystem.SevenZip;
using Lhamiel.Util;
using System.Text;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// ロック中ファイルを含む圧縮処理に対する嫌がらせテスト（CopyLockedFilesAsync 系）
/// プロセスがファイルをロックしたまま圧縮できるか、かつ一時ファイルが残らないかを検証する
/// </summary>
[Collection("Sequential")]
public class LockedFileCompressionAdversarialTests
{
    // === ヘルパー ===

    private static string CreateTempDir(string prefix = "LockedFileAdversarialTest")
    {
        var dir = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task WithTempDir(Func<string, Task> action)
    {
        var dir = CreateTempDir();
        try { await action(dir); }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>テスト前後で SevenZip_* 一時ディレクトリが増えていないことを確認</summary>
    private static string[] SnapshotLhamielTempDirs() =>
        Directory.GetDirectories(Path.GetTempPath(), "SevenZip_*");

    private static void AssertNoNewLhamielTempDirs(string[] snapshot)
    {
        var current = Directory.GetDirectories(Path.GetTempPath(), "SevenZip_*");
        var leaked = current.Except(snapshot).ToArray();
        Assert.True(leaked.Length == 0,
            $"一時ディレクトリが {leaked.Length} 件残留している:\n{string.Join("\n", leaked)}");
    }

    // ==============================
    // 🗡️ 境界値・極端入力
    // ==============================

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 0バイトのロック中ファイルを圧縮してもクラッシュしない（空ファイルのコピーは特殊ケース）
    /// </summary>
    [Fact]
    public async Task LockedFile_ZeroByte_CanBeCompressedWithoutCrash()
    {
        var snapshot = SnapshotLhamielTempDirs();
        await WithTempDir(async dir =>
        {
            var srcFile = Path.Combine(dir, "empty_locked.txt");
            File.WriteAllBytes(srcFile, []);
            var archivePath = Path.Combine(dir, "out.zip");

            await using var lockStream = new FileStream(srcFile, FileMode.Open, FileAccess.Write, FileShare.Read);

            await ArchiveCompressor.CompressFilesAsync([srcFile], archivePath, Format.Zip,
                null, TestContext.Current.CancellationToken);

            Assert.True(File.Exists(archivePath));
            using var reader = new ArchiveReader(archivePath);
            Assert.Contains(reader.Items, i => !i.IsDirectory && i.FullName.Contains("empty_locked.txt"));
        });
        AssertNoNewLhamielTempDirs(snapshot);
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 1MB のロック中ファイルの内容がラウンドトリップで一致する（大サイズコピー時のデータ損失なし）
    /// </summary>
    [Fact]
    public async Task LockedFile_1MB_ContentPreservedAfterRoundTrip()
    {
        var snapshot = SnapshotLhamielTempDirs();
        await WithTempDir(async dir =>
        {
            var content = new byte[1024 * 1024];
            new Random(42).NextBytes(content);  // シード固定で再現可能
            var srcFile = Path.Combine(dir, "large_locked.bin");
            File.WriteAllBytes(srcFile, content);
            var archivePath = Path.Combine(dir, "out.zip");
            var extractDir = Path.Combine(dir, "extracted");
            Directory.CreateDirectory(extractDir);

            // ファイルを排他ロック中に圧縮
            var ls = new FileStream(srcFile, FileMode.Open, FileAccess.Write, FileShare.Read);
            try
            {
                await ArchiveCompressor.CompressFilesAsync([srcFile], archivePath, Format.Zip,
                    null, TestContext.Current.CancellationToken);
            }
            finally
            {
                await ls.DisposeAsync();
            }

            // 展開して内容検証
            await ArchiveExtractor.ExtractArchive(archivePath, extractDir, null, null, false,
                TestContext.Current.CancellationToken);
            var extractedFile = Directory.GetFiles(extractDir, "*.bin", SearchOption.AllDirectories).First();
            Assert.Equal(content, File.ReadAllBytes(extractedFile));
        });
        AssertNoNewLhamielTempDirs(snapshot);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 日本語ファイル名のロック中ファイルが圧縮できる（Unicode パス + ロック処理の組み合わせ）
    /// </summary>
    [Fact]
    public async Task LockedFile_JapaneseFilename_CompressedWithCorrectName()
    {
        var snapshot = SnapshotLhamielTempDirs();
        await WithTempDir(async dir =>
        {
            var srcFile = Path.Combine(dir, "ロック中の日本語ファイル.txt");
            File.WriteAllText(srcFile, "日本語コンテンツ", Encoding.UTF8);
            var archivePath = Path.Combine(dir, "out.zip");

            await using var lockStream = new FileStream(srcFile, FileMode.Open, FileAccess.Write, FileShare.Read);

            await ArchiveCompressor.CompressFilesAsync([srcFile], archivePath, Format.Zip,
                null, TestContext.Current.CancellationToken);

            Assert.True(File.Exists(archivePath));
            using var reader = new ArchiveReader(archivePath);
            Assert.Contains(reader.Items, i => !i.IsDirectory && i.FullName.Contains("ロック中の日本語ファイル.txt"));
        });
        AssertNoNewLhamielTempDirs(snapshot);
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 50個全てがロック中のファイルを含むディレクトリを圧縮 → 全ファイルがアーカイブに含まれる
    /// </summary>
    [Fact]
    public async Task LockedFile_50AllLocked_AllFilesIncludedInArchive()
    {
        var snapshot = SnapshotLhamielTempDirs();
        await WithTempDir(async dir =>
        {
            var srcDir = Path.Combine(dir, "many_locked");
            Directory.CreateDirectory(srcDir);
            var lockStreams = new List<FileStream>();
            try
            {
                for (var i = 0; i < 50; i++)
                {
                    var file = Path.Combine(srcDir, $"locked_{i:D3}.txt");
                    File.WriteAllText(file, $"content_{i}");
                    lockStreams.Add(new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.Read));
                }

                var archivePath = Path.Combine(dir, "out.zip");
                await ArchiveCompressor.CompressFilesAsync([srcDir], archivePath, Format.Zip,
                    null, TestContext.Current.CancellationToken);

                Assert.True(File.Exists(archivePath));
                using var reader = new ArchiveReader(archivePath);
                Assert.Equal(50, reader.Items.Count(i => !i.IsDirectory));
            }
            finally
            {
                foreach (var s in lockStreams) await s.DisposeAsync();
            }
        });
        AssertNoNewLhamielTempDirs(snapshot);
    }

    // ==============================
    // ⚡ 並行性・レースコンディション
    // ==============================

    /// <summary>
    /// @adversarial @category concurrency @severity high
    /// ロック中ファイルを含む圧縮を3件並行実行 → 一時ディレクトリが互いに干渉しない
    /// </summary>
    [Fact]
    public async Task LockedFile_ThreeParallelCompressions_TempDirsDoNotCrossContaminate()
    {
        var snapshot = SnapshotLhamielTempDirs();
        await WithTempDir(async dir =>
        {
            var lockStreams = new List<FileStream>();
            try
            {
                var tasks = Enumerable.Range(0, 3).Select(async i =>
                {
                    var subDir = Path.Combine(dir, $"src_{i}");
                    Directory.CreateDirectory(subDir);
                    var file = Path.Combine(subDir, $"file_{i}.txt");
                    File.WriteAllText(file, $"parallel_content_{i}");
                    FileStream ls;
                    lock (lockStreams)
                    {
                        ls = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.Read);
                        lockStreams.Add(ls);
                    }
                    var outPath = Path.Combine(dir, $"out_{i}.zip");
                    await ArchiveCompressor.CompressFilesAsync([file], outPath, Format.Zip,
                        null, TestContext.Current.CancellationToken);
                    return outPath;
                }).ToList();

                var results = await Task.WhenAll(tasks);
                foreach (var path in results)
                    Assert.True(File.Exists(path));
            }
            finally
            {
                foreach (var s in lockStreams) await s.DisposeAsync();
            }
        });
        AssertNoNewLhamielTempDirs(snapshot);
    }

    /// <summary>
    /// @adversarial @category concurrency @severity medium
    /// ロック中ファイルに別スレッドが書き込み続けている間も圧縮できる（書き込み中スナップショット）
    /// </summary>
    [Fact]
    public async Task LockedFile_ActivelyWrittenFile_CanBeCompressed()
    {
        var snapshot = SnapshotLhamielTempDirs();
        var dir = CreateTempDir();
        try
        {
            var srcFile = Path.Combine(dir, "active_write.log");
            File.WriteAllText(srcFile, "initial content");
            var archivePath = Path.Combine(dir, "out.zip");

            using var writeCts = new CancellationTokenSource();
            using var fileOpenedSignal = new SemaphoreSlim(0, 1);
#pragma warning disable xUnit1051 // writeTask は writeCts で独立管理するため TestContext.CT は不使用
            var writeTask = Task.Run(async () =>
            {
                try
                {
                    // FileShare.ReadWrite: 他プロセスの読み書きを許可しつつファイルを占有（NLog の典型的なパターン）
                    // ただし 7z.dll の FileShare.Read オープンとは非互換なため、ロック検出の対象になる
                    await using var fs = new FileStream(srcFile, FileMode.Open, FileAccess.ReadWrite,
                        FileShare.ReadWrite);
                    fileOpenedSignal.Release(); // ファイルが開かれたことを通知
                    while (!writeCts.Token.IsCancellationRequested)
                    {
                        fs.Seek(0, SeekOrigin.End);
                        var bytes = Encoding.UTF8.GetBytes($"\nline at {DateTime.Now:HH:mm:ss.fff}");
                        await fs.WriteAsync(bytes, writeCts.Token);
                        await Task.Delay(5, writeCts.Token);
                    }
                }
                catch (OperationCanceledException) { }
            });
#pragma warning restore xUnit1051

            // write タスクがファイルを開くまで待機してから圧縮（競合を防ぐ）
            await fileOpenedSignal.WaitAsync(TestContext.Current.CancellationToken);

            // 書き込み中（= ロック中）に圧縮
            await ArchiveCompressor.CompressFilesAsync([srcFile], archivePath, Format.Zip,
                null, TestContext.Current.CancellationToken);

            // 書き込みスレッドを停止してファイルハンドルを解放してから後処理
            writeCts.Cancel();
            await writeTask;

            // スナップショット時点の内容でアーカイブが作られていること
            Assert.True(File.Exists(archivePath));
            using var reader = new ArchiveReader(archivePath);
            Assert.Contains(reader.Items, i => !i.IsDirectory && i.FullName.Contains("active_write.log"));
        }
        finally
        {
            // ファイルハンドルが解放されるまで少し待ってから削除（OS のハンドル解放タイミング対策）
            for (var i = 0; i < 5; i++)
            {
                try
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                    break;
                }
                catch (IOException)
                {
                    await Task.Delay(100, CancellationToken.None); // クリーンアップ中はテスト CT を使わない
                }
            }
        }
        AssertNoNewLhamielTempDirs(snapshot);
    }

    // ==============================
    // 💀 リソース枯渇
    // ==============================

    /// <summary>
    /// @adversarial @category resource @severity critical
    /// キャンセル済みトークンで呼んだ場合、一時ファイルが残らず出力ファイルも残らない
    /// </summary>
    [Fact]
    public async Task LockedFile_PreCancelledToken_NoTempFilesAndNoOutputLeft()
    {
        var snapshot = SnapshotLhamielTempDirs();
        await WithTempDir(async dir =>
        {
            var srcFile = Path.Combine(dir, "file.txt");
            File.WriteAllText(srcFile, "hello");
            var archivePath = Path.Combine(dir, "out.zip");

            await using var lockStream = new FileStream(srcFile, FileMode.Open, FileAccess.Write, FileShare.Read);

            using var cts = new CancellationTokenSource();
            cts.Cancel();  // 事前キャンセル

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => ArchiveCompressor.CompressFilesAsync([srcFile], archivePath, Format.Zip, null, cts.Token));

            Assert.False(File.Exists(archivePath), "キャンセル後に出力ファイルが残っている");
        });
        AssertNoNewLhamielTempDirs(snapshot);
    }

    /// <summary>
    /// @adversarial @category resource @severity high
    /// 圧縮中途でキャンセルされても一時ファイルが残らない（キャンセル時の finally 保証）
    /// </summary>
    [Fact]
    public async Task LockedFile_CancelledMidway_TempFilesCleanedUp()
    {
        var snapshot = SnapshotLhamielTempDirs();
        await WithTempDir(async dir =>
        {
            var srcDir = Path.Combine(dir, "src");
            Directory.CreateDirectory(srcDir);
            var lockStreams = new List<FileStream>();
            try
            {
                for (var i = 0; i < 30; i++)
                {
                    var file = Path.Combine(srcDir, $"f_{i:D2}.txt");
                    File.WriteAllBytes(file, new byte[4096]); // 4KB × 30 = 120KB
                    lockStreams.Add(new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.Read));
                }

                using var cts = new CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromMilliseconds(30));  // 30ms でキャンセル

                var archivePath = Path.Combine(dir, "out.zip");
                try
                {
                    await ArchiveCompressor.CompressFilesAsync([srcDir], archivePath, Format.Zip, null, cts.Token);
                    // キャンセルが間に合わなかった場合も正常終了（これも OK）
                }
                catch (OperationCanceledException)
                {
                    Assert.False(File.Exists(archivePath), "キャンセル後に出力ファイルが残っている");
                }
            }
            finally
            {
                foreach (var s in lockStreams) await s.DisposeAsync();
            }
        });
        AssertNoNewLhamielTempDirs(snapshot);
    }

    // ==============================
    // 🔀 状態遷移の矛盾
    // ==============================

    /// <summary>
    /// @adversarial @category state @severity high
    /// 圧縮成功後に一時ディレクトリが確実に削除される（正常終了時の finally クリーンアップ）
    /// </summary>
    [Fact]
    public async Task LockedFile_AfterSuccessfulCompression_TempDirIsAlwaysDeleted()
    {
        var snapshot = SnapshotLhamielTempDirs();
        await WithTempDir(async dir =>
        {
            var srcFile = Path.Combine(dir, "file.txt");
            File.WriteAllText(srcFile, "cleanup test content");
            var archivePath = Path.Combine(dir, "out.zip");

            var ls = new FileStream(srcFile, FileMode.Open, FileAccess.Write, FileShare.Read);
            try
            {
                await ArchiveCompressor.CompressFilesAsync([srcFile], archivePath, Format.Zip,
                    null, TestContext.Current.CancellationToken);
            }
            finally
            {
                await ls.DisposeAsync();
            }

            Assert.True(File.Exists(archivePath));
        });
        AssertNoNewLhamielTempDirs(snapshot);
    }

    /// <summary>
    /// @adversarial @category state @severity high
    /// ロック中ファイルのコピー内容がオリジナルと完全一致する（バイト単位のデータ整合性）
    /// </summary>
    [Fact]
    public async Task LockedFile_ContentIntegrity_ByteForByteMatchAfterRoundTrip()
    {
        var snapshot = SnapshotLhamielTempDirs();
        await WithTempDir(async dir =>
        {
            // 制御文字・絵文字・マルチバイト文字を含む混合コンテンツ
            var originalContent = "テスト\nSecond line\r\nTab:\there\nEmoji: 🎌🗾\nNull guard: end";
            var srcFile = Path.Combine(dir, "integrity.txt");
            File.WriteAllText(srcFile, originalContent, Encoding.UTF8);
            var archivePath = Path.Combine(dir, "out.zip");
            var extractDir = Path.Combine(dir, "extracted");
            Directory.CreateDirectory(extractDir);

            var ls = new FileStream(srcFile, FileMode.Open, FileAccess.Write, FileShare.Read);
            try
            {
                await ArchiveCompressor.CompressFilesAsync([srcFile], archivePath, Format.Zip,
                    null, TestContext.Current.CancellationToken);
            }
            finally
            {
                await ls.DisposeAsync();
            }

            await ArchiveExtractor.ExtractArchive(archivePath, extractDir, null, null, false,
                TestContext.Current.CancellationToken);
            var extractedFile = Directory.GetFiles(extractDir, "*.txt", SearchOption.AllDirectories).First();
            Assert.Equal(originalContent, File.ReadAllText(extractedFile, Encoding.UTF8));
        });
        AssertNoNewLhamielTempDirs(snapshot);
    }

    /// <summary>
    /// @adversarial @category state @severity medium
    /// ロック中ファイルと通常ファイルが混在する場合、両方がアーカイブに含まれる
    /// </summary>
    [Fact]
    public async Task LockedFile_MixedWithNormalFiles_BothIncludedInArchive()
    {
        var snapshot = SnapshotLhamielTempDirs();
        await WithTempDir(async dir =>
        {
            var srcDir = Path.Combine(dir, "src");
            Directory.CreateDirectory(srcDir);
            var lockedFile = Path.Combine(srcDir, "locked.txt");
            var normalFile = Path.Combine(srcDir, "normal.txt");
            File.WriteAllText(lockedFile, "locked content");
            File.WriteAllText(normalFile, "normal content");
            var archivePath = Path.Combine(dir, "out.zip");

            await using var lockStream = new FileStream(lockedFile, FileMode.Open, FileAccess.Write, FileShare.Read);

            await ArchiveCompressor.CompressFilesAsync([srcDir], archivePath, Format.Zip,
                null, TestContext.Current.CancellationToken);

            Assert.True(File.Exists(archivePath));
            using var reader = new ArchiveReader(archivePath);
            var fileNames = reader.Items.Where(i => !i.IsDirectory).Select(i => i.FullName).ToList();
            Assert.Contains(fileNames, n => n.Contains("locked.txt"));
            Assert.Contains(fileNames, n => n.Contains("normal.txt"));
        });
        AssertNoNewLhamielTempDirs(snapshot);
    }

    // ==============================
    // 🎭 型パンチ・プロトコル違反
    // ==============================

    /// <summary>
    /// @adversarial @category type @severity medium
    /// 深いサブフォルダ（3階層）内のロック中ファイルが相対パスを保持してアーカイブに含まれる
    /// </summary>
    [Fact]
    public async Task LockedFile_InDeepSubdirectory_RelativePathPreservedInArchive()
    {
        var snapshot = SnapshotLhamielTempDirs();
        await WithTempDir(async dir =>
        {
            var srcDir = Path.Combine(dir, "src");
            var deepDir = Path.Combine(srcDir, "level1", "level2", "level3");
            Directory.CreateDirectory(deepDir);
            var lockedFile = Path.Combine(deepDir, "deep_locked.txt");
            File.WriteAllText(lockedFile, "deeply nested locked content");
            var archivePath = Path.Combine(dir, "out.zip");

            await using var lockStream = new FileStream(lockedFile, FileMode.Open, FileAccess.Write, FileShare.Read);

            await ArchiveCompressor.CompressFilesAsync([srcDir], archivePath, Format.Zip,
                null, TestContext.Current.CancellationToken);

            Assert.True(File.Exists(archivePath));
            using var reader = new ArchiveReader(archivePath);
            var item = reader.Items.FirstOrDefault(i => !i.IsDirectory && i.FullName.Contains("deep_locked.txt"));
            Assert.NotNull(item);
            // 相対パスに全階層が含まれること
            Assert.Contains("level1", item.FullName);
            Assert.Contains("level2", item.FullName);
            Assert.Contains("level3", item.FullName);
        });
        AssertNoNewLhamielTempDirs(snapshot);
    }

    /// <summary>
    /// @adversarial @category type @severity medium
    /// ロック中ファイルを Flat モードで圧縮すると相対パスにスラッシュが含まれない
    /// </summary>
    [Fact]
    public async Task LockedFile_FlatMode_RelativePathHasNoSlash()
    {
        var snapshot = SnapshotLhamielTempDirs();
        SettingsManager.Instance.Current.DirectoryStructureMode = DirectoryStructureMode.Flat;
        try
        {
            await WithTempDir(async dir =>
            {
                var srcDir = Path.Combine(dir, "src");
                var subDir = Path.Combine(srcDir, "sub");
                Directory.CreateDirectory(subDir);
                var lockedFile = Path.Combine(subDir, "flat_locked.txt");
                File.WriteAllText(lockedFile, "flat mode locked content");
                var archivePath = Path.Combine(dir, "out.zip");

                await using var lockStream = new FileStream(lockedFile, FileMode.Open, FileAccess.Write, FileShare.Read);

                await ArchiveCompressor.CompressFilesAsync([srcDir], archivePath, Format.Zip,
                    null, TestContext.Current.CancellationToken);

                Assert.True(File.Exists(archivePath));
                using var reader = new ArchiveReader(archivePath);
                var item = reader.Items.FirstOrDefault(i => !i.IsDirectory && i.FullName.Contains("flat_locked.txt"));
                Assert.NotNull(item);
                // Flat モードではサブフォルダが消えてファイル名だけになる
                Assert.DoesNotContain("/", item.FullName);
                Assert.DoesNotContain("\\", item.FullName);
            });
        }
        finally
        {
            SettingsManager.Instance.Current.DirectoryStructureMode = DirectoryStructureMode.IncludeRoot;
        }
        AssertNoNewLhamielTempDirs(snapshot);
    }

    // ==============================
    // 🌪️ 環境異常・カオス
    // ==============================

    /// <summary>
    /// @adversarial @category chaos @severity high
    /// ロック中ファイルを 7z 形式で圧縮してもラウンドトリップで内容が保持される
    /// </summary>
    [Fact]
    public async Task LockedFile_With7zFormat_RoundTripContentPreserved()
    {
        var snapshot = SnapshotLhamielTempDirs();
        await WithTempDir(async dir =>
        {
            var content = "7z format locked file test content: テスト 🎌";
            var srcFile = Path.Combine(dir, "locked.dat");
            File.WriteAllText(srcFile, content, Encoding.UTF8);
            var archivePath = Path.Combine(dir, "out.7z");
            var extractDir = Path.Combine(dir, "extracted");
            Directory.CreateDirectory(extractDir);

            var ls = new FileStream(srcFile, FileMode.Open, FileAccess.Write, FileShare.Read);
            try
            {
                await ArchiveCompressor.CompressFilesAsync([srcFile], archivePath, Format.SevenZip,
                    null, TestContext.Current.CancellationToken);
            }
            finally
            {
                await ls.DisposeAsync();
            }

            Assert.True(File.Exists(archivePath));
            await ArchiveExtractor.ExtractArchive(archivePath, extractDir, null, null, false,
                TestContext.Current.CancellationToken);
            var extractedFile = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories).First();
            Assert.Equal(content, File.ReadAllText(extractedFile, Encoding.UTF8));
        });
        AssertNoNewLhamielTempDirs(snapshot);
    }

    /// <summary>
    /// @adversarial @category chaos @severity high
    /// ロック中ファイルのコピー完了直後にファイルが削除されても圧縮は成功する
    /// （一時コピーが既にあるため問題ない、というシナリオの検証）
    /// </summary>
    [Fact]
    public async Task LockedFile_DeletedAfterLockReleased_ArchiveAlreadyHasSnapshot()
    {
        var snapshot = SnapshotLhamielTempDirs();
        await WithTempDir(async dir =>
        {
            var srcFile = Path.Combine(dir, "snapshot_test.txt");
            File.WriteAllText(srcFile, "original snapshot content");
            var archivePath = Path.Combine(dir, "out.zip");
            var extractDir = Path.Combine(dir, "extracted");
            Directory.CreateDirectory(extractDir);

            // ロック中に圧縮（一時コピーが作られる）
            var ls = new FileStream(srcFile, FileMode.Open, FileAccess.Write, FileShare.Read);
            try
            {
                await ArchiveCompressor.CompressFilesAsync([srcFile], archivePath, Format.Zip,
                    null, TestContext.Current.CancellationToken);
            }
            finally
            {
                await ls.DisposeAsync();
            }

            // 圧縮後に元ファイルを書き換え
            File.WriteAllText(srcFile, "modified after compression");

            // 展開した内容は圧縮時のスナップショットと一致すること
            await ArchiveExtractor.ExtractArchive(archivePath, extractDir, null, null, false,
                TestContext.Current.CancellationToken);
            var extractedFile = Directory.GetFiles(extractDir, "*.txt", SearchOption.AllDirectories).First();
            Assert.Equal("original snapshot content", File.ReadAllText(extractedFile));
        });
        AssertNoNewLhamielTempDirs(snapshot);
    }

    /// <summary>
    /// @adversarial @category chaos @severity medium
    /// ロックされていないファイルでも一律一時コピー経由で圧縮でき、一時ディレクトリが圧縮後に削除される
    /// </summary>
    [Fact]
    public async Task UnlockedFiles_AlsoCopiedToTemp_TempCleanedUpAfter()
    {
        var snapshot = SnapshotLhamielTempDirs();
        await WithTempDir(async dir =>
        {
            var srcFile = Path.Combine(dir, "normal.txt");
            File.WriteAllText(srcFile, "normal unlocked content");
            var archivePath = Path.Combine(dir, "out.zip");

            await ArchiveCompressor.CompressFilesAsync([srcFile], archivePath, Format.Zip,
                null, TestContext.Current.CancellationToken);

            Assert.True(File.Exists(archivePath));
        });
        // 一律コピーしても圧縮後に一時ディレクトリが削除されること
        AssertNoNewLhamielTempDirs(snapshot);
    }
}
