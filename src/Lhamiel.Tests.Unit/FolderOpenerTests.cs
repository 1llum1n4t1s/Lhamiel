using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// FolderOpener のユニットテスト（実装を信用しないエッジケース重視）
/// </summary>
[Collection("FolderOpener")]
public class FolderOpenerTests : IDisposable
{
    public FolderOpenerTests()
    {
        FolderOpener.DryRun = true;
    }

    public void Dispose()
    {
        FolderOpener.DryRun = false;
    }
    // === OpenExtractionResult ===

    [Fact]
    public void OpenExtractionResult_WithNonExistentPath_DoesNotThrow()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var exception = Record.Exception(() =>
            FolderOpener.OpenExtractionResult(nonExistentPath));
        Assert.Null(exception);
    }

    // === OpenFolder ===

    [Fact]
    public void OpenFolder_WithNullPath_DoesNotThrow()
    {
        var exception = Record.Exception(() => FolderOpener.OpenFolder(null!));
        Assert.Null(exception);
    }

    [Fact]
    public void OpenFolder_WithEmptyPath_DoesNotThrow()
    {
        var exception = Record.Exception(() => FolderOpener.OpenFolder(""));
        Assert.Null(exception);
    }

    [Fact]
    public void OpenFolder_WithWhitespacePath_DoesNotThrow()
    {
        var exception = Record.Exception(() => FolderOpener.OpenFolder("   "));
        Assert.Null(exception);
    }

    [Fact]
    public void OpenFolder_WithNonExistentPath_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            FolderOpener.OpenFolder(@"C:\nonexistent_folder_" + Guid.NewGuid()));
        Assert.Null(exception);
    }
}
