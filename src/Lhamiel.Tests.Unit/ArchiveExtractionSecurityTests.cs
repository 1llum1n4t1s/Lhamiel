using System.Diagnostics;
using System.IO.Compression;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

[Collection("Sequential")]
public sealed class ArchiveExtractionSecurityTests
{
    [Theory]
    [InlineData("CON")]
    [InlineData("NUL.txt")]
    [InlineData("nested/COM1.bin")]
    [InlineData(@"nested\LPT9\file.txt")]
    [InlineData("COM¹")]
    [InlineData("nested/LPT³.log")]
    [InlineData("file.")]
    [InlineData("file ")]
    [InlineData("folder./file.txt")]
    [InlineData("folder /file.txt")]
    [InlineData("folder/../safe.txt")]
    public void TryResolveSafeEntryPath_RejectsWindowsAliasedSegments(string entryName)
    {
        using var temp = new TempDirectory();

        Assert.False(ArchiveExtractor.TryResolveSafeEntryPath(temp.Path, entryName, out _));
    }

    [Theory]
    [InlineData("normal.txt")]
    [InlineData("nested/file.txt")]
    [InlineData(".git/config")]
    [InlineData("COM10.txt")]
    [InlineData("LPT0")]
    [InlineData("file.name")]
    [InlineData("nested/")]
    public void TryResolveSafeEntryPath_AllowsUnambiguousSegments(string entryName)
    {
        using var temp = new TempDirectory();

        Assert.True(ArchiveExtractor.TryResolveSafeEntryPath(temp.Path, entryName, out var resolved));
        Assert.StartsWith(temp.Path, resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractArchive_UnescapedReservedDeviceEntry_IsRejectedBeforeNativeSave()
    {
        using var temp = new TempDirectory();
        var archive = Path.Combine(temp.Path, "reserved.zip");
        var output = Path.Combine(temp.Path, "output");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("COM¹");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("must-not-reach-device");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ArchiveExtractor.ExtractArchive(
                archive,
                output,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task ExtractArchive_LibrarySanitizedReservedEntry_UsesEscapedFileName()
    {
        using var temp = new TempDirectory();
        var archive = Path.Combine(temp.Path, "sanitized.zip");
        var output = Path.Combine(temp.Path, "output");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("NUL");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("sanitized");
        }

        await ArchiveExtractor.ExtractArchive(
            archive,
            output,
            cancellationToken: TestContext.Current.CancellationToken);

        var extracted = Assert.Single(Directory.GetFiles(output, "*", SearchOption.AllDirectories));
        Assert.NotEqual("NUL", Path.GetFileName(extracted), StringComparer.OrdinalIgnoreCase);
        Assert.Equal("sanitized", await File.ReadAllTextAsync(extracted));
    }

    [Fact]
    public async Task ExtractArchive_BackupCleanupDoesNotFollowDirectoryJunction()
    {
        using var temp = new TempDirectory();
        var target = Path.Combine(temp.Path, "target");
        var targetFile = Path.Combine(target, "preserve.txt");
        var output = Path.Combine(temp.Path, "output");
        var junction = Path.Combine(output, "link");
        var archive = Path.Combine(temp.Path, "payload.zip");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(output);
        File.WriteAllText(targetFile, "preserve");
        File.SetAttributes(targetFile, File.GetAttributes(targetFile) | FileAttributes.ReadOnly);
        CreateJunction(junction, target);

        try
        {
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("new.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("new");
            }

            await ArchiveExtractor.ExtractArchive(
                archive,
                output,
                overwriteConfirmed: true,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(File.Exists(targetFile));
            Assert.NotEqual(
                0,
                (int)(File.GetAttributes(targetFile) & FileAttributes.ReadOnly));
        }
        finally
        {
            if (File.Exists(targetFile))
                File.SetAttributes(targetFile, File.GetAttributes(targetFile) & ~FileAttributes.ReadOnly);
        }
    }

    [Fact]
    public void RemoveReadOnlyAttributes_DoesNotFollowRootDirectoryJunction()
    {
        using var temp = new TempDirectory();
        var target = Path.Combine(temp.Path, "target");
        var targetFile = Path.Combine(target, "preserve.txt");
        var junction = Path.Combine(temp.Path, "link");
        Directory.CreateDirectory(target);
        File.WriteAllText(targetFile, "preserve");
        File.SetAttributes(targetFile, File.GetAttributes(targetFile) | FileAttributes.ReadOnly);
        CreateJunction(junction, target);

        try
        {
            ArchiveExtractor.RemoveReadOnlyAttributes(junction);

            Assert.NotEqual(
                0,
                (int)(File.GetAttributes(targetFile) & FileAttributes.ReadOnly));
        }
        finally
        {
            if (File.Exists(targetFile))
                File.SetAttributes(targetFile, File.GetAttributes(targetFile) & ~FileAttributes.ReadOnly);
        }
    }

    private static void CreateJunction(string junction, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "/d", "/c", "mklink", "/J", junction, target },
        });
        Assert.NotNull(process);
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"Lhamiel_ExtractionSecurity_{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // テスト後のベストエフォート掃除。
            }
        }
    }
}
