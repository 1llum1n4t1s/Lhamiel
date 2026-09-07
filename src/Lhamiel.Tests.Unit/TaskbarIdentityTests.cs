using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

public class TaskbarIdentityTests
{
    [Fact]
    public void Resolve_BothLaunchRoutesUseRegisteredIdentity()
    {
        const string executable = @"C:\Apps\Lhamiel\current\Lhamiel.exe";
        string[] registered = [@"C:\Apps\Lhamiel\current"];
        Assert.Equal(TaskbarIdentity.PackagedAppId, TaskbarIdentity.Resolve(null, executable, registered));
        Assert.Equal(TaskbarIdentity.PackagedAppId,
            TaskbarIdentity.Resolve(TaskbarIdentity.PackagedAppId, executable, registered));
    }

    [Theory]
    [InlineData(@"C:\Apps\Lhamiel\current\Lhamiel.exe", @"c:\apps\lhamiel\current\", true)]
    [InlineData(@"C:\Dev\Lhamiel\Lhamiel.exe", @"C:\Apps\Lhamiel\current", false)]
    [InlineData(@"C:\Apps\Lhamiel\current2\Lhamiel.exe", @"C:\Apps\Lhamiel\current", false)]
    [InlineData(@"C:\Apps\Lhamiel\current\Other.exe", @"C:\Apps\Lhamiel\current", false)]
    public void Resolve_OnlyUsesRegistrationForSameExecutable(string executable, string directory, bool matches)
    {
        Assert.Equal(matches ? TaskbarIdentity.PackagedAppId : TaskbarIdentity.UnpackagedAppId,
            TaskbarIdentity.Resolve(null, executable, [directory]));
    }

    [Fact]
    public void Resolve_NoRegistrationPreservesVelopackIdentity()
    {
        Assert.Equal(TaskbarIdentity.UnpackagedAppId,
            TaskbarIdentity.Resolve(null, @"C:\Apps\Lhamiel\Lhamiel.exe", []));
    }

    [Fact]
    public void NativeLookup_CanReadRegisteredExternalLocations()
    {
        var directories = TaskbarIdentity.ReadExternalDirectories();
        foreach (var directory in directories)
        {
            Assert.True(Path.IsPathFullyQualified(directory));
            Assert.Equal(TaskbarIdentity.PackagedAppId,
                TaskbarIdentity.GetAppId(Path.Combine(directory, "Lhamiel.exe")));
        }
        TestContext.Current.TestOutputHelper!.WriteLine($"Windows から取得した外部配置先: {string.Join(", ", directories)}");
    }

    [Fact]
    public void ShortcutMigration_PreservesTargetAndArgumentsAndDoesNotRewriteAgain()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"Lhamiel-Identity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var shortcut = Path.Combine(directory, "Lhamiel.lnk");
        var executable = Environment.ProcessPath!;
        try
        {
            Assert.True(ShellLinkNative.CreateShortcut(executable, shortcut, "identity test", executable,
                TaskbarIdentity.UnpackagedAppId, "--compress"));
            Assert.False(ShortcutCreator.MigrateShortcutIdentity(shortcut, Path.Combine(directory, "Other.exe"),
                TaskbarIdentity.PackagedAppId));
            Assert.Equal(TaskbarIdentity.UnpackagedAppId, ShellLinkNative.GetAppUserModelId(shortcut));
            Assert.True(ShortcutCreator.MigrateShortcutIdentity(shortcut, executable, TaskbarIdentity.PackagedAppId));
            Assert.Equal(TaskbarIdentity.PackagedAppId, ShellLinkNative.GetAppUserModelId(shortcut));
            Assert.Equal(executable, ShellLinkNative.GetTargetPath(shortcut), ignoreCase: true);
            Assert.Equal("--compress", ShellLinkNative.GetArguments(shortcut));
            var lastWrite = File.GetLastWriteTimeUtc(shortcut);
            Assert.False(ShortcutCreator.MigrateShortcutIdentity(shortcut, executable, TaskbarIdentity.PackagedAppId));
            Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(shortcut));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
