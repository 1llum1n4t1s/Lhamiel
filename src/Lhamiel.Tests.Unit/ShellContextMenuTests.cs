using Lhamiel.Util;
using Microsoft.Win32;
using System.Runtime.Versioning;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>エクスプローラー右クリックメニュー登録のテスト。</summary>
[SupportedOSPlatform("windows")]
public sealed class ShellContextMenuTests : IDisposable
{
    private readonly string _testRootPath = $@"Software\Lhamiel.Tests\ShellContextMenu\{Guid.NewGuid():N}";

    [Fact]
    public void Register_BothEnabled_AddsExtractionForFilesAndCompressionForFilesAndDirectories()
    {
        const string appPath = @"C:\Program Files\Lhamiel\Lhamiel.exe";

        ShellContextMenu.Register(
            Registry.CurrentUser, _testRootPath, appPath, extractEnabled: true, compressEnabled: true);

        AssertVerb("*", appPath, ContextMenuOperation.Extract);
        AssertVerb("*", appPath, ContextMenuOperation.Compress);
        AssertVerb("Directory", appPath, ContextMenuOperation.Compress);
        Assert.Null(Registry.CurrentUser.OpenSubKey(
            ShellContextMenu.BuildVerbPath(_testRootPath, "Directory", ContextMenuOperation.Extract)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Register_OnlyAddsEnabledOperations(bool extractEnabled, bool compressEnabled)
    {
        const string appPath = @"C:\Program Files\Lhamiel\Lhamiel.exe";

        ShellContextMenu.Register(
            Registry.CurrentUser, _testRootPath, appPath, extractEnabled, compressEnabled);

        Assert.Equal(extractEnabled, VerbExists("*", ContextMenuOperation.Extract));
        Assert.Equal(compressEnabled, VerbExists("*", ContextMenuOperation.Compress));
        Assert.Equal(compressEnabled, VerbExists("Directory", ContextMenuOperation.Compress));
    }

    [Fact]
    public void Unregister_RemovesNewAndLegacyLhamielVerbs()
    {
        const string appPath = @"C:\Program Files\Lhamiel\Lhamiel.exe";
        ShellContextMenu.Register(
            Registry.CurrentUser, _testRootPath, appPath, extractEnabled: true, compressEnabled: true);
        using (Registry.CurrentUser.CreateSubKey(ShellContextMenu.BuildLegacyVerbPath(_testRootPath, "*"))) { }

        ShellContextMenu.Unregister(Registry.CurrentUser, _testRootPath);

        Assert.False(VerbExists("*", ContextMenuOperation.Extract));
        Assert.False(VerbExists("*", ContextMenuOperation.Compress));
        Assert.Null(Registry.CurrentUser.OpenSubKey(
            ShellContextMenu.BuildLegacyVerbPath(_testRootPath, "*")));
    }

    [Fact]
    public void ApplyRegistration_OnWindows11_RegistersSparsePackageAndWritesIndependentState()
    {
        var applicationDirectory = CreateModernArtifacts();
        try
        {
            var appPath = Path.Combine(applicationDirectory, "Lhamiel.exe");
            (string ExtensionPath, string PackageUri, string ExternalLocationUri)? invocation = null;

            ShellContextMenu.ApplyRegistration(
                Registry.CurrentUser,
                _testRootPath,
                appPath,
                extractEnabled: true,
                compressEnabled: false,
                preferModernMenu: true,
                (extensionPath, packageUri, externalLocationUri) =>
                {
                    invocation = (extensionPath, packageUri, externalLocationUri);
                    return 0;
                });

            Assert.NotNull(invocation);
            Assert.Equal(Path.Combine(applicationDirectory, ShellContextMenu.ShellExtensionFileName), invocation!.Value.ExtensionPath);
            Assert.Equal(new Uri(Path.Combine(applicationDirectory, ShellContextMenu.ModernPackageFileName)).AbsoluteUri, invocation.Value.PackageUri);
            Assert.Equal(new Uri(applicationDirectory + Path.DirectorySeparatorChar).AbsoluteUri, invocation.Value.ExternalLocationUri);
            Assert.False(VerbExists("*", ContextMenuOperation.Extract));
            Assert.False(VerbExists("*", ContextMenuOperation.Compress));
            AssertState(extractEnabled: true, compressEnabled: false);
        }
        finally
        {
            Directory.Delete(applicationDirectory, recursive: true);
        }
    }

    [Fact]
    public void ApplyRegistration_WhenModernArtifactsAreMissing_FallsBackToEnabledLegacyVerbs()
    {
        var appPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Lhamiel.exe");

        ShellContextMenu.ApplyRegistration(
            Registry.CurrentUser,
            _testRootPath,
            appPath,
            extractEnabled: false,
            compressEnabled: true,
            preferModernMenu: true,
            (_, _, _) => throw new InvalidOperationException("配布物が無い場合は呼ばれません。"));

        Assert.False(VerbExists("*", ContextMenuOperation.Extract));
        AssertVerb("*", appPath, ContextMenuOperation.Compress);
        AssertVerb("Directory", appPath, ContextMenuOperation.Compress);
        AssertState(extractEnabled: false, compressEnabled: true);
    }

    [Fact]
    public void ApplyRegistration_WhenModernRegistrationFails_PreservesPreviousPublishedState()
    {
        var applicationDirectory = CreateModernArtifacts();
        try
        {
            var appPath = Path.Combine(applicationDirectory, "Lhamiel.exe");
            ShellContextMenu.Register(
                Registry.CurrentUser,
                _testRootPath,
                appPath,
                extractEnabled: false,
                compressEnabled: true);
            ShellContextMenu.WriteState(
                Registry.CurrentUser,
                _testRootPath,
                extractEnabled: false,
                compressEnabled: true);

            Assert.ThrowsAny<Exception>(() =>
                ShellContextMenu.ApplyRegistration(
                    Registry.CurrentUser,
                    _testRootPath,
                    appPath,
                    extractEnabled: true,
                    compressEnabled: false,
                    preferModernMenu: true,
                    (_, _, _) => unchecked((int)0x80004005)));

            AssertState(extractEnabled: false, compressEnabled: true);
            Assert.False(VerbExists("*", ContextMenuOperation.Extract));
            AssertVerb("*", appPath, ContextMenuOperation.Compress);
            AssertVerb("Directory", appPath, ContextMenuOperation.Compress);
        }
        finally
        {
            Directory.Delete(applicationDirectory, recursive: true);
        }
    }

    [Fact]
    public void ApplyRegistration_WhenModernPackageUpdateIsPending_PublishesStateWithoutLegacyFallback()
    {
        var applicationDirectory = CreateModernArtifacts();
        try
        {
            var appPath = Path.Combine(applicationDirectory, "Lhamiel.exe");
            ShellContextMenu.Register(
                Registry.CurrentUser, _testRootPath, appPath, extractEnabled: true, compressEnabled: true);
            ShellContextMenu.WriteState(Registry.CurrentUser, _testRootPath, true, true);

            ShellContextMenu.ApplyRegistration(
                Registry.CurrentUser,
                _testRootPath,
                appPath,
                extractEnabled: true,
                compressEnabled: false,
                preferModernMenu: true,
                (_, _, _) => ShellContextMenu.PackagePendingRemovalHResult);

            Assert.False(VerbExists("*", ContextMenuOperation.Extract));
            Assert.False(VerbExists("*", ContextMenuOperation.Compress));
            AssertState(extractEnabled: true, compressEnabled: false);
        }
        finally
        {
            Directory.Delete(applicationDirectory, recursive: true);
        }
    }

    [Fact]
    public void ApplyRegistration_WhenBothDisabled_KeepsSparsePackageAndWritesDisabledState()
    {
        const string appPath = @"C:\Program Files\Lhamiel\Lhamiel.exe";
        ShellContextMenu.Register(
            Registry.CurrentUser, _testRootPath, appPath, extractEnabled: true, compressEnabled: true);
        ShellContextMenu.WriteState(Registry.CurrentUser, _testRootPath, true, true);

        ShellContextMenu.ApplyRegistration(
            Registry.CurrentUser,
            _testRootPath,
            appPath,
            extractEnabled: false,
            compressEnabled: false,
            preferModernMenu: true,
            (_, _, _) => throw new InvalidOperationException("登録処理は呼ばれません。"));

        Assert.False(VerbExists("*", ContextMenuOperation.Extract));
        Assert.False(VerbExists("*", ContextMenuOperation.Compress));
        AssertState(extractEnabled: false, compressEnabled: false);
    }

    [Fact]
    public void RemoveRegistration_UnregistersEverythingAndDeletesState()
    {
        var applicationDirectory = CreateModernArtifacts();
        try
        {
            var appPath = Path.Combine(applicationDirectory, "Lhamiel.exe");
            ShellContextMenu.Register(
                Registry.CurrentUser, _testRootPath, appPath, extractEnabled: true, compressEnabled: true);
            ShellContextMenu.WriteState(Registry.CurrentUser, _testRootPath, true, true);
            (string ExtensionPath, string PackageFamilyName)? invocation = null;

            ShellContextMenu.RemoveRegistration(
                Registry.CurrentUser,
                _testRootPath,
                appPath,
                (extensionPath, packageFamilyName) =>
                {
                    invocation = (extensionPath, packageFamilyName);
                    return 0;
                });

            Assert.NotNull(invocation);
            Assert.Equal(ShellContextMenu.ModernPackageFamilyName, invocation!.Value.PackageFamilyName);
            Assert.False(VerbExists("*", ContextMenuOperation.Extract));
            Assert.False(VerbExists("*", ContextMenuOperation.Compress));
            Assert.Null(Registry.CurrentUser.OpenSubKey(ShellContextMenu.BuildStatePath(_testRootPath)));
        }
        finally
        {
            Directory.Delete(applicationDirectory, recursive: true);
        }
    }

    public void Dispose() =>
        Registry.CurrentUser.DeleteSubKeyTree(_testRootPath, throwOnMissingSubKey: false);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplyRegistration_OnlyDeploysWhenPackageIsNotCurrent(bool current)
    {
        var directory = CreateModernArtifacts();
        try
        {
            var packagePath = Path.Combine(directory, ShellContextMenu.ModernPackageFileName);
            using (var file = File.Create(packagePath))
            using (var zip = new System.IO.Compression.ZipArchive(file, System.IO.Compression.ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("AppxManifest.xml").Open()))
                writer.Write("<Package xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\"><Identity Version=\"1.2.3.4\" /></Package>");
            var deployed = 0;
            ShellContextMenu.ApplyRegistration(Registry.CurrentUser, _testRootPath,
                Path.Combine(directory, "Lhamiel.exe"), true, false, true,
                (_, _, _) => { deployed++; return 0; },
                (dll, externalDirectory, version) =>
                {
                    Assert.Equal(directory, externalDirectory);
                    Assert.Equal(Path.Combine(directory, ShellContextMenu.ShellExtensionFileName), dll);
                    Assert.Equal(0x0001000200030004UL, version);
                    return current;
                });
            Assert.Equal(current ? 0 : 1, deployed);
            AssertState(true, false);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private bool VerbExists(string targetClass, ContextMenuOperation operation)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            ShellContextMenu.BuildVerbPath(_testRootPath, targetClass, operation));
        return key != null;
    }

    private void AssertVerb(string targetClass, string appPath, ContextMenuOperation operation)
    {
        var verbPath = ShellContextMenu.BuildVerbPath(_testRootPath, targetClass, operation);
        using var verbKey = Registry.CurrentUser.OpenSubKey(verbPath);
        Assert.NotNull(verbKey);
        Assert.Equal(
            operation == ContextMenuOperation.Extract
                ? ShellContextMenu.ExtractMenuText
                : ShellContextMenu.CompressMenuText,
            verbKey!.GetValue(""));
        Assert.Equal($"\"{appPath}\",0", verbKey.GetValue("Icon"));
        Assert.Equal("Player", verbKey.GetValue("MultiSelectModel"));

        using var commandKey = Registry.CurrentUser.OpenSubKey($@"{verbPath}\command");
        Assert.NotNull(commandKey);
        Assert.Equal(ShellContextMenu.BuildCommand(appPath, operation), commandKey!.GetValue(""));
    }

    private void AssertState(bool extractEnabled, bool compressEnabled)
    {
        using var stateKey = Registry.CurrentUser.OpenSubKey(ShellContextMenu.BuildStatePath(_testRootPath));
        Assert.NotNull(stateKey);
        Assert.Equal(extractEnabled ? 1 : 0, stateKey!.GetValue(ShellContextMenu.ExtractEnabledValueName));
        Assert.Equal(compressEnabled ? 1 : 0, stateKey.GetValue(ShellContextMenu.CompressEnabledValueName));
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
