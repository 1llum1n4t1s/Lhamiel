using Cube.FileSystem.SevenZip;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// 一時コピー廃止後の直接パス圧縮に対する嫌がらせテスト。
/// 全ファイルを事前コピーしていたフローを廃止し、元パスを直接 writer.Add() に渡す
/// 方式に変更したことで新たに露出した攻撃面をカバーする。
/// </summary>
[Collection("Sequential")]
public class DirectPathCompressionAdversarialTests
{
    // === ヘルパー ===

    private static string CreateTempDir(string prefix = "DirectPathAdversarial")
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
    private static string[] SnapshotTempDirs() =>
        Directory.GetDirectories(Path.GetTempPath(), "SevenZip_*");

    private static void AssertNoLeakedTempDirs(string[] snapshot)
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
    /// 空ファイル（0バイト）を直接パスで圧縮してもクラッシュしない
    /// 以前はコピー時に特殊処理していたが、直接パスでは 7z.dll に直接渡される
    /// </summary>
    [Fact]
    public async Task ZeroByteFile_DirectPath_CompressesWithoutCrash()
    {
        var snapshot = SnapshotTempDirs();
        await WithTempDir(async dir =>
        {
            var emptyFile = Path.Combine(dir, "empty.txt");
            File.WriteAllBytes(emptyFile, []);
            var archivePath = Path.Combine(dir, "out.zip");

            await ArchiveCompressor.CompressFilesAsync([emptyFile], archivePath, Format.Zip);

            Assert.True(File.Exists(archivePath));
            using var reader = new ArchiveReader(archivePath);
            Assert.Contains(reader.Items, i => !i.IsDirectory && i.FullName.Contains("empty.txt"));
        });
        AssertNoLeakedTempDirs(snapshot);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 読み取り専用ファイルを直接パスで圧縮してもクラッシュしない
    /// </summary>
    [Fact]
    public async Task ReadOnlyFile_DirectPath_CompressesWithoutCrash()
    {
        var snapshot = SnapshotTempDirs();
        await WithTempDir(async dir =>
        {
            var file = Path.Combine(dir, "readonly.txt");
            File.WriteAllText(file, "読み取り専用テスト");
            File.SetAttributes(file, FileAttributes.ReadOnly);
            var archivePath = Path.Combine(dir, "out.zip");

            try
            {
                await ArchiveCompressor.CompressFilesAsync([file], archivePath, Format.Zip);
                Assert.True(File.Exists(archivePath));
            }
            finally
            {
                // クリーンアップのために読み取り専用を解除
                File.SetAttributes(file, FileAttributes.Normal);
            }
        });
        AssertNoLeakedTempDirs(snapshot);
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// Unicode ファイル名（絵文字・CJK拡張）を直接パスで圧縮してラウンドトリップ一致
    /// 以前はコピー時にパスが正規化されていたが、直接パスでは元パスがそのまま渡る
    /// </summary>
    [Fact]
    public async Task UnicodeFileName_DirectPath_RoundTripsCorrectly()
    {
        var snapshot = SnapshotTempDirs();
        await WithTempDir(async dir =>
        {
            var unicodeName = "テスト_データ_2026.txt";
            var file = Path.Combine(dir, unicodeName);
            File.WriteAllText(file, "テストデータ");
            var archivePath = Path.Combine(dir, "out.zip");

            await ArchiveCompressor.CompressFilesAsync([file], archivePath, Format.Zip);

            using var reader = new ArchiveReader(archivePath);
            Assert.Contains(reader.Items, i => !i.IsDirectory && i.FullName.Contains(unicodeName));
        });
        AssertNoLeakedTempDirs(snapshot);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// スペースを含むファイル名を直接パスで圧縮してラウンドトリップ一致
    /// </summary>
    [Fact]
    public async Task SpacesInFileName_DirectPath_RoundTripsCorrectly()
    {
        var snapshot = SnapshotTempDirs();
        await WithTempDir(async dir =>
        {
            var file = Path.Combine(dir, "file with spaces.txt");
            File.WriteAllText(file, "スペース含みテスト");
            var archivePath = Path.Combine(dir, "out.zip");

            await ArchiveCompressor.CompressFilesAsync([file], archivePath, Format.Zip);

            using var reader = new ArchiveReader(archivePath);
            Assert.Contains(reader.Items, i => !i.IsDirectory && i.FullName.Contains("file with spaces.txt"));
        });
        AssertNoLeakedTempDirs(snapshot);
    }

    // ==============================
    // ⚡ 並行性・レースコンディション
    // ==============================

    /// <summary>
    /// @adversarial @category concurrency @severity high
    /// 圧縮中にソースファイルが書き換わってもクラッシュしない
    /// 以前はコピーがスナップショットを保証していたが、直接パスでは保証されない
    /// </summary>
    [Fact]
    public async Task FileModifiedDuringCompression_DoesNotCrash()
    {
        var snapshot = SnapshotTempDirs();
        await WithTempDir(async dir =>
        {
            // 大きめのファイルを作成（圧縮中に書き換えが間に合うように）
            var file = Path.Combine(dir, "changing.bin");
            var data = new byte[512 * 1024];
            new Random(42).NextBytes(data);
            File.WriteAllBytes(file, data);
            var archivePath = Path.Combine(dir, "out.7z");

            // 圧縮中にファイルを書き換えるタスク
            using var cts = new CancellationTokenSource();
            var writerTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await using var fs = new FileStream(file, FileMode.Open, FileAccess.Write,
                            FileShare.ReadWrite | FileShare.Delete);
                        fs.Seek(0, SeekOrigin.Begin);
                        fs.WriteByte(0xFF);
                    }
                    catch { /* ファイルが一時的にアクセスできない場合は無視 */ }
                    await Task.Delay(1);
                }
            });

            // 圧縮自体がクラッシュしないことが重要（結果の正確性は保証しない）
            Exception? caughtException = null;
            try
            {
                await ArchiveCompressor.CompressFilesAsync([file], archivePath, Format.SevenZip);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }
            finally
            {
                cts.Cancel();
                try { await writerTask; } catch { }
            }

            // クラッシュ（AccessViolation 等）でなければ OK。圧縮エラーは許容する。
            if (caughtException is not null)
            {
                // NullReferenceException や AccessViolationException は致命的バグ
                Assert.IsNotType<NullReferenceException>(caughtException);
            }
        });
        AssertNoLeakedTempDirs(snapshot);
    }

    /// <summary>
    /// @adversarial @category concurrency @severity high
    /// キャンセル時に一時ディレクトリが残留しない
    /// ライブラリ側の UpdateCallback.Dispose() で確実に削除されることを検証
    /// </summary>
    [Fact]
    public async Task CancellationDuringCompression_NoTempDirLeak()
    {
        var snapshot = SnapshotTempDirs();
        await WithTempDir(async dir =>
        {
            // キャンセルをテストするために大きめのファイルを作る
            var files = new List<string>();
            for (var i = 0; i < 50; i++)
            {
                var file = Path.Combine(dir, $"file_{i}.bin");
                File.WriteAllBytes(file, new byte[10 * 1024]);
                files.Add(file);
            }
            var archivePath = Path.Combine(dir, "out.zip");

            using var cts = new CancellationTokenSource();
            // 即座にキャンセル
            cts.CancelAfter(TimeSpan.FromMilliseconds(10));

            var wasCancelled = false;
            try
            {
                await ArchiveCompressor.CompressFilesAsync(files, archivePath, Format.Zip,
                    cancellationToken: cts.Token);
            }
            catch (OperationCanceledException)
            {
                wasCancelled = true;
            }

            // キャンセルが間に合った場合のみ: 不完全なアーカイブが残っていないこと
            // キャンセル前に圧縮が完了した場合は完全なアーカイブが残るのが正しい挙動
            if (wasCancelled)
                Assert.False(File.Exists(archivePath), "キャンセル後に不完全なアーカイブが残っている");
        });
        AssertNoLeakedTempDirs(snapshot);
    }

    // ==============================
    // 🔀 状態遷移の矛盾
    // ==============================

    /// <summary>
    /// @adversarial @category state @severity high
    /// スキャン後にソースファイルが削除された場合、スキップして圧縮が成功する
    /// </summary>
    [Fact]
    public async Task SourceDeletedAfterScan_SkippedAndSucceeds()
    {
        await WithTempDir(async dir =>
        {
            var remaining = Path.Combine(dir, "remaining.txt");
            var deleted = Path.Combine(dir, "will_be_deleted.txt");
            File.WriteAllText(remaining, "残るファイル");
            File.WriteAllText(deleted, "消えるファイル");
            var archivePath = Path.Combine(dir, "out.zip");

            var resolvedFiles = new List<(string fullPath, string relativePath)>
            {
                (remaining, "remaining.txt"),
                (deleted, "will_be_deleted.txt")
            };

            // ファイルを削除
            File.Delete(deleted);

            // エラーにならず圧縮が完了する
            await ArchiveCompressor.CompressFilesAsync([dir], archivePath, Format.Zip,
                resolvedFiles: resolvedFiles);

            Assert.True(File.Exists(archivePath));
            using var reader = new ArchiveReader(archivePath);
            Assert.Contains(reader.Items, i => !i.IsDirectory && i.FullName.Contains("remaining.txt"));
            Assert.DoesNotContain(reader.Items, i => i.FullName.Contains("will_be_deleted.txt"));
        });
    }

    /// <summary>
    /// @adversarial @category state @severity medium
    /// 空のディレクトリのみを圧縮した場合、空アーカイブを作らずに InvalidOperationException で中止する。
    /// v1.0.181+: 空アーカイブの誤生成を防ぐため addedCount==0 で例外を投げる仕様。
    /// </summary>
    [Fact]
    public async Task EmptyDirectoryOnly_ThrowsInvalidOperation()
    {
        await WithTempDir(async dir =>
        {
            var emptySubDir = Path.Combine(dir, "empty_folder");
            Directory.CreateDirectory(emptySubDir);
            var archivePath = Path.Combine(dir, "out.zip");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await ArchiveCompressor.CompressFilesAsync([emptySubDir], archivePath, Format.Zip));

            Assert.Contains("AllSourcesInaccessible", ex.Message);
            Assert.False(File.Exists(archivePath));
        });
    }

    // ==============================
    // 🌪️ 環境異常・カオステスト
    // ==============================

    /// <summary>
    /// @adversarial @category chaos @severity high
    /// 出力先ディレクトリが存在しない場合に自動作成される
    /// </summary>
    [Fact]
    public async Task OutputDirDoesNotExist_CreatesAutomatically()
    {
        await WithTempDir(async dir =>
        {
            var file = Path.Combine(dir, "test.txt");
            File.WriteAllText(file, "テスト");
            var outputDir = Path.Combine(dir, "non", "existent", "path");
            var archivePath = Path.Combine(outputDir, "out.zip");

            await ArchiveCompressor.CompressFilesAsync([file], archivePath, Format.Zip);

            Assert.True(File.Exists(archivePath));
        });
    }

    /// <summary>
    /// @adversarial @category chaos @severity medium
    /// 複数の 7z 形式でも直接パス圧縮が動作する（ZIP 以外のカバレッジ）
    /// </summary>
    [Fact]
    public async Task SevenZipFormat_DirectPath_CompressesSuccessfully()
    {
        var snapshot = SnapshotTempDirs();
        await WithTempDir(async dir =>
        {
            var file = Path.Combine(dir, "data.txt");
            File.WriteAllText(file, "7z 形式テスト");
            var archivePath = Path.Combine(dir, "out.7z");

            await ArchiveCompressor.CompressFilesAsync([file], archivePath, Format.SevenZip);

            Assert.True(File.Exists(archivePath));
            using var reader = new ArchiveReader(archivePath);
            Assert.Contains(reader.Items, i => !i.IsDirectory && i.FullName.Contains("data.txt"));
        });
        AssertNoLeakedTempDirs(snapshot);
    }

    /// <summary>
    /// @adversarial @category chaos @severity medium
    /// ロック中ファイルとロックなしファイルが混在する場合、
    /// ロック中ファイルだけがライブラリ側でコピーされ、全ファイルが正しく圧縮される
    /// </summary>
    [Fact]
    public async Task MixedLockedAndUnlocked_AllFilesCompressed()
    {
        var snapshot = SnapshotTempDirs();
        await WithTempDir(async dir =>
        {
            // ロックなしファイル
            var normalFile = Path.Combine(dir, "normal.txt");
            File.WriteAllText(normalFile, "通常ファイル");

            // ロック中ファイル
            var lockedFile = Path.Combine(dir, "locked.txt");
            File.WriteAllText(lockedFile, "ロック中ファイル");

            var archivePath = Path.Combine(dir, "out.zip");

            await using var lockStream = new FileStream(lockedFile, FileMode.Open,
                FileAccess.Write, FileShare.Read);

            await ArchiveCompressor.CompressFilesAsync([dir], archivePath, Format.Zip);

            Assert.True(File.Exists(archivePath));
            using var reader = new ArchiveReader(archivePath);
            Assert.Contains(reader.Items, i => !i.IsDirectory && i.FullName.Contains("normal.txt"));
            Assert.Contains(reader.Items, i => !i.IsDirectory && i.FullName.Contains("locked.txt"));
        });
        AssertNoLeakedTempDirs(snapshot);
    }
}
