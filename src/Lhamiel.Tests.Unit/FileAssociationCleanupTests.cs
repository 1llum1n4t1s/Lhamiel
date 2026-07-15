using Lhamiel.Util;
using Microsoft.Win32;
using System.Runtime.Versioning;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// アンインストール時のファイル関連付け解除テスト。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FileAssociationCleanupTests : IDisposable
{
    private readonly string _testRootPath = $@"Software\Lhamiel.Tests\FileAssociation\{Guid.NewGuid():N}";

    [Fact]
    public void DisassociateFileType_RemovesLhamielDefaultAndPreservesSharedExtensionData()
    {
        var extensionPath = $@"{_testRootPath}\.zip";
        using (var extensionKey = Registry.CurrentUser.CreateSubKey(extensionPath))
        {
            extensionKey.SetValue("", "Lhamiel.zip");
            extensionKey.SetValue("Content Type", "application/zip");
            extensionKey.CreateSubKey("OpenWithProgids").Dispose();
        }
        Registry.CurrentUser.CreateSubKey($@"{_testRootPath}\Lhamiel.zip\shell\open\command")!.Dispose();

        FileAssociation.DisassociateFileType(Registry.CurrentUser, _testRootPath, "zip");

        using var remainingExtensionKey = Registry.CurrentUser.OpenSubKey(extensionPath);
        Assert.NotNull(remainingExtensionKey);
        Assert.Null(remainingExtensionKey!.GetValue(""));
        Assert.Equal("application/zip", remainingExtensionKey.GetValue("Content Type"));
        using var openWithKey = remainingExtensionKey.OpenSubKey("OpenWithProgids");
        Assert.NotNull(openWithKey);
        Assert.Null(Registry.CurrentUser.OpenSubKey($@"{_testRootPath}\Lhamiel.zip"));
    }

    [Fact]
    public void DisassociateFileType_PreservesAnotherApplicationsDefault()
    {
        var extensionPath = $@"{_testRootPath}\.7z";
        using (var extensionKey = Registry.CurrentUser.CreateSubKey(extensionPath))
            extensionKey.SetValue("", "OtherArchiver.7z");
        Registry.CurrentUser.CreateSubKey($@"{_testRootPath}\Lhamiel.7z")!.Dispose();

        FileAssociation.DisassociateFileType(Registry.CurrentUser, _testRootPath, ".7z");

        using var remainingExtensionKey = Registry.CurrentUser.OpenSubKey(extensionPath);
        Assert.NotNull(remainingExtensionKey);
        Assert.Equal("OtherArchiver.7z", remainingExtensionKey!.GetValue(""));
        Assert.Null(Registry.CurrentUser.OpenSubKey($@"{_testRootPath}\Lhamiel.7z"));
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_testRootPath, throwOnMissingSubKey: false);
    }
}
