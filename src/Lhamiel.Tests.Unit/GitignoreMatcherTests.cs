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
    public void MiddleDoubleStar_DoesNotMergePathComponents()
    {
        // gitignore 仕様: "foo/**/bar" は path 区切りを跨ぐが、隣接 component をマージしない。
        // "foo/bar" / "foo/x/bar" にはマッチするが、区切りを失った "foobar" にはマッチしない。
        // (Codex P2 #5 指摘の回帰テスト)
        var m = GitignoreMatcher.Compile(["foo/**/bar"]);

        // 正常マッチ
        Assert.True(m.IsExcluded("foo/bar", false));        // 0 directories (** は 0 個 OK)
        Assert.True(m.IsExcluded("foo/x/bar", false));      // 1 directory
        Assert.True(m.IsExcluded("foo/a/b/c/bar", false));  // 深いネスト

        // 重要: 区切りを失った root file はマッチさせない
        Assert.False(m.IsExcluded("foobar", false));        // path 区切りなし
        Assert.False(m.IsExcluded("foobar.txt", false));    // 区切りなし + 拡張子
        Assert.False(m.IsExcluded("xfoo/bar", false));      // foo の前に余計な文字
        Assert.False(m.IsExcluded("foo/barx", false));      // bar の後に余計な文字
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

    [Fact]
    public void DirectoryOnlyPattern_MatchesFileUnderIgnoredDirectory()
    {
        // git の挙動: "node_modules/" は配下のファイル "node_modules/a.js" も除外する。
        // (Codex P2 指摘の回帰テスト)
        var m = GitignoreMatcher.Compile(["node_modules/"]);
        Assert.True(m.IsExcluded("node_modules", isDirectory: true));
        Assert.True(m.IsExcluded("node_modules/a.js", isDirectory: false));
        Assert.True(m.IsExcluded("src/node_modules/index.js", isDirectory: false));
        // ファイル名が "node_modules" だが directoryOnly なのでマッチしない
        Assert.False(m.IsExcluded("node_modules", isDirectory: false));
    }

    [Fact]
    public void DirectoryOnlyPattern_SingleFileMode_MatchesParentSegment()
    {
        // 単一ファイル指定（rootDir なし）で C:\...\node_modules\a.js を渡しても、
        // 親セグメント "node_modules" が directoryOnly ルールにマッチして除外される。
        var m = GitignoreMatcher.Compile(["node_modules/"]);
        Assert.True(m.IsExcluded("C:/foo/node_modules/a.js", isDirectory: false, singleFileMode: true));
    }

    // === Codex P2 #3 回帰テスト: ** は gitignore 仕様の境界形式のみ globstar として扱う ===

    [Fact]
    public void EmbeddedDoubleStar_DoesNotSpanPathSeparator()
    {
        // gitignore 仕様: "ab**cd" のようにパス区切りを伴わない ** は通常の連続 * として扱う。
        // すなわち [^/]* と等価で、パス区切り '/' は跨がない。
        // (Codex P2 #3 指摘の回帰テスト)
        var m = GitignoreMatcher.Compile(["ab**cd"]);

        // ファイル名内のワイルドカードとしては機能する
        Assert.True(m.IsExcluded("abcd", false));
        Assert.True(m.IsExcluded("abxcd", false));
        Assert.True(m.IsExcluded("abxxxcd", false));

        // 重要: パス区切りを跨ぐマッチは Git の挙動と乖離するため、起きてはならない
        Assert.False(m.IsExcluded("ab/cd", false));
        Assert.False(m.IsExcluded("ab/xx/cd", false));
    }

    [Fact]
    public void EmbeddedDoubleStarWithLeadingSlash_DoesNotSpanPathSeparator()
    {
        // 先頭にアンカー "/" を持っていても、body 内の ** が境界 (/) を持たないなら通常扱い。
        var m = GitignoreMatcher.Compile(["/ab**cd"]);

        Assert.True(m.IsExcluded("abxcd", false));
        Assert.False(m.IsExcluded("ab/cd", false));
        // アンカードなので深い位置のファイルにはマッチしない
        Assert.False(m.IsExcluded("sub/abcd", false));
    }

    [Fact]
    public void EmbeddedDoubleStarBetweenSegments_DoesNotSpanPathSeparator()
    {
        // "foo**bar" のように segment 内に挟まる ** も通常扱い (Git 仕様)
        var m = GitignoreMatcher.Compile(["foo**bar"]);

        Assert.True(m.IsExcluded("foobar", false));
        Assert.True(m.IsExcluded("fooxxbar", false));
        Assert.False(m.IsExcluded("foo/bar", false));
        Assert.False(m.IsExcluded("foo/xx/bar", false));
    }

    [Fact]
    public void TrailingEmbeddedDoubleStar_IsTreatedAsRegularStar()
    {
        // "foo**" は末尾 "/**" 形式ではない（** 直前が '/' でない）ので、通常の連続 * = 単一の * として扱う。
        // すなわち "foo**" ≡ "foo*" の挙動になり、Git の `foo*` 仕様と一致する:
        //   - "foobar" 等の basename match: ✅
        //   - "foo" 単体: ✅
        //   - "foo/bar": ディレクトリ "foo" として match → 配下も波及（Git の foo* の挙動と同じ）
        // Codex P2 #3 指摘の核は「path 区切りを跨ぐ ab**cd の directory semantics 強制」回避なので、
        // このケース（segment 内 wildcard としての展開）は意図通り。
        var m = GitignoreMatcher.Compile(["foo**"]);

        Assert.True(m.IsExcluded("foobar", false));
        Assert.True(m.IsExcluded("foo", false));
    }

    [Fact]
    public void LeadingEmbeddedDoubleStar_IsTreatedAsRegularStar()
    {
        // "**foo" は先頭 "**/" 形式ではない（** 直後が '/' でない）ので、通常の連続 * = 単一の * として扱う。
        // すなわち "**foo" ≡ "*foo" の挙動になり、Git の `*foo` 仕様（unrooted basename match）と一致する。
        var m = GitignoreMatcher.Compile(["**foo"]);

        Assert.True(m.IsExcluded("foo", false));
        Assert.True(m.IsExcluded("xxfoo", false));
        // anchored=false なので任意深さでも basename "foo" 系にマッチする（Git の `*foo` と同じ挙動）。
        // ここで重要なのは「** が directory globstar として強制扱いされない」ことであって、
        // unrooted shell glob としての basename match までは止めない。
    }

    // === Codex P2 #4 回帰テスト: ネゲート文字クラス [!...] を gitignore 仕様準拠 ===

    [Fact]
    public void NegatedCharClass_ExcludesMembers()
    {
        // gitignore 仕様: "[!a].tmp" は「a 以外の単一文字 + .tmp」を除外する。
        // 旧実装は [!a] を .NET regex の [!a] にそのまま転写していたため、
        // ! と a がメンバとして扱われ、結果が逆転していた (Codex P2 #4 指摘)。
        var m = GitignoreMatcher.Compile(["[!a].tmp"]);

        // a 以外の単一文字 + .tmp はマッチする（'!' も a ではないのでマッチ ← Codex 指摘の核心）
        Assert.True(m.IsExcluded("b.tmp", false));
        Assert.True(m.IsExcluded("c.tmp", false));
        Assert.True(m.IsExcluded("x.tmp", false));
        Assert.True(m.IsExcluded("!.tmp", false));

        // a 自体はメンバなのでマッチしない
        Assert.False(m.IsExcluded("a.tmp", false));
    }

    [Fact]
    public void NegatedCharClass_DoesNotMatchPathSeparator()
    {
        // gitignore (POSIX fnmatch) の文字クラスは暗黙に '/' を除外する。
        // [^.../] に '/' を追加する変換でないと、`foo[!a]bar` が `foo/bar` にマッチして
        // path 区切りを跨ぐ誤マッチになる。
        var m = GitignoreMatcher.Compile(["foo[!a]bar"]);

        // segment 内の単一文字置換はマッチする
        Assert.True(m.IsExcluded("fooxbar", false));
        Assert.True(m.IsExcluded("foobbar", false));

        // path 区切りには到達しない
        Assert.False(m.IsExcluded("foo/bar", false));
        // a はメンバなのでマッチしない
        Assert.False(m.IsExcluded("fooabar", false));
    }

    [Fact]
    public void NegatedCharClassMultipleChars_ExcludesAllMembers()
    {
        // [!abc] は a, b, c 以外の単一文字にマッチ
        var m = GitignoreMatcher.Compile(["[!abc].log"]);

        Assert.True(m.IsExcluded("d.log", false));
        Assert.True(m.IsExcluded("z.log", false));
        Assert.False(m.IsExcluded("a.log", false));
        Assert.False(m.IsExcluded("b.log", false));
        Assert.False(m.IsExcluded("c.log", false));
    }

    [Fact]
    public void NonNegatedCharClass_StillWorks()
    {
        // 通常の [abc] は変換されない（従来通り）
        var m = GitignoreMatcher.Compile(["[abc].log"]);

        Assert.True(m.IsExcluded("a.log", false));
        Assert.True(m.IsExcluded("b.log", false));
        Assert.True(m.IsExcluded("c.log", false));
        Assert.False(m.IsExcluded("d.log", false));
        Assert.False(m.IsExcluded("!.log", false));  // ! はメンバではない
    }

    [Fact]
    public void ValidGlobstarForms_StillWorkAsBefore()
    {
        // 修正後も以下の gitignore 仕様の valid globstar 形式は従来通り動作することを保証する。

        // (1) 先頭の **/ : 任意深さの foo にマッチ
        var leading = GitignoreMatcher.Compile(["**/foo"]);
        Assert.True(leading.IsExcluded("foo", false));
        Assert.True(leading.IsExcluded("a/foo", false));
        Assert.True(leading.IsExcluded("a/b/c/foo", false));

        // (2) 末尾の /** : foo 配下の全てにマッチ（ParseLine で endsWithDoubleStar 経由）
        var trailing = GitignoreMatcher.Compile(["foo/**"]);
        Assert.True(trailing.IsExcluded("foo/bar", false));
        Assert.True(trailing.IsExcluded("foo/sub/bar", false));
        Assert.False(trailing.IsExcluded("foo", false));    // foo 単体ファイルは除外しない

        // (3) 中間の /**/ : 任意セグメント数を跨ぐ
        var middle = GitignoreMatcher.Compile(["foo/**/bar"]);
        Assert.True(middle.IsExcluded("foo/bar", false));
        Assert.True(middle.IsExcluded("foo/x/bar", false));
        Assert.True(middle.IsExcluded("foo/x/y/z/bar", false));
    }
}
