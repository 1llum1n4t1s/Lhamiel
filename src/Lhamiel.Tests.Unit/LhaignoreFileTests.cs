using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// LhaignoreFile のユニットテスト。Codex P2 (legacy literal escape) の回帰防止用。
/// </summary>
public class LhaignoreFileTests
{
    [Fact]
    public void EscapeGitignoreLiteral_PlainName_IsUnchanged()
    {
        Assert.Equal("foo.txt", LhaignoreFile.EscapeGitignoreLiteral("foo.txt"));
        Assert.Equal("README.md", LhaignoreFile.EscapeGitignoreLiteral("README.md"));
    }

    [Fact]
    public void EscapeGitignoreLiteral_Wildcards_AreEscaped()
    {
        // gitignore メタ文字 * ? はリテラルとして扱われるべき
        Assert.Equal(@"build\*", LhaignoreFile.EscapeGitignoreLiteral("build*"));
        Assert.Equal(@"file\?.txt", LhaignoreFile.EscapeGitignoreLiteral("file?.txt"));
        Assert.Equal(@"a\*b\?c", LhaignoreFile.EscapeGitignoreLiteral("a*b?c"));
    }

    [Fact]
    public void EscapeGitignoreLiteral_CharacterClassBrackets_AreEscaped()
    {
        // [1] のような character class はリテラル扱いさせる
        Assert.Equal(@"foo\[1\].txt", LhaignoreFile.EscapeGitignoreLiteral("foo[1].txt"));
        Assert.Equal(@"\[abc\]", LhaignoreFile.EscapeGitignoreLiteral("[abc]"));
    }

    [Fact]
    public void EscapeGitignoreLiteral_LeadingBang_IsEscaped()
    {
        // 先頭の '!' は gitignore で否定の意味を持つので escape
        Assert.Equal(@"\!important.txt", LhaignoreFile.EscapeGitignoreLiteral("!important.txt"));
    }

    [Fact]
    public void EscapeGitignoreLiteral_LeadingHash_IsEscaped()
    {
        // 先頭の '#' は gitignore でコメントになるので escape
        Assert.Equal(@"\#literal-hash.txt", LhaignoreFile.EscapeGitignoreLiteral("#literal-hash.txt"));
    }

    [Fact]
    public void EscapeGitignoreLiteral_NonLeadingBangOrHash_IsNotEscaped()
    {
        // 文字列の途中の '!' / '#' は gitignore で特別な意味を持たないのでそのまま
        Assert.Equal("important!", LhaignoreFile.EscapeGitignoreLiteral("important!"));
        Assert.Equal("v1.0#alpha", LhaignoreFile.EscapeGitignoreLiteral("v1.0#alpha"));
    }

    [Fact]
    public void EscapeGitignoreLiteral_Backslash_IsEscaped()
    {
        // backslash 自身もエスケープ文字なので escape
        Assert.Equal(@"path\\file", LhaignoreFile.EscapeGitignoreLiteral(@"path\file"));
    }

    [Fact]
    public void EscapeGitignoreLiteral_EmptyOrNull_Returns_Same()
    {
        Assert.Equal("", LhaignoreFile.EscapeGitignoreLiteral(""));
        Assert.Null(LhaignoreFile.EscapeGitignoreLiteral(null!));
    }
}
