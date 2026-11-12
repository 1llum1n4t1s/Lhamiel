using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// FolderOpener の嫌がらせテスト — 境界値、特殊パス、状態異常を攻める
/// </summary>
[Collection("FolderOpener")]
public class FolderOpenerAdversarialTests : IDisposable
{
    // 作成した一時ディレクトリ。削除は共通ヘルパー TestDirectory の Dispose（best-effort）に任せる。
    private readonly List<TestDirectory> _tempDirs = [];

    public FolderOpenerAdversarialTests()
    {
        FolderOpener.DryRun = true;
    }

    private string CreateTempDir()
    {
        var temp = TestDirectory.Create("Lhamiel_test");
        _tempDirs.Add(temp);
        return temp.Path;
    }

    public void Dispose()
    {
        FolderOpener.DryRun = false;
        foreach (var temp in _tempDirs)
            temp.Dispose();
    }

    // =====================================================================
    // 🗡️ カテゴリ1: 境界値・極端入力（Boundary Assault）
    // =====================================================================

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// パストラバーサル文字列（../）を含むパスでクラッシュしないこと
    /// </summary>
    [Theory]
    [InlineData(@"..\..\..\Windows\System32")]
    [InlineData(@"..\..\")]
    [InlineData(@"folder\..\..\..\etc")]
    [InlineData(@"..")]
    public void OpenFolder_WithPathTraversal_DoesNotThrow(string path)
    {
        var exception = Record.Exception(() => FolderOpener.OpenFolder(path));
        Assert.Null(exception);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// Windows予約デバイス名でクラッシュしないこと
    /// </summary>
    [Theory]
    [InlineData("CON")]
    [InlineData("NUL")]
    [InlineData("PRN")]
    [InlineData("COM1")]
    [InlineData("LPT1")]
    [InlineData("AUX")]
    public void OpenFolder_WithWindowsReservedNames_DoesNotThrow(string name)
    {
        var exception = Record.Exception(() => FolderOpener.OpenFolder(name));
        Assert.Null(exception);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// Unicode特殊文字（日本語、絵文字、ゼロ幅文字、RTL制御文字）を含むパスでクラッシュしないこと
    /// </summary>
    [Theory]
    [InlineData(@"C:\テスト\展開先")]
    [InlineData(@"C:\folder\名前\サブ")]
    [InlineData("C:\\folder\\name\u200B")]       // ゼロ幅スペース
    [InlineData("C:\\folder\\\u202Ename")]       // RTL override
    [InlineData("C:\\folder\\emoji\U0001F4C1")]  // 📁 絵文字
    public void OpenFolder_WithUnicodePaths_DoesNotThrow(string path)
    {
        var exception = Record.Exception(() => FolderOpener.OpenFolder(path));
        Assert.Null(exception);
    }

    /// <summary>
    /// @adversarial @category boundary @severity high
    /// 260文字を超える長いパスでクラッシュしないこと（MAX_PATH制限）
    /// </summary>
    [Fact]
    public void OpenFolder_WithVeryLongPath_DoesNotThrow()
    {
        // 300文字超のパスを作成
        var longSegment = new string('a', 200);
        var longPath = Path.Combine(@"C:\", longSegment, longSegment);

        var exception = Record.Exception(() => FolderOpener.OpenFolder(longPath));
        Assert.Null(exception);
    }

    /// <summary>
    /// @adversarial @category boundary @severity low
    /// 末尾にスペースやドットを含むパスでクラッシュしないこと
    /// （Windowsはディレクトリ名末尾のスペース/ドットを暗黙的に除去する）
    /// </summary>
    [Theory]
    [InlineData(@"C:\folder\test ")]
    [InlineData(@"C:\folder\test.")]
    [InlineData(@"C:\folder\test...")]
    [InlineData(@"C:\folder\test . . ")]
    public void OpenFolder_WithTrailingSpacesOrDots_DoesNotThrow(string path)
    {
        var exception = Record.Exception(() => FolderOpener.OpenFolder(path));
        Assert.Null(exception);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// 特殊文字（&amp;, %, $, ^, #）を含むパスでクラッシュしないこと
    /// </summary>
    [Theory]
    [InlineData(@"C:\folder\test & folder")]
    [InlineData(@"C:\folder\100% done")]
    [InlineData(@"C:\folder\$variable")]
    [InlineData(@"C:\folder\test^2")]
    [InlineData(@"C:\folder\C# project")]
    [InlineData(@"C:\folder\file (1)")]
    [InlineData(@"C:\folder\[brackets]")]
    public void OpenFolder_WithSpecialCharacters_DoesNotThrow(string path)
    {
        var exception = Record.Exception(() => FolderOpener.OpenFolder(path));
        Assert.Null(exception);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// UNCパス形式でクラッシュしないこと
    /// </summary>
    [Theory]
    [InlineData(@"\\server\share\folder")]
    [InlineData(@"\\?\C:\very\long\path")]
    [InlineData(@"\\.\COM1")]
    public void OpenFolder_WithUncPaths_DoesNotThrow(string path)
    {
        var exception = Record.Exception(() => FolderOpener.OpenFolder(path));
        Assert.Null(exception);
    }

    // =====================================================================
    // 🔀 カテゴリ4: 状態遷移の矛盾（State Machine Abuse）
    // =====================================================================

    /// <summary>
    /// @adversarial @category state @severity high
    /// ファイル（ディレクトリではない）のパスを渡した場合、開かないこと
    /// </summary>
    [Fact]
    public async Task OpenExtractionResultAsync_WithFilePath_DoesNotOpenOrThrow()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // ファイルは存在するがディレクトリではない → Directory.Exists = false
            Assert.True(File.Exists(tempFile));
            var exception = await Record.ExceptionAsync(() =>
                FolderOpener.OpenExtractionResultAsync(tempFile, structureInfo: null, createArchiveNameFolder: true));
            Assert.Null(exception);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// @adversarial @category state @severity medium
    /// OpenExtractionResultAsync に null を渡してもクラッシュしないこと
    /// </summary>
    [Fact]
    public async Task OpenExtractionResultAsync_WithNull_DoesNotThrow()
    {
        var exception = await Record.ExceptionAsync(() =>
            FolderOpener.OpenExtractionResultAsync(null!, structureInfo: null, createArchiveNameFolder: true));
        Assert.Null(exception);
    }

    /// <summary>
    /// @adversarial @category state @severity medium
    /// OpenExtractionResultAsync に空文字列を渡してもクラッシュしないこと
    /// </summary>
    [Fact]
    public async Task OpenExtractionResultAsync_WithEmptyString_DoesNotThrow()
    {
        var exception = await Record.ExceptionAsync(() =>
            FolderOpener.OpenExtractionResultAsync("", structureInfo: null, createArchiveNameFolder: true));
        Assert.Null(exception);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// OpenExtractionResultAsync が実在するフォルダに対してクラッシュしないこと
    /// （DryRun のため実起動はせず、フォルダ決定と事前チェックの経路だけを通す）
    /// </summary>
    [Fact]
    public async Task OpenExtractionResultAsync_WithExistingTempDir_DoesNotThrow()
    {
        var tempDir = CreateTempDir();
        var exception = await Record.ExceptionAsync(() =>
            FolderOpener.OpenExtractionResultAsync(tempDir, structureInfo: null, createArchiveNameFolder: true));
        Assert.Null(exception);
    }

    // =====================================================================
    // 🎭 カテゴリ5: 型パンチ・プロトコル違反（Type Punching）
    // =====================================================================

    /// <summary>
    /// @adversarial @category type @severity medium
    /// 制御文字を含むパスでクラッシュしないこと
    /// </summary>
    [Theory]
    [InlineData("C:\\folder\\\0hidden")]     // ヌルバイト
    [InlineData("C:\\folder\\test\ttab")]     // タブ
    [InlineData("C:\\folder\\test\nnewline")] // 改行
    [InlineData("C:\\folder\\test\r\nCRLF")]  // CRLF
    public void OpenFolder_WithControlCharacters_DoesNotThrow(string path)
    {
        var exception = Record.Exception(() => FolderOpener.OpenFolder(path));
        Assert.Null(exception);
    }

    /// <summary>
    /// @adversarial @category type @severity low
    /// explorer.exeの引数インジェクション風文字列でクラッシュしないこと
    /// </summary>
    [Theory]
    [InlineData("/select,C:\\Windows\\notepad.exe")]
    [InlineData("/root,C:\\")]
    [InlineData("/e,C:\\Users")]
    public void OpenFolder_WithExplorerCommandArgs_DoesNotThrow(string path)
    {
        // これらはexplorer.exeのコマンドライン引数形式だが、
        // Directory.Existsチェックで弾かれるため実際には実行されない
        var exception = Record.Exception(() => FolderOpener.OpenFolder(path));
        Assert.Null(exception);
    }
}
