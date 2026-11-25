using Cube.FileSystem.SevenZip;
using Lhamiel.Util;
using System.Text;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// ロック中ファイルを含む圧縮処理に対する嫌がらせテスト（CopyLockedFilesAsync 系）
/// プロセスがファイルをロックしたまま圧縮できるか、かつ一時ファイルが残らないかを検証する。
/// 「ロック入力」の同値クラスを代表させるため、1MB ラウンドトリップ・書き込み中・事前キャンセル・
/// 通常ファイルとの混在・Flat モードの 5 件に絞っている。
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
}
