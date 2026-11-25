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
    public void DeleteDirectorySafely_RemovesReadOnlyEntriesAndKeepsJunctionTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), $"Lhamiel-ReadOnlyDelete-{Guid.NewGuid():N}");
        var target = Path.Combine(root, $"Lhamiel_Temp_{Guid.NewGuid():N}");
        var sub = Path.Combine(target, "sub");
        var roDir = Path.Combine(target, "rodir");
        var outside = Path.Combine(root, "outside");
        var junction = Path.Combine(target, "link");
        Directory.CreateDirectory(sub);
        Directory.CreateDirectory(roDir);
        Directory.CreateDirectory(outside);
        var roFile = Path.Combine(target, "ro.txt");
        var roSubFile = Path.Combine(sub, "ro2.txt");
        var roDirFile = Path.Combine(roDir, "ro3.txt");
        var outsideFile = Path.Combine(outside, "preserve.txt");
        File.WriteAllText(roFile, "a");
        File.WriteAllText(roSubFile, "b");
        File.WriteAllText(roDirFile, "c");
        File.WriteAllText(outsideFile, "preserve");
        File.SetAttributes(outsideFile, FileAttributes.ReadOnly);
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

            File.SetAttributes(roFile, FileAttributes.ReadOnly);
            File.SetAttributes(roSubFile, FileAttributes.ReadOnly);
            File.SetAttributes(roDirFile, FileAttributes.ReadOnly);
            File.SetAttributes(sub, FileAttributes.Directory | FileAttributes.ReadOnly);
            File.SetAttributes(roDir, FileAttributes.Directory | FileAttributes.ReadOnly);

            TempCleanup.DeleteDirectorySafely(target);

            Assert.False(Directory.Exists(target));
            Assert.True(File.Exists(outsideFile));
            Assert.True((File.GetAttributes(outsideFile) & FileAttributes.ReadOnly) != 0);
        }
        finally
        {
            if (File.Exists(outsideFile))
                File.SetAttributes(outsideFile, FileAttributes.Normal);
            if (Directory.Exists(junction))
                Directory.Delete(junction);
            if (Directory.Exists(root))
            {
                // 後始末で本来の assertion 失敗を隠さないよう、ディレクトリの ReadOnly も外してから消す。
                foreach (var f in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
                {
                    var a = File.GetAttributes(f);
                    File.SetAttributes(f, (a & FileAttributes.Directory) != 0 ? FileAttributes.Directory : FileAttributes.Normal);
                }
                Directory.Delete(root, recursive: true);
            }
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

/// <summary>
/// TempCleanup.CleanupOrphanedTempDirectories（追跡外の Lhamiel_Temp_&lt;Guid:N&gt; 掃除）のテスト。
/// 共有の %TEMP% 直下を走査・削除するため、分割前と同じく [Collection("Sequential")] で排他実行する。
/// 同じ一時フォルダー掃除の契約を扱う TrackedTempCleanupTests と同じファイルに置く。
/// 旧 VelopackIntegrationHappyTests / VelopackIntegrationAdversarialTests から移動した。
/// </summary>
[Collection("Sequential")]
public class TempCleanupOrphanedDirectoryTests
{
    /// <summary>
    /// CleanupOrphanedTempDirectories が 30 分以上前に作成された Lhamiel_Temp_&lt;Guid:N&gt; を削除すること。
    /// </summary>
    [Fact]
    public void TempCleanup_CleanupOrphanedTempDirectories_DeletesOldAutoGeneratedDirectories()
    {
        // Arrange
        var tempRoot = Path.GetTempPath();
        var guid = Guid.NewGuid().ToString("N");
        var oldDir = Path.Combine(tempRoot, $"Lhamiel_Temp_{guid}");
        Directory.CreateDirectory(oldDir);
        // 1 時間前に偽装 (MinAge=30 分超え)
        var oldTime = DateTime.UtcNow.AddHours(-1);
        Directory.SetLastWriteTimeUtc(oldDir, oldTime);

        try
        {
            // Act
            TempCleanup.CleanupOrphanedTempDirectories();

            // Assert
            Assert.False(Directory.Exists(oldDir), "30 分以上前の Lhamiel_Temp_<Guid> は削除されるべき");
        }
        finally
        {
            if (Directory.Exists(oldDir))
            {
                try { Directory.Delete(oldDir, recursive: true); } catch { /* best-effort */ }
            }
        }
    }

    /// <summary>
    /// TempCleanup が GUID 形式でない suffix を持つディレクトリは削除しない (ユーザーデータ誤削除防御)。
    /// </summary>
    [Fact]
    public void TempCleanup_NonGuidSuffix_NotDeleted()
    {
        // Arrange
        var tempRoot = Path.GetTempPath();
        var fakeDir = Path.Combine(tempRoot, "Lhamiel_Temp_NotAGuid_User_Data");
        Directory.CreateDirectory(fakeDir);
        Directory.SetLastWriteTimeUtc(fakeDir, DateTime.UtcNow.AddDays(-7));
        try
        {
            // Act
            TempCleanup.CleanupOrphanedTempDirectories();

            // Assert
            Assert.True(Directory.Exists(fakeDir),
                "GUID 形式でない末尾のディレクトリは削除されてはならない");
        }
        finally
        {
            try { if (Directory.Exists(fakeDir)) Directory.Delete(fakeDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 30 分以内の新しい Lhamiel_Temp_{Guid} は誤削除されない (MinAge=30 分ガード)。
    /// </summary>
    [Fact]
    public void TempCleanup_FreshOrphanedDir_PreservedDueToMinAgeGuard()
    {
        // Arrange
        var tempRoot = Path.GetTempPath();
        var freshDir = Path.Combine(tempRoot, $"Lhamiel_Temp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(freshDir);
        // mtime は現在 (= MinAge 30 分未満)
        try
        {
            // Act
            TempCleanup.CleanupOrphanedTempDirectories();

            // Assert
            Assert.True(Directory.Exists(freshDir),
                "MinAge (30 分) 未満の Lhamiel_Temp ディレクトリは保護されるべき");
        }
        finally
        {
            try { if (Directory.Exists(freshDir)) Directory.Delete(freshDir, recursive: true); } catch { }
        }
    }
}
