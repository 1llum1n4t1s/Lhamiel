using Cube.FileSystem.SevenZip;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// <see cref="ArchiveCompressor.CompressFilesAsync"/> のパスワード保護経路に対する統合テスト。
/// 実 7z.dll (1llum1n4t1s.Sevenzip) を経由するので Windows 限定。
/// <see cref="NativeArchiveGate"/> で直列化されているため <c>[Collection("Sequential")]</c> を付与。
/// </summary>
[Collection("Sequential")]
public class ArchiveCompressorPasswordTests : IDisposable
{
    private readonly string _testDir;

    public ArchiveCompressorPasswordTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "ArchiveCompressorPasswordTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, recursive: true); }
        catch { /* テスト後始末の失敗は無視 */ }
    }

    private string CreateSourceFile(string name = "secret.txt", string content = "Top secret content 0xDEADBEEF")
    {
        var path = Path.Combine(_testDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task TarFormat_WithPassword_Throws()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "7z.dll 経路は Windows 限定");
        var src = CreateSourceFile();
        var archive = Path.Combine(_testDir, "out.tar");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ArchiveCompressor.CompressFilesAsync(
                [src], archive, Format.Tar,
                cancellationToken: TestContext.Current.CancellationToken,
                password: "any-password"));
    }

    [Fact]
    public async Task ZipFormat_WithPassword_CreatesNonEmptyArchive()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "7z.dll 経路は Windows 限定");
        var src = CreateSourceFile();
        var archive = Path.Combine(_testDir, "encrypted.zip");

        await ArchiveCompressor.CompressFilesAsync(
            [src], archive, Format.Zip,
            cancellationToken: TestContext.Current.CancellationToken,
            password: "mypassword");

        Assert.True(File.Exists(archive));
        var size = new FileInfo(archive).Length;
        Assert.True(size > 50, $"暗号化 ZIP のサイズが想定より小さい: {size} bytes");
    }

    [Fact]
    public async Task SevenZipFormat_WithPasswordAndHeaderEncryption_CreatesArchive()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "7z.dll 経路は Windows 限定");
        var src = CreateSourceFile();
        var archive = Path.Combine(_testDir, "encrypted.7z");

        await ArchiveCompressor.CompressFilesAsync(
            [src], archive, Format.SevenZip,
            cancellationToken: TestContext.Current.CancellationToken,
            password: "mypassword", encryptFileNames: true);

        Assert.True(File.Exists(archive));
        // 7z ヘッダ暗号化が掛かっていると、メタデータがランダム化されてエントロピが高くなる。
        // ここでは「ファイルが存在し、サイズが妥当」の最低限の検証にとどめる。
        var size = new FileInfo(archive).Length;
        Assert.True(size > 50, $"暗号化 7z のサイズが想定より小さい: {size} bytes");
    }

    [Fact]
    public async Task SevenZipFormat_WithoutPassword_CreatesArchive()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "7z.dll 経路は Windows 限定");
        var src = CreateSourceFile();
        var archive = Path.Combine(_testDir, "plain.7z");

        await ArchiveCompressor.CompressFilesAsync(
            [src], archive, Format.SevenZip,
            cancellationToken: TestContext.Current.CancellationToken,
            password: null);

        Assert.True(File.Exists(archive));
        Assert.True(new FileInfo(archive).Length > 0);
    }

    [Fact]
    public async Task EmptySourcePaths_Throws()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "7z.dll 経路は Windows 限定");
        var archive = Path.Combine(_testDir, "out.zip");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            ArchiveCompressor.CompressFilesAsync(
                [], archive, Format.Zip,
                cancellationToken: TestContext.Current.CancellationToken,
                password: "any"));
    }
}
