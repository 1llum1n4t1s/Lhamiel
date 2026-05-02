using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

public class ArchiveIntegrityVerifierTests
{
    [Fact]
    public async Task VerifyArchiveAsync_NonExistentFile_ReturnsInvalid()
    {
        var result = await ArchiveIntegrityVerifier.VerifyArchiveAsync(
            @"C:\non_existent_archive_99999.zip",
            TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task VerifyArchiveAsync_ValidZipFile_ReturnsValid()
    {
        // 有効な ZIP ファイルを作成して検証
        var testDir = Path.Combine(Path.GetTempPath(), $"lhamiel_verify_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        var sourceFile = Path.Combine(testDir, "test.txt");
        File.WriteAllText(sourceFile, "Hello, integrity verification!");

        var zipPath = Path.Combine(testDir, "test.zip");

        try
        {
            // ArchiveCompressor を使って正常な ZIP を作成
            var format = ArchiveCompressor.ParseFormat("zip");
            await ArchiveCompressor.CompressFilesAsync(
                [sourceFile], zipPath, format,
                new Progress<ProgressInfo>(),
                TestContext.Current.CancellationToken);

            Assert.True(File.Exists(zipPath), "ZIP ファイルが作成されていない");

            var result = await ArchiveIntegrityVerifier.VerifyArchiveAsync(
                zipPath, TestContext.Current.CancellationToken);

            Assert.True(result.IsValid, $"正常な ZIP が検証失敗: {result.ErrorMessage}");
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public async Task VerifyArchiveAsync_CorruptedFile_ReturnsInvalid()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"lhamiel_verify_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            // まず正常な ZIP を作成
            var sourceFile = Path.Combine(testDir, "data.txt");
            File.WriteAllText(sourceFile, new string('A', 1024));

            var zipPath = Path.Combine(testDir, "valid.zip");
            var format = ArchiveCompressor.ParseFormat("zip");
            await ArchiveCompressor.CompressFilesAsync(
                [sourceFile], zipPath, format,
                new Progress<ProgressInfo>(),
                TestContext.Current.CancellationToken);

            // ZIP のデータ部分を破壊（先頭のヘッダは残し、データ領域を上書き）
            var bytes = File.ReadAllBytes(zipPath);
            if (bytes.Length > 50)
            {
                var random = new Random(42);
                random.NextBytes(bytes.AsSpan(30, Math.Min(100, bytes.Length - 30)));
                File.WriteAllBytes(zipPath, bytes);
            }

            var result = await ArchiveIntegrityVerifier.VerifyArchiveAsync(
                zipPath, TestContext.Current.CancellationToken);

            Assert.False(result.IsValid, "破損 ZIP が検証成功してしまった");
        }
        finally
        {
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public async Task VerifyArchiveAsync_Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ArchiveIntegrityVerifier.VerifyArchiveAsync(@"C:\dummy.zip", cts.Token));
    }
}
