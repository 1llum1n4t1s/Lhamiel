using System.Text;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// LhaignoreFile のユニットテスト。Codex P2 (legacy literal escape) の回帰防止用。
/// </summary>
public class LhaignoreFileTests
{
    /// <summary>
    /// EscapeGitignoreLiteral が gitignore のメタ文字だけをエスケープし、
    /// それ以外の文字はそのまま返すこと。
    /// 入力ごとの意図は各行のコメントを参照する。
    /// </summary>
    [Theory]
    // 通常のファイル名は変更しない
    [InlineData("foo.txt", "foo.txt")]
    [InlineData("README.md", "README.md")]
    // gitignore メタ文字 * ? はリテラルとして扱われるべき
    [InlineData("build*", @"build\*")]
    [InlineData("file?.txt", @"file\?.txt")]
    [InlineData("a*b?c", @"a\*b\?c")]
    // [1] のような character class はリテラル扱いさせる
    [InlineData("foo[1].txt", @"foo\[1\].txt")]
    [InlineData("[abc]", @"\[abc\]")]
    // 先頭の '!' は gitignore で否定の意味を持つので escape
    [InlineData("!important.txt", @"\!important.txt")]
    // 先頭の '#' は gitignore でコメントになるので escape
    [InlineData("#literal-hash.txt", @"\#literal-hash.txt")]
    // 文字列の途中の '!' / '#' は gitignore で特別な意味を持たないのでそのまま
    [InlineData("important!", "important!")]
    [InlineData("v1.0#alpha", "v1.0#alpha")]
    // backslash 自身もエスケープ文字なので escape
    [InlineData(@"path\file", @"path\\file")]
    // 空文字列と null はそのまま返す
    [InlineData("", "")]
    [InlineData(null, null)]
    public void EscapeGitignoreLiteral_EscapesOnlyGitignoreMetaCharacters(string? input, string? expected)
    {
        Assert.Equal(expected, LhaignoreFile.EscapeGitignoreLiteral(input!));
    }

    // === EnsureExistsAt（原子的な初回作成）===
    // 実ユーザーの %LocalAppData%\Lhamiel\.lhaignore に触れないよう、必ず一時ディレクトリのパスを渡す。

    [Fact]
    public void EnsureExistsAt_WhenMissing_CreatesDefaultContentWithBom()
    {
        using var temp = TestDirectory.Create("Lhamiel_lhaignore");
        var path = Path.Combine(temp.Path, "sub", ".lhaignore");

        Assert.True(LhaignoreFile.EnsureExistsAt(path, legacyPatterns: null));

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        Assert.Equal(LhaignoreFile.CreateDefaultContent(), File.ReadAllText(path, Encoding.UTF8));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public void EnsureExistsAt_WithLegacyPatterns_WritesEscapedPatterns()
    {
        using var temp = TestDirectory.Create("Lhamiel_lhaignore");
        var path = Path.Combine(temp.Path, ".lhaignore");

        Assert.True(LhaignoreFile.EnsureExistsAt(path, ["foo[1].txt", "  ", "Thumbs.db"]));

        var lines = File.ReadAllLines(path, Encoding.UTF8);
        Assert.Contains(@"foo\[1\].txt", lines);
        Assert.Contains("Thumbs.db", lines);
    }

    [Fact]
    public void EnsureExistsAt_WhenFileAlreadyExists_ReturnsFalseAndKeepsContent()
    {
        using var temp = TestDirectory.Create("Lhamiel_lhaignore");
        var path = Path.Combine(temp.Path, ".lhaignore");
        File.WriteAllText(path, "user-rule/");

        Assert.False(LhaignoreFile.EnsureExistsAt(path, ["legacy"]));

        Assert.Equal("user-rule/", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
    }

    [Fact]
    public void EnsureExistsAt_WhenParentIsFile_ReturnsFalseWithoutCreating()
    {
        using var temp = TestDirectory.Create("Lhamiel_lhaignore");
        var blocker = Path.Combine(temp.Path, "blocker");
        File.WriteAllText(blocker, "x");
        var path = Path.Combine(blocker, ".lhaignore");

        Assert.False(LhaignoreFile.EnsureExistsAt(path, legacyPatterns: null));
        Assert.False(File.Exists(path));
    }
}
