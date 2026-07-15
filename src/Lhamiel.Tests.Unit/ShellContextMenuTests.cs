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

    [Fact]
    public void ApplyRegistration_OnWindows11_RegistersSparsePackageAndRemovesLegacyVerbs()
    {
        var applicationDirectory = CreateModernArtifacts();
        try
        {
            var appPath = Path.Combine(applicationDirectory, "Lhamiel.exe");
            ShellContextMenu.Register(Registry.CurrentUser, _testRootPath, appPath);
            (string ExtensionPath, string PackageUri, string ExternalLocationUri)? invocation = null;

            ShellContextMenu.ApplyRegistration(
                Registry.CurrentUser,
                _testRootPath,
                appPath,
                enabled: true,
                preferModernMenu: true,
                (extensionPath, packageUri, externalLocationUri) =>
                {
                    invocation = (extensionPath, packageUri, externalLocationUri);
                    return 0;
                },
                (_, _) => throw new InvalidOperationException("解除処理は呼ばれません。"));

            Assert.NotNull(invocation);
            Assert.Equal(Path.Combine(applicationDirectory, ShellContextMenu.ShellExtensionFileName), invocation!.Value.ExtensionPath);
            Assert.Equal(new Uri(Path.Combine(applicationDirectory, ShellContextMenu.ModernPackageFileName)).AbsoluteUri, invocation.Value.PackageUri);
            Assert.Equal(new Uri(applicationDirectory + Path.DirectorySeparatorChar).AbsoluteUri, invocation.Value.ExternalLocationUri);
            Assert.Null(Registry.CurrentUser.OpenSubKey(ShellContextMenu.BuildVerbPath(_testRootPath, "*")));
            Assert.Null(Registry.CurrentUser.OpenSubKey(ShellContextMenu.BuildVerbPath(_testRootPath, "Directory")));
        }
        finally
        {
            Directory.Delete(applicationDirectory, recursive: true);
        }
    }

    [Fact]
    public void ApplyRegistration_WhenModernArtifactsAreMissing_FallsBackToLegacyVerbs()
    {
        var appPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Lhamiel.exe");

        ShellContextMenu.ApplyRegistration(
            Registry.CurrentUser,
            _testRootPath,
            appPath,
            enabled: true,
            preferModernMenu: true,
            (_, _, _) => throw new InvalidOperationException("配布物が無い場合は呼ばれません。"),
            (_, _) => throw new InvalidOperationException("解除処理は呼ばれません。"));

        AssertVerb("*", appPath);
        AssertVerb("Directory", appPath);
    }

    [Fact]
    public void ApplyRegistration_WhenDisabled_UnregistersSparsePackageAndLegacyVerbs()
    {
        var applicationDirectory = CreateModernArtifacts();
        try
        {
            var appPath = Path.Combine(applicationDirectory, "Lhamiel.exe");
            ShellContextMenu.Register(Registry.CurrentUser, _testRootPath, appPath);
            (string ExtensionPath, string PackageName)? invocation = null;

            ShellContextMenu.ApplyRegistration(
                Registry.CurrentUser,
                _testRootPath,
                appPath,
                enabled: false,
                preferModernMenu: true,
                (_, _, _) => throw new InvalidOperationException("登録処理は呼ばれません。"),
                (extensionPath, packageName) =>
                {
                    invocation = (extensionPath, packageName);
                    return 0;
                });

            Assert.NotNull(invocation);
            Assert.Equal(Path.Combine(applicationDirectory, ShellContextMenu.ShellExtensionFileName), invocation!.Value.ExtensionPath);
            Assert.Equal(ShellContextMenu.ModernPackageName, invocation.Value.PackageName);
            Assert.Null(Registry.CurrentUser.OpenSubKey(ShellContextMenu.BuildVerbPath(_testRootPath, "*")));
            Assert.Null(Registry.CurrentUser.OpenSubKey(ShellContextMenu.BuildVerbPath(_testRootPath, "Directory")));
        }
        finally
        {
            Directory.Delete(applicationDirectory, recursive: true);
        }
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

    private static string CreateModernArtifacts()
    {
        var applicationDirectory = Path.Combine(Path.GetTempPath(), "Lhamiel.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(applicationDirectory);
        File.WriteAllText(Path.Combine(applicationDirectory, ShellContextMenu.ShellExtensionFileName), string.Empty);
        File.WriteAllText(Path.Combine(applicationDirectory, ShellContextMenu.ModernPackageFileName), string.Empty);
        return applicationDirectory;
    }
}
