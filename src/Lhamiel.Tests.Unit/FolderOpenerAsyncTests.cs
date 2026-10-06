using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// FolderOpener の待機可能版 (OpenFolderAsync / OpenExtractionResultAsync) のテスト。
/// これらは CLI / ファイル関連付け経路で「ファイルマネージャー起動完了を await してから
/// アプリをシャットダウンする」ために追加されたメソッドで、v1.0.171 で混入した
/// 「展開先を開く設定が効かない (fire-and-forget × 即時 Shutdown 競合)」回帰の修正。
/// </summary>
[Collection("FolderOpener")]
public class FolderOpenerAsyncTests : IDisposable
{
    public FolderOpenerAsyncTests()
    {
        FolderOpener.DryRun = true;
        ShellOpener.DryRun = true;
    }

    public void Dispose()
    {
        FolderOpener.DryRun = false;
        ShellOpener.DryRun = false;
    }

    // === OpenFolderAsync: ガード経路は完了する ===

    [Fact]
    public async Task OpenFolderAsync_WithNullPath_CompletesWithoutThrow()
    {
        await FolderOpener.OpenFolderAsync(null!);
    }

    [Fact]
    public async Task OpenFolderAsync_WithWhitespacePath_CompletesWithoutThrow()
    {
        await FolderOpener.OpenFolderAsync("   ");
    }

    [Fact]
    public async Task OpenFolderAsync_WithNonExistentPath_CompletesWithoutThrow()
    {
        await FolderOpener.OpenFolderAsync(@"C:\nonexistent_folder_" + Guid.NewGuid());
    }


    // === OpenExtractionResultAsync ===

    [Fact]
    public async Task OpenExtractionResultAsync_WithNonExistentPath_CompletesWithoutThrow()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        await FolderOpener.OpenExtractionResultAsync(nonExistentPath);
    }

    [Fact]
    public async Task OpenExtractionResultAsync_WithExistingDirectory_Completes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Lhamiel_Test_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            // DryRun のため実起動はせず、決定したフォルダの解決 + await 完了のみを確認する。
            await FolderOpener.OpenExtractionResultAsync(tempDir);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
