using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

public class ArchiveOverwriteInfoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lhamiel-overwrite-" + Guid.NewGuid().ToString("N"));

    public ArchiveOverwriteInfoTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void ScanTotal_CountsNestedAndHiddenFilesAndSingleFiles()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, "d", "sub")).Parent!.FullName;
        File.WriteAllBytes(Path.Combine(dir, "a.bin"), new byte[10]);
        var hidden = Path.Combine(dir, "sub", "h.bin");
        File.WriteAllBytes(hidden, new byte[5]);
        File.SetAttributes(hidden, FileAttributes.Hidden);
        var single = Path.Combine(_root, "s.bin");
        File.WriteAllBytes(single, new byte[7]);

        var (bytes, files) = ArchiveOverwriteInfo.ScanTotal([dir, single], CancellationToken.None);

        Assert.Equal(22, bytes);
        Assert.Equal(3, files);
    }

    [Fact]
    public void ScanTotal_DoesNotFollowDirectoryJunctions()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        File.WriteAllBytes(Path.Combine(target, "t.bin"), new byte[100]);
        var dir = Directory.CreateDirectory(Path.Combine(_root, "src")).FullName;
        File.WriteAllBytes(Path.Combine(dir, "a.bin"), new byte[1]);
        // ジャンクションは権限不要で作れる。
        var link = Path.Combine(dir, "link");
        DirectoryJunction.Create(link, target);

        var (bytes, files) = ArchiveOverwriteInfo.ScanTotal([dir], CancellationToken.None);

        Assert.Equal(1, bytes);
        Assert.Equal(1, files);
    }

    [Fact]
    public void ScanTotal_MissingPathIsSkippedAndCancellationThrows()
    {
        var (bytes, files) = ArchiveOverwriteInfo.ScanTotal([Path.Combine(_root, "none")], CancellationToken.None);
        Assert.Equal((0, 0), (bytes, files));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => ArchiveOverwriteInfo.ScanTotal([_root], cts.Token));
    }
}
