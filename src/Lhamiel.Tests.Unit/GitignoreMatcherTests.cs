using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// .gitignore 互換マッチャの単体テスト。
/// gitignore 仕様の主要ケースを網羅する（コメント・否定・グロブ・アンカー・ディレクトリ限定）。
/// </summary>
public class GitignoreMatcherTests
{
    [Fact]
    public void Empty_MatchesNothing()
    {
        Assert.False(GitignoreMatcher.Empty.IsExcluded("anything", false));
        Assert.False(GitignoreMatcher.Empty.HasRules);
    }

    [Fact]
    public void Compile_BlankLinesAndComments_AreSkipped()
    {
        var m = GitignoreMatcher.Compile(["", "  ", "# comment", "\t# tab comment", "*.log"]);
        Assert.True(m.HasRules);
        Assert.True(m.IsExcluded("debug.log", false));
    }

    [Fact]
    public void ExactFilename_MatchesAnyDepth()
    {
        var m = GitignoreMatcher.Compile([".DS_Store"]);
        Assert.True(m.IsExcluded(".DS_Store", false));
        Assert.True(m.IsExcluded("a/.DS_Store", false));
        Assert.True(m.IsExcluded("a/b/c/.DS_Store", false));
        Assert.False(m.IsExcluded("readme.txt", false));
    }

    [Fact]
    public void WildcardStar_OnlyMatchesNonSlash()
    {
        var m = GitignoreMatcher.Compile(["*.log"]);
        Assert.True(m.IsExcluded("a.log", false));
        Assert.True(m.IsExcluded("path/b.log", false));
        // *.log は a/.log にもマッチする (* は 0 文字以上)
        Assert.True(m.IsExcluded("a/.log", false));
        Assert.False(m.IsExcluded("a.txt", false));
    }

    [Fact]
    public void QuestionMark_MatchesSingleChar()
    {
        var m = GitignoreMatcher.Compile(["file?.txt"]);
        Assert.True(m.IsExcluded("file1.txt", false));
        Assert.True(m.IsExcluded("fileA.txt", false));
        Assert.False(m.IsExcluded("file.txt", false));
        Assert.False(m.IsExcluded("file12.txt", false));
    }

    [Fact]
    public void DoubleStar_MatchesArbitraryDepth()
    {
        var m = GitignoreMatcher.Compile(["foo/**/bar"]);
        Assert.True(m.IsExcluded("foo/bar", false));
        Assert.True(m.IsExcluded("foo/x/bar", false));
        Assert.True(m.IsExcluded("foo/x/y/bar", false));
        Assert.False(m.IsExcluded("foo/bar.txt", false));
    }

    [Fact]
    public void LeadingSlash_AnchorsToRoot()
    {
        var m = GitignoreMatcher.Compile(["/build"]);
        Assert.True(m.IsExcluded("build", true));
        Assert.True(m.IsExcluded("build/output.txt", false));
        Assert.False(m.IsExcluded("src/build", false));
    }

    [Fact]
    public void TrailingSlash_MatchesDirectoriesOnly()
    {
        var m = GitignoreMatcher.Compile(["node_modules/"]);
        Assert.True(m.IsExcluded("node_modules", true));
        Assert.True(m.IsExcluded("a/node_modules", true));
        // ファイル名が node_modules でも directoryOnly なのでマッチしない
        Assert.False(m.IsExcluded("a/node_modules", false));
    }

    [Fact]
    public void NegationPattern_ReIncludesPreviouslyExcluded()
    {
        var m = GitignoreMatcher.Compile(["*.log", "!keep.log"]);
        Assert.True(m.IsExcluded("debug.log", false));
        Assert.False(m.IsExcluded("keep.log", false));
        Assert.False(m.IsExcluded("a/keep.log", false));
    }

    [Fact]
    public void CaseInsensitive_MatchesRegardlessOfCase()
    {
        var m = GitignoreMatcher.Compile(["thumbs.db"]);
        Assert.True(m.IsExcluded("Thumbs.db", false));
        Assert.True(m.IsExcluded("THUMBS.DB", false));
        Assert.True(m.IsExcluded("dir/Thumbs.db", false));
    }

    [Fact]
    public void EscapedHash_IsLiteral()
    {
        // \# は # で始まるファイル名にマッチするリテラルパターン
        var m = GitignoreMatcher.Compile([@"\#literal"]);
        Assert.True(m.IsExcluded("#literal", false));
    }

    [Fact]
    public void EscapedBang_IsLiteral()
    {
        var m = GitignoreMatcher.Compile([@"\!literal"]);
        Assert.True(m.IsExcluded("!literal", false));
    }

    [Fact]
    public void MiddleSlash_BehavesAsAnchored()
    {
        // gitignore 仕様: 中間に / があるパターンは root 相対アンカー扱い
        var m = GitignoreMatcher.Compile(["doc/manual.txt"]);
        Assert.True(m.IsExcluded("doc/manual.txt", false));
        Assert.False(m.IsExcluded("src/doc/manual.txt", false));
    }

    [Fact]
    public void WindowsBackslashPath_IsNormalized()
    {
        var m = GitignoreMatcher.Compile(["*.log"]);
        Assert.True(m.IsExcluded(@"src\sub\debug.log", false));
    }

    [Fact]
    public void NormalizePath_ConvertsBackslashesToSlash()
    {
        Assert.Equal("a/b/c", GitignoreMatcher.NormalizePath(@"a\b\c"));
        Assert.Equal("a/b/c", GitignoreMatcher.NormalizePath("a/b/c"));
    }

    [Fact]
    public void CharacterClass_MatchesAnyOfTheChars()
    {
        var m = GitignoreMatcher.Compile(["[Tt]humbs.db"]);
        Assert.True(m.IsExcluded("Thumbs.db", false));
        Assert.True(m.IsExcluded("thumbs.db", false));
        Assert.False(m.IsExcluded("Xhumbs.db", false));
    }

    [Fact]
    public void TrailingDoubleStar_MatchesContentsButNotParentFile()
    {
        // gitignore 仕様: "foo/**" は foo 配下にマッチするが、foo そのものというファイルにはマッチしない
        var m = GitignoreMatcher.Compile(["foo/**"]);
        Assert.True(m.IsExcluded("foo/bar", false));      // 直下ファイル
        Assert.True(m.IsExcluded("foo/sub/bar", false));  // ネスト
        Assert.False(m.IsExcluded("foo", false));         // foo ファイル自体は除外しない
        Assert.False(m.IsExcluded("notfoo/bar", false));  // 別ディレクトリ
    }

    [Fact]
    public void SingleFileMode_SkipsAnchoredPatterns()
    {
        // rootDir が無い「単一ファイル」モードでは、root 相対のアンカードパターンは無効化する。
        // 例えば "/build" は build ディレクトリのルート相対指定なので、単独ファイル "build" には適用しない。
        var m = GitignoreMatcher.Compile(["/build", "*.log"]);
        Assert.False(m.IsExcluded("build", false, singleFileMode: true));
        Assert.True(m.IsExcluded("build", false, singleFileMode: false));
        // *.log はアンカードでないので singleFileMode でもマッチする
        Assert.True(m.IsExcluded("debug.log", false, singleFileMode: true));
    }

    // === CompileLayered (.gitignore 階層対応) ===

    [Fact]
    public void Layered_PatternsScopedToBaseDirectory()
    {
        // base="src/repo" の layer は src/repo/ 配下にのみ適用される
        var m = GitignoreMatcher.CompileLayered([
            (string.Empty, new[] { "*.lha" }),                  // source root
            ("src/repo", new[] { "*.log" }),                    // src/repo/.gitignore 相当
        ]);

        // root layer は全パスにマッチ
        Assert.True(m.IsExcluded("foo.lha", false));
        Assert.True(m.IsExcluded("src/repo/foo.lha", false));

        // src/repo layer は src/repo/ 配下にのみ適用
        Assert.True(m.IsExcluded("src/repo/debug.log", false));
        Assert.True(m.IsExcluded("src/repo/sub/debug.log", false));
        Assert.False(m.IsExcluded("debug.log", false));       // 外側
        Assert.False(m.IsExcluded("src/other/debug.log", false)); // 別ディレクトリ
    }

    [Fact]
    public void Layered_DeeperLayerOverridesParent()
    {
        // 深い layer が後勝ちで否定できる（同じ matcher 内で last-wins）
        var m = GitignoreMatcher.CompileLayered([
            (string.Empty, new[] { "*.log" }),                  // 全 .log を除外
            ("src/repo", new[] { "!keep.log" }),                // src/repo 配下の keep.log だけ再包含
        ]);

        Assert.True(m.IsExcluded("debug.log", false));
        Assert.True(m.IsExcluded("src/repo/debug.log", false));
        Assert.False(m.IsExcluded("src/repo/keep.log", false));
        // 別ディレクトリの keep.log は再包含ルールが効かないので除外されたまま
        Assert.True(m.IsExcluded("src/other/keep.log", false));
    }

    [Fact]
    public void Layered_AnchoredRuleInNestedGitignore_IsLocalToBase()
    {
        // src/repo/.gitignore に書かれた "/build" は src/repo/build にだけマッチ
        var m = GitignoreMatcher.CompileLayered([
            (string.Empty, Array.Empty<string>()),
            ("src/repo", new[] { "/build" }),
        ]);

        Assert.True(m.IsExcluded("src/repo/build", true));
        Assert.True(m.IsExcluded("src/repo/build/x.o", false));
        // root の build は src/repo layer のスコープ外
        Assert.False(m.IsExcluded("build", true));
        Assert.False(m.IsExcluded("src/other/build", true));
    }

    [Fact]
    public void Layered_EmptyLayersResultsInEmptyMatcher()
    {
        var m = GitignoreMatcher.CompileLayered([]);
        Assert.False(m.HasRules);
        Assert.False(m.IsExcluded("anything", false));
    }
}
