using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// 一時フォルダ方式の最終配置（<c>ArchiveExtractor.MoveExtractedFilesAsync</c>）が
/// DESIGN.md の不変条件「Existing outputs use backup/restore semantics」を守ることの回帰テスト。
/// <para>
/// この経路は既存衝突がある GUI 展開のすべてが通る。以前は既存ファイルを
/// <c>File.Move(overwrite: true)</c> で即上書きし、宛先ファイル・ディレクトリ衝突は
/// <c>File.Delete</c> / <c>Directory.Delete(recursive)</c> で消していたため、途中で
/// 失敗すると上書き済みの原本を復元できなかった（実質データ損失）。
/// </para>
/// </summary>
[Collection("Sequential")]
public class ExtractionOverwriteAtomicityTests
{
    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string[] Backups(string dir)
        => Directory.GetFileSystemEntries(dir, "*.Lhamiel_backup_*", SearchOption.AllDirectories);

    [Fact]
    public async Task 移動成功時_既存は置き換わりバックアップが残らない()
    {
        using var temp = TestDirectory.Create("MoveAtomicOk");
        var src = Path.Combine(temp.Path, "src");
        var dst = Path.Combine(temp.Path, "dst");
        Write(Path.Combine(src, "a.txt"), "new-a");
        Write(Path.Combine(src, "sub", "b.txt"), "new-b");
        Write(Path.Combine(dst, "a.txt"), "orig-a");
        Write(Path.Combine(dst, "sub", "b.txt"), "orig-b");

        await ArchiveExtractor.MoveExtractedFilesAsync(src, dst, null, CancellationToken.None);

        Assert.Equal("new-a", File.ReadAllText(Path.Combine(dst, "a.txt")));
        Assert.Equal("new-b", File.ReadAllText(Path.Combine(dst, "sub", "b.txt")));
        Assert.Empty(Backups(dst));
    }

    [Fact]
    public async Task 移動途中で失敗_先に上書きした原本が復元される()
    {
        using var temp = TestDirectory.Create("MoveAtomicFail");
        var src = Path.Combine(temp.Path, "src");
        var dst = Path.Combine(temp.Path, "dst");
        // 列挙順で 01 が先、99 が後になるよう命名する
        Write(Path.Combine(src, "01_first.txt"), "new-first");
        Write(Path.Combine(src, "99_locked.txt"), "new-locked");
        Write(Path.Combine(dst, "01_first.txt"), "orig-first");
        Write(Path.Combine(dst, "99_locked.txt"), "orig-locked");

        // 後続ファイルの宛先を共有不可で開き、退避（File.Move）を失敗させる
        using (var _ = new FileStream(
            Path.Combine(dst, "99_locked.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<Exception>(
                () => ArchiveExtractor.MoveExtractedFilesAsync(src, dst, null, CancellationToken.None));
        }

        // 先に処理された 01_first.txt の原本が戻っていること（ここが以前は失われていた）
        Assert.Equal("orig-first", File.ReadAllText(Path.Combine(dst, "01_first.txt")));
        Assert.Equal("orig-locked", File.ReadAllText(Path.Combine(dst, "99_locked.txt")));
        Assert.Empty(Backups(dst));
    }

    [Fact]
    public async Task スキップ指定した既存ファイルは退避されず残る()
    {
        using var temp = TestDirectory.Create("MoveAtomicSkip");
        var src = Path.Combine(temp.Path, "src");
        var dst = Path.Combine(temp.Path, "dst");
        Write(Path.Combine(src, "keep.txt"), "new-keep");
        Write(Path.Combine(src, "replace.txt"), "new-replace");
        Write(Path.Combine(dst, "keep.txt"), "orig-keep");
        Write(Path.Combine(dst, "replace.txt"), "orig-replace");

        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "keep.txt" };
        await ArchiveExtractor.MoveExtractedFilesAsync(src, dst, skip, CancellationToken.None);

        // 宛先ディレクトリ全体を退避する実装だと keep.txt が失われる
        Assert.Equal("orig-keep", File.ReadAllText(Path.Combine(dst, "keep.txt")));
        Assert.Equal("new-replace", File.ReadAllText(Path.Combine(dst, "replace.txt")));
        Assert.Empty(Backups(dst));
    }

    [Fact]
    public async Task 読み取り専用の既存ファイルも退避して置き換えられる()
    {
        using var temp = TestDirectory.Create("MoveAtomicRo");
        var src = Path.Combine(temp.Path, "src");
        var dst = Path.Combine(temp.Path, "dst");
        Write(Path.Combine(src, "ro.txt"), "new-ro");
        var target = Path.Combine(dst, "ro.txt");
        Write(target, "orig-ro");
        File.SetAttributes(target, FileAttributes.ReadOnly);

        await ArchiveExtractor.MoveExtractedFilesAsync(src, dst, null, CancellationToken.None);

        Assert.Equal("new-ro", File.ReadAllText(target));
        // ReadOnly のまま退避したバックアップも破棄できていること
        Assert.Empty(Backups(dst));
    }

    [Fact]
    public async Task 宛先ディレクトリをファイルで置き換える衝突_失敗時にディレクトリが復元される()
    {
        using var temp = TestDirectory.Create("MoveAtomicDirClash");
        var src = Path.Combine(temp.Path, "src");
        var dst = Path.Combine(temp.Path, "dst");
        // 01_clash は「宛先がディレクトリ・ソースがファイル」のパス型衝突（列挙順で先に処理される）
        Write(Path.Combine(src, "01_clash"), "new-clash-as-file");
        Write(Path.Combine(src, "99_locked.txt"), "new-locked");
        Write(Path.Combine(dst, "01_clash", "inner.txt"), "orig-inner");
        Write(Path.Combine(dst, "99_locked.txt"), "orig-locked");

        using (var _ = new FileStream(
            Path.Combine(dst, "99_locked.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<Exception>(
                () => ArchiveExtractor.MoveExtractedFilesAsync(src, dst, null, CancellationToken.None));
        }

        // 以前は Directory.Delete(recursive: true) で消していたため、この木ごと戻らなかった
        Assert.True(Directory.Exists(Path.Combine(dst, "01_clash")));
        Assert.Equal("orig-inner", File.ReadAllText(Path.Combine(dst, "01_clash", "inner.txt")));
        Assert.Equal("orig-locked", File.ReadAllText(Path.Combine(dst, "99_locked.txt")));
        Assert.Empty(Backups(dst));
    }

    [Fact]
    public void 既存対象の退避直後にキャンセル_原本が復元される()
    {
        using var temp = TestDirectory.Create("MoveAtomicCancel");
        var dst = Path.Combine(temp.Path, "dst");
        var target = Path.Combine(dst, "existing.txt");
        Write(target, "original");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ArchiveExtractor.PrepareExistingTargetsForOverwrite([target], dst, cts.Token));

        Assert.Equal("original", File.ReadAllText(target));
        Assert.Empty(Backups(dst));
    }
}
