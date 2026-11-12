using System.Text;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// <see cref="AtomicFileWriter"/>（settings.json / .lhaignore 共通の tmp + flush + Move 書込み）の単体テスト。
/// </summary>
public sealed class AtomicFileWriterTests
{
    private static async Task WithTempDirAsync(Func<string, Task> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"lhamiel_atomic_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            await body(dir);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    /// <summary>書込み先と同じフォルダーに残った一時ファイル（従来の命名 {dest}.{GUID}.tmp）。</summary>
    private static string[] LeftoverTempFiles(string destination) =>
        Directory.GetFiles(Path.GetDirectoryName(destination)!, Path.GetFileName(destination) + ".*.tmp");

    [Fact]
    public Task WriteAllText_NoBomUtf8_WritesExactBytes_LikeFileWriteAllTextDefault()
    {
        return WithTempDirAsync(dir =>
        {
            var dest = Path.Combine(dir, "settings.json");
            const string content = "{\"Theme\":\"日本語\"}";

            AtomicFileWriter.WriteAllText(dest, content, new UTF8Encoding(false));

            // settings.json の旧実装 File.WriteAllText(path, content) と同じバイト列（BOM なし）。
            var reference = Path.Combine(dir, "reference.json");
            File.WriteAllText(reference, content);
            Assert.Equal(File.ReadAllBytes(reference), File.ReadAllBytes(dest));
            Assert.Empty(LeftoverTempFiles(dest));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task WriteAllText_Utf8WithBom_WritesExactBytes_LikeLhaignoreLegacy()
    {
        return WithTempDirAsync(dir =>
        {
            var dest = Path.Combine(dir, ".lhaignore");
            const string content = "*.tmp\r\n# コメント\r\n";

            AtomicFileWriter.WriteAllText(dest, content, Encoding.UTF8);

            // .lhaignore の旧実装 File.WriteAllText(path, content, Encoding.UTF8) と同じバイト列（BOM あり）。
            var reference = Path.Combine(dir, "reference");
            File.WriteAllText(reference, content, Encoding.UTF8);
            var actual = File.ReadAllBytes(dest);
            Assert.Equal(File.ReadAllBytes(reference), actual);
            Assert.Equal([0xEF, 0xBB, 0xBF], actual[..3]);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task WriteAllText_OverwritesExistingFile()
    {
        return WithTempDirAsync(dir =>
        {
            var dest = Path.Combine(dir, "settings.json");
            File.WriteAllText(dest, "old-content-that-is-longer-than-new");

            AtomicFileWriter.WriteAllText(dest, "new", new UTF8Encoding(false));

            Assert.Equal("new", File.ReadAllText(dest));
            Assert.Empty(LeftoverTempFiles(dest));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task WriteAllText_WhenMoveFails_RemovesTempFileAndKeepsDestination()
    {
        return WithTempDirAsync(dir =>
        {
            // 書込み先が同名フォルダーだと Move（上書き）が失敗する。リトライ上限後に例外が伝わり、
            // 一時ファイルは後始末され、既存のフォルダーはそのまま残る。
            var dest = Path.Combine(dir, "settings.json");
            Directory.CreateDirectory(dest);

            Assert.ThrowsAny<Exception>(() => AtomicFileWriter.WriteAllText(dest, "x", new UTF8Encoding(false)));

            Assert.True(Directory.Exists(dest));
            Assert.Empty(LeftoverTempFiles(dest));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void WriteAllText_WhenDirectoryMissing_ThrowsWithoutLeavingFiles()
    {
        var dest = Path.Combine(Path.GetTempPath(), $"lhamiel_missing_{Guid.NewGuid():N}", "settings.json");

        Assert.Throws<DirectoryNotFoundException>(() => AtomicFileWriter.WriteAllText(dest, "x", new UTF8Encoding(false)));
        Assert.False(Directory.Exists(Path.GetDirectoryName(dest)));
    }

    [Fact]
    public Task WriteAllText_WhenDestinationTemporarilyLocked_RetriesAndSucceeds()
    {
        // .lhaignore にも settings.json と同じ LockedFileRetryPolicy が掛かることの確認:
        // AV / 外部エディタが一瞬ファイルを掴んでいても、解放後の再試行で書き込める。
        return WithTempDirAsync(async dir =>
        {
            var dest = Path.Combine(dir, ".lhaignore");
            File.WriteAllText(dest, "old");
            var holder = new FileStream(dest, FileMode.Open, FileAccess.Read, FileShare.None);
            var release = Task.Run(async () =>
            {
                await Task.Delay(150, TestContext.Current.CancellationToken);
                await holder.DisposeAsync();
            }, TestContext.Current.CancellationToken);
            try
            {
                AtomicFileWriter.WriteAllText(dest, "new", Encoding.UTF8);
            }
            finally
            {
                await release;
                await holder.DisposeAsync();
            }

            Assert.Equal("new", File.ReadAllText(dest));
            Assert.Empty(LeftoverTempFiles(dest));
        });
    }

    // === TryCreateNew（.lhaignore の原子的な初回作成） ===

    [Fact]
    public Task TryCreateNew_WhenMissing_CreatesBomUtf8_LikeFileWriteAllTextUtf8()
    {
        return WithTempDirAsync(dir =>
        {
            var dest = Path.Combine(dir, ".lhaignore");
            const string content = "# 日本語コメント\r\nnode_modules/\r\n";

            Assert.True(AtomicFileWriter.TryCreateNew(dest, content, Encoding.UTF8));

            // 旧実装 File.WriteAllText(path, content, Encoding.UTF8) と同じバイト列（BOM あり）。
            var reference = Path.Combine(dir, "reference.txt");
            File.WriteAllText(reference, content, Encoding.UTF8);
            Assert.Equal(File.ReadAllBytes(reference), File.ReadAllBytes(dest));
            Assert.Empty(LeftoverTempFiles(dest));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task TryCreateNew_WhenDestinationExists_ReturnsFalseWithoutOverwriting()
    {
        // 存在確認から作成までの間に別プロセス・外部エディタが先に作った「作成競合に負けた」状態。
        // 相手の内容を上書きせず、自分の一時ファイルも残さない。
        return WithTempDirAsync(dir =>
        {
            var dest = Path.Combine(dir, ".lhaignore");
            File.WriteAllText(dest, "user-content");

            Assert.False(AtomicFileWriter.TryCreateNew(dest, "default-content", Encoding.UTF8));

            Assert.Equal("user-content", File.ReadAllText(dest));
            Assert.Empty(LeftoverTempFiles(dest));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task TryCreateNew_WhenExistingDestinationIsLocked_ReturnsFalseWithoutOverwriting()
    {
        // 既存ファイルは置き換え対象ではないので、ロック中でも再試行せず「作成済み」として false を返す。
        return WithTempDirAsync(dir =>
        {
            var dest = Path.Combine(dir, ".lhaignore");
            File.WriteAllText(dest, "user-content");
            using (new FileStream(dest, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.False(AtomicFileWriter.TryCreateNew(dest, "default-content", Encoding.UTF8));
            }

            Assert.Equal("user-content", File.ReadAllText(dest));
            Assert.Empty(LeftoverTempFiles(dest));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task TryCreateNew_WhenDestinationIsDirectory_ThrowsAndRemovesTemp()
    {
        // 既存「ファイル」ではない失敗（同名ディレクトリ）は競合負けではなく、例外として呼び出し側へ返す。
        return WithTempDirAsync(dir =>
        {
            var dest = Path.Combine(dir, ".lhaignore");
            Directory.CreateDirectory(dest);

            var ex = Record.Exception(() => AtomicFileWriter.TryCreateNew(dest, "x", Encoding.UTF8));

            Assert.True(ex is IOException or UnauthorizedAccessException, $"unexpected: {ex?.GetType()}");
            Assert.True(Directory.Exists(dest));
            Assert.Empty(LeftoverTempFiles(dest));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void TryCreateNew_WhenDirectoryMissing_ThrowsWithoutLeavingFiles()
    {
        var dest = Path.Combine(Path.GetTempPath(), $"lhamiel_missing_{Guid.NewGuid():N}", ".lhaignore");

        Assert.Throws<DirectoryNotFoundException>(() => AtomicFileWriter.TryCreateNew(dest, "x", Encoding.UTF8));
        Assert.False(Directory.Exists(Path.GetDirectoryName(dest)));
    }
}
