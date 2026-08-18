using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

public sealed class TrackedTempCleanupTests
{
    [Fact]
    public void CleanupTrackedDirectories_DeletesOnlyOldRegisteredGeneratedDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"Lhamiel-TrackedCleanupTest-{Guid.NewGuid():N}");
        var manifest = Path.Combine(root, "tracked.txt");
        var tracked = Path.Combine(root, $"Lhamiel_Extract_{Guid.NewGuid():N}");
        var untracked = Path.Combine(root, $"Lhamiel_Extract_{Guid.NewGuid():N}");
        var outside = Path.Combine(root, "outside");
        var junction = Path.Combine(tracked, "link");
        Directory.CreateDirectory(tracked);
        Directory.CreateDirectory(untracked);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(tracked, "secret.txt"), "temporary");
        var outsideFile = Path.Combine(outside, "preserve.txt");
        File.WriteAllText(outsideFile, "preserve");
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                ArgumentList = { "/d", "/c", "mklink", "/J", junction, outside },
            });
            Assert.NotNull(process);
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());

            Assert.True(TempCleanup.RegisterTrackedDirectory(tracked, manifest));
            var now = DateTime.UtcNow;
            Directory.SetLastWriteTimeUtc(tracked, now - TimeSpan.FromHours(1));

            Assert.Equal(1, TempCleanup.CleanupTrackedDirectories(manifest, now));
            Assert.False(Directory.Exists(tracked));
            Assert.True(Directory.Exists(untracked));
            Assert.Equal("preserve", File.ReadAllText(outsideFile));
            Assert.False(File.Exists(manifest));
        }
        finally
        {
            if (Directory.Exists(junction))
                Directory.Delete(junction);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CleanupTrackedDirectories_PreservesFreshDirectoryForRetry()
    {
        var root = Path.Combine(Path.GetTempPath(), $"Lhamiel-TrackedCleanupFresh-{Guid.NewGuid():N}");
        var manifest = Path.Combine(root, "tracked.txt");
        var tracked = Path.Combine(root, $"Lhamiel_Temp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tracked);
        try
        {
            Assert.True(TempCleanup.RegisterTrackedDirectory(tracked, manifest));

            Assert.Equal(0, TempCleanup.CleanupTrackedDirectories(manifest, DateTime.UtcNow));
            Assert.True(Directory.Exists(tracked));
            Assert.True(File.Exists(manifest));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
