using Lhamiel.Util;
using Microsoft.Win32;
using System.Runtime.Versioning;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// エクスプローラー右クリックメニュー登録のテスト。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ShellContextMenuTests : IDisposable
{
    private readonly string _testRootPath = $@"Software\Lhamiel.Tests\ShellContextMenu\{Guid.NewGuid():N}";

    [Fact]
    public void Register_AddsFileAndDirectoryVerbsUsingShortcutDropCommandLine()
    {
        const string appPath = @"C:\Program Files\Lhamiel\Lhamiel.exe";

        ShellContextMenu.Register(Registry.CurrentUser, _testRootPath, appPath);

        AssertVerb("*", appPath);
        AssertVerb("Directory", appPath);
    }

    [Fact]
    public void Unregister_RemovesOnlyLhamielVerbs()
    {
        const string appPath = @"C:\Program Files\Lhamiel\Lhamiel.exe";
        ShellContextMenu.Register(Registry.CurrentUser, _testRootPath, appPath);

        ShellContextMenu.Unregister(Registry.CurrentUser, _testRootPath);

        Assert.Null(Registry.CurrentUser.OpenSubKey(
            ShellContextMenu.BuildVerbPath(_testRootPath, "*")));
        Assert.Null(Registry.CurrentUser.OpenSubKey(
            ShellContextMenu.BuildVerbPath(_testRootPath, "Directory")));
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_testRootPath, throwOnMissingSubKey: false);
    }

    private void AssertVerb(string targetClass, string appPath)
    {
        var verbPath = ShellContextMenu.BuildVerbPath(_testRootPath, targetClass);
        using var verbKey = Registry.CurrentUser.OpenSubKey(verbPath);
        Assert.NotNull(verbKey);
        Assert.Equal(ShellContextMenu.MenuText, verbKey!.GetValue(""));
        Assert.Equal($"\"{appPath}\",0", verbKey.GetValue("Icon"));
        Assert.Equal("Player", verbKey.GetValue("MultiSelectModel"));

        using var commandKey = Registry.CurrentUser.OpenSubKey($@"{verbPath}\command");
        Assert.NotNull(commandKey);
        Assert.Equal(ShellContextMenu.BuildCommand(appPath), commandKey!.GetValue(""));
        Assert.Equal($"\"{appPath}\" \"%1\"", commandKey.GetValue(""));
    }
}
