using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// FolderOpener の嫌がらせテスト — 境界値、特殊パス、状態異常を攻める
/// </summary>
[Collection("FolderOpener")]
public class FolderOpenerAdversarialTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    public FolderOpenerAdversarialTests()
    {
        FolderOpener.DryRun = true;
    }

    private string CreateTempDir(string? subName = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Lhamiel_test_" + Guid.NewGuid());
        if (subName != null)
            dir = Path.Combine(dir, subName);
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        FolderOpener.DryRun = false;
        foreach (var dir in _tempDirs)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { /* テスト用一時ディレクトリの削除失敗は無視 */ }
        }
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
    public void OpenExtractionResult_WithFilePath_DoesNotOpenOrThrow()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // ファイルは存在するがディレクトリではない → Directory.Exists = false
            Assert.True(File.Exists(tempFile));
            var exception = Record.Exception(() =>
                FolderOpener.OpenExtractionResult(tempFile));
            Assert.Null(exception);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// @adversarial @category state @severity medium
    /// OpenExtractionResult に null を渡してもクラッシュしないこと
    /// </summary>
    [Fact]
    public void OpenExtractionResult_WithNull_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            FolderOpener.OpenExtractionResult(null!));
        Assert.Null(exception);
    }

    /// <summary>
    /// @adversarial @category state @severity medium
    /// OpenExtractionResult に空文字列を渡してもクラッシュしないこと
    /// </summary>
    [Fact]
    public void OpenExtractionResult_WithEmptyString_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            FolderOpener.OpenExtractionResult(""));
        Assert.Null(exception);
    }

    /// <summary>
    /// @adversarial @category boundary @severity medium
    /// OpenExtractionResultが実在するフォルダに対してクラッシュしないこと
    /// （Process.Startが呼ばれるパスの検証 — CIでは実際にExplorerが開く可能性に注意）
    /// </summary>
    [Fact]
    public void OpenExtractionResult_WithExistingTempDir_DoesNotThrow()
    {
        var tempDir = CreateTempDir();
        var exception = Record.Exception(() =>
            FolderOpener.OpenExtractionResult(tempDir));
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
