using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// FolderOpener のユニットテスト（実装を信用しないエッジケース重視）
/// </summary>
public class FolderOpenerTests
{
    // === OpenExtractionResult ===

    [Fact]
    public void OpenExtractionResult_WithNullStructureInfo_DoesNotThrow()
    {
        // structureInfo が null でも例外にならないこと
        // 存在しないパスなので実際にエクスプローラーは開かない
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var exception = Record.Exception(() =>
            FolderOpener.OpenExtractionResult(nonExistentPath, null));
        Assert.Null(exception);
    }

    [Fact]
    public void OpenExtractionResult_WithNonExistentPath_DoesNotThrow()
    {
        var structureInfo = new ArchiveExtractor.ArchiveStructureInfo
        {
            HasSingleRootItem = false
        };
        var exception = Record.Exception(() =>
            FolderOpener.OpenExtractionResult(@"C:\nonexistent\path", structureInfo));
        Assert.Null(exception);
    }

    [Fact]
    public void OpenExtractionResult_WithSingleRootButEmptyName_DoesNotThrow()
    {
        var structureInfo = new ArchiveExtractor.ArchiveStructureInfo
        {
            HasSingleRootItem = true,
            SingleRootItemName = ""
        };
        var exception = Record.Exception(() =>
            FolderOpener.OpenExtractionResult(@"C:\nonexistent", structureInfo));
        Assert.Null(exception);
    }

    [Fact]
    public void OpenExtractionResult_WithSingleRootAndNullName_DoesNotThrow()
    {
        var structureInfo = new ArchiveExtractor.ArchiveStructureInfo
        {
            HasSingleRootItem = true,
            SingleRootItemName = null
        };
        var exception = Record.Exception(() =>
            FolderOpener.OpenExtractionResult(@"C:\nonexistent", structureInfo));
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
