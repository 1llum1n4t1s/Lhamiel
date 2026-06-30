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
    public void NegatedDirectoryRule_DoesNotPropagateToDescendantDirectories()
    {
        // Codex P2 指摘の回帰テスト: `!src/` は `src` ディレクトリ自体を traversable にするだけで、
        // `src/sub` 等の descendant ディレクトリには再包含を伝播させない (Git 仕様: 親が
        // excluded のまま descendant の re-include は効かない)。
        var m = GitignoreMatcher.CompileLayered([
            (string.Empty, new[] { "*", "!src/" }),
        ]);

        // src 自身は再包含されて traversable
        Assert.False(m.IsExcluded("src", isDirectory: true));

        // descendant ディレクトリは依然 ignored (!src/ は伝播しない)
        Assert.True(m.IsExcluded("src/sub", isDirectory: true));
        Assert.True(m.IsExcluded("src/sub/inner", isDirectory: true));

        // 配下のファイルも依然 ignored
        Assert.True(m.IsExcluded("src/main.c", isDirectory: false));
        Assert.True(m.IsExcluded("src/sub/file.c", isDirectory: false));
    }

    [Fact]
    public void PositiveCharClass_DoesNotMatchSlash()
    {
        // Codex P2 指摘の回帰テスト: positive な文字クラス `[ab/]` は gitignore の
        // FNM_PATHNAME 仕様で `/` をメンバとして扱わない。
        // Git: "foo[ab/]bar" は fooabar / foobbar にマッチするが foo/bar にはマッチしない。
        var m = GitignoreMatcher.Compile(["foo[ab/]bar"]);

        // 通常のメンバマッチ
        Assert.True(m.IsExcluded("fooabar", isDirectory: false));
        Assert.True(m.IsExcluded("foobbar", isDirectory: false));

        // 重要: '/' はメンバ扱いされず、path 区切りを跨ぐマッチは起きない
        Assert.False(m.IsExcluded("foo/bar", isDirectory: false));
    }

    [Fact]
    public void PositiveCharClass_RangeContainingSlash_DoesNotMatchSlash()
    {
        // Codex P2 #6 指摘の回帰テスト: ASCII range が '/' (0x2F) を含むケース。
        // [.-0] は ASCII 0x2E (.) 〜 0x30 (0) で間に 0x2F (/) が含まれる。
        // Git は FNM_PATHNAME で文字クラス内に '/' をマッチさせないので、
        // "foo[.-0]bar" は fooabar (NG), foo.bar / foo0bar (OK) にマッチするが
        // foo/bar にはマッチしない。
        var m = GitignoreMatcher.Compile(["foo[.-0]bar"]);

        // range の両端は OK
        Assert.True(m.IsExcluded("foo.bar", isDirectory: false));
        Assert.True(m.IsExcluded("foo0bar", isDirectory: false));
        // range の中間 (0x2F 直前の文字 / 直後の文字)
        // 注: ASCII 0x2E-0x30 の間に 0x2F だけしかないので両端のみ確認

        // 重要: '/' (0x2F) は range に物理的に含まれるが、subtraction で除外される
        Assert.False(m.IsExcluded("foo/bar", isDirectory: false));
    }

    [Fact]
    public void PositiveCharClass_NegatedRangeContainingSlash_DoesNotMatchSlash()
    {
        // negated 側の対称テスト: [!.-0] でも '/' をマッチさせない (補集合で除外)
        var m = GitignoreMatcher.Compile(["foo[!.-0]bar"]);

        // .-0 の範囲外文字はマッチ
        Assert.True(m.IsExcluded("fooXbar", isDirectory: false));
        Assert.True(m.IsExcluded("fooAbar", isDirectory: false));

        // range 内文字はマッチしない
        Assert.False(m.IsExcluded("foo.bar", isDirectory: false));
        Assert.False(m.IsExcluded("foo0bar", isDirectory: false));

        // '/' は依然マッチしない (negated でも path 区切りは跨がない)
        Assert.False(m.IsExcluded("foo/bar", isDirectory: false));
    }

    [Fact]
    public void PositiveCharClass_OnlySlash_RuleIsDiscarded()
    {
        // [/] のような '/' のみのクラスは gitignore 仕様外。Git では何にもマッチしない。
        // 実装では rule 全体を破棄して「マッチしない」扱いに倒す (安全側)。
        var m = GitignoreMatcher.Compile(["[/]"]);
        Assert.False(m.IsExcluded("foo", isDirectory: false));
        Assert.False(m.IsExcluded("a/b", isDirectory: false));
    }

    [Fact]
    public void NegatedDirectoryRule_DoesNotReIncludeChildFiles()
    {
        // Git 仕様: "!foo/" は foo ディレクトリ自体を traversable にするだけで、配下のファイルを
        // 再包含しない。allow-list ignore ("*" + "!src/" 等) で Git は src/ 配下のファイルを
        // 依然 ignored 扱いするが、旧実装は親ディレクトリマッチで child を再包含してしまっていた。
        // (Codex P2 指摘の回帰テスト)
        var m = GitignoreMatcher.Compile(["*", "!src/"]);

        // ディレクトリ src そのものは traversable (!src/ で再包含されるべき)
        Assert.False(m.IsExcluded("src", isDirectory: true));

        // 配下のファイルは依然 ignored (!src/ は file レベルの再包含をしない)
        Assert.True(m.IsExcluded("src/main.c", isDirectory: false));
        Assert.True(m.IsExcluded("src/build/obj.o", isDirectory: false));

        // 他のファイルも * によって ignored のまま
        Assert.True(m.IsExcluded("readme.txt", isDirectory: false));
    }

    [Fact]
    public void NonNegatedDirectoryRule_StillExcludesChildFiles()
    {
        // 通常の "node_modules/" は配下のファイルも除外する (Git 仕様)
        // (Codex P2 修正の回帰防止: negated 側だけ child 再包含を止めたことで、
        //  non-negated 側の child 除外が壊れていないことを保証)
        var m = GitignoreMatcher.Compile(["node_modules/"]);

        Assert.True(m.IsExcluded("node_modules", isDirectory: true));
        Assert.True(m.IsExcluded("node_modules/a.js", isDirectory: false));
        Assert.True(m.IsExcluded("src/node_modules/index.js", isDirectory: false));
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

    // === traversalMode: DFS 枝刈り併用の git 忠実な「ディレクトリ否定再包含」===
    // すべて実 git の挙動（git check-ignore で検証済）と一致することを保証する。
    // traversalMode では各エントリを「自分自身のレベル」だけで照合し、除外の推移性は
    // 呼び出し側の DFS 枝刈り（除外ディレクトリには降りない）が担保する。

    // 標準 Xcode .gitignore（github/gitignore の Swift/Xcode テンプレートと同型）。
    // これがバグ B の現場: `*.xcodeproj/*` で潰し、`!...xcshareddata/` 等で再包含する。
    private static readonly string[] XcodeGitignore =
    [
        "*.xcworkspace",
        "*.xcodeproj/*",
        "!*.xcodeproj/project.pbxproj",
        "!*.xcodeproj/xcshareddata/",
        "!*.xcodeproj/project.xcworkspace/",
        "!*.xcworkspace/contents.xcworkspacedata",
    ];

    [Fact]
    public void TraversalMode_XcodeReinclude_DescendantsOfReincludedDirectoryAreNotPruned()
    {
        // バグ B の修正検証: 否定で再包含されたディレクトリ（xcshareddata / project.xcworkspace）
        // の配下が、git と同じく救われる。DFS が降りる前提の「ディレクトリは枝刈りされない／
        // ファイルは除外されない」を per-level で確認する。
        var m = GitignoreMatcher.Compile(XcodeGitignore);

        // xcodeproj 自身は降りる
        Assert.False(m.IsExcluded("app.xcodeproj", isDirectory: true, traversalMode: true));

        // 直接ファイル否定: project.pbxproj は再包含される（従来も OK な経路）
        Assert.False(m.IsExcluded("app.xcodeproj/project.pbxproj", isDirectory: false, traversalMode: true));

        // ディレクトリ否定再包含: xcshareddata と配下 xcschemes は枝刈りされず降りる
        Assert.False(m.IsExcluded("app.xcodeproj/xcshareddata", isDirectory: true, traversalMode: true));
        Assert.False(m.IsExcluded("app.xcodeproj/xcshareddata/xcschemes", isDirectory: true, traversalMode: true));
        // ★ バグ B の本体: 再包含ディレクトリ配下のスキームファイルが含まれる
        Assert.False(m.IsExcluded("app.xcodeproj/xcshareddata/xcschemes/App.xcscheme", isDirectory: false, traversalMode: true));

        // project.xcworkspace も再包含されて降り、contents.xcworkspacedata が含まれる
        Assert.False(m.IsExcluded("app.xcodeproj/project.xcworkspace", isDirectory: true, traversalMode: true));
        Assert.False(m.IsExcluded("app.xcodeproj/project.xcworkspace/contents.xcworkspacedata", isDirectory: false, traversalMode: true));
    }

    [Fact]
    public void FlatMode_XcodeReinclude_DescendantFilesStillExcluded_DocumentsOldTransitiveBehavior()
    {
        // 対照: flat モード（既定）は従来どおり推移マッチするため、再包含ディレクトリ配下の
        // 深いファイルは依然除外される（バグ B の挙動）。traversalMode との差分を固定する。
        var m = GitignoreMatcher.Compile(XcodeGitignore);

        // 直接ファイル否定は flat でも効く
        Assert.False(m.IsExcluded("app.xcodeproj/project.pbxproj", isDirectory: false));
        // しかしディレクトリ否定経由の深いファイルは flat では `*.xcodeproj/*` の推移マッチで除外されたまま
        Assert.True(m.IsExcluded("app.xcodeproj/xcshareddata/xcschemes/App.xcscheme", isDirectory: false));
    }

    [Fact]
    public void TraversalMode_CodexP2_AllowListStillExcludesSubtree()
    {
        // 回帰防止: `*` + `!src/` の allow-list では、再包含された src の配下サブディレクトリ
        // src/sub は own-level で `*` に一致して枝刈りされ、その配下ファイルは含まれない（git と一致）。
        var m = GitignoreMatcher.Compile(["*", "!src/"]);

        // src 自身は再包含されて降りる
        Assert.False(m.IsExcluded("src", isDirectory: true, traversalMode: true));
        // src 直下のファイルは `*` で除外（再包含は dir のみ・file には及ばない）
        Assert.True(m.IsExcluded("src/keep.txt", isDirectory: false, traversalMode: true));
        // ★ Codex P2 の核: src/sub は枝刈りされる（配下 file は DFS で到達せず除外）
        Assert.True(m.IsExcluded("src/sub", isDirectory: true, traversalMode: true));
        // トップレベルの他ファイルも除外
        Assert.True(m.IsExcluded("top.txt", isDirectory: false, traversalMode: true));
    }

    [Fact]
    public void TraversalMode_DirContentsGlob_vs_DirItself_Distinction()
    {
        // git の重要な区別:
        //  `d/*` は d 自体に一致しない → d に降りられる → `!d/sub/` で sub 配下が救われる
        //  `d/`  は d 自体に一致する   → d ごと枝刈り       → `!d/sub/` は無効（親が除外）
        var contents = GitignoreMatcher.Compile(["d/*", "!d/sub/"]);
        Assert.False(contents.IsExcluded("d", isDirectory: true, traversalMode: true));            // d は降りる
        Assert.True(contents.IsExcluded("d/a.txt", isDirectory: false, traversalMode: true));      // 直下ファイルは除外
        Assert.False(contents.IsExcluded("d/sub", isDirectory: true, traversalMode: true));        // sub は再包含
        Assert.False(contents.IsExcluded("d/sub/b.txt", isDirectory: false, traversalMode: true)); // sub 配下は救われる

        var dirItself = GitignoreMatcher.Compile(["d/", "!d/sub/"]);
        // d 自体が枝刈りされる → 配下は到達不能（git も d/sub/b.txt を ignore）
        Assert.True(dirItself.IsExcluded("d", isDirectory: true, traversalMode: true));
    }

    [Fact]
    public void TraversalMode_GlobstarReinclude_DescendantFilesStillExcluded()
    {
        // git: `foo/**` + `!foo/keep/` では、keep ディレクトリは再包含されるが `foo/**` は
        // `/` を跨ぐので foo/keep/k.txt 自体に一致し続け、ファイルは依然 ignore。
        var m = GitignoreMatcher.Compile(["foo/**", "!foo/keep/"]);

        Assert.False(m.IsExcluded("foo", isDirectory: true, traversalMode: true));        // foo は降りる
        Assert.False(m.IsExcluded("foo/keep", isDirectory: true, traversalMode: true));   // keep は再包含されて降りる
        Assert.True(m.IsExcluded("foo/keep/k.txt", isDirectory: false, traversalMode: true)); // だが配下 file は globstar で除外
        Assert.True(m.IsExcluded("foo/other", isDirectory: true, traversalMode: true));   // other は枝刈り
    }

    [Fact]
    public void TraversalMode_PlainDirectoryRule_PrunesDirectory()
    {
        // 通常の `node_modules/` は traversalMode でもディレクトリ自体に一致して枝刈りされる
        // （配下ファイルは DFS が到達せず除外）。回帰防止。
        var m = GitignoreMatcher.Compile(["node_modules/"]);
        Assert.True(m.IsExcluded("node_modules", isDirectory: true, traversalMode: true));
        Assert.True(m.IsExcluded("a/node_modules", isDirectory: true, traversalMode: true));
        // ファイル名が node_modules でも directoryOnly なので（traversal でも）一致しない
        Assert.False(m.IsExcluded("node_modules", isDirectory: false, traversalMode: true));
    }

    [Fact]
    public void CharClass_EscapedClosingBracketDoesNotPrematurelyEndClass()
    {
        // gitignore 仕様: 文字クラス内の `\]` はメンバの ']' であって終端ではない。
        // 旧実装は pattern.IndexOf(']') が `\]` を終端と誤認して classBody が末尾バックスラッシュ
        // になり、不正な subtraction regex を生成 → 何にもマッチしない fail-open 除外漏れだった。
        // パターン `[a\]b]` は a / ] / b のいずれかのファイル名にマッチすべき。
        var m = GitignoreMatcher.Compile([@"[a\]b]"]);
        Assert.True(m.HasRules);
        Assert.True(m.IsExcluded("a", isDirectory: false));
        Assert.True(m.IsExcluded("]", isDirectory: false));
        Assert.True(m.IsExcluded("b", isDirectory: false));
        Assert.False(m.IsExcluded("c", isDirectory: false));
    }

    [Fact]
    public void CharClass_EvenBackslashRun_BeforeTrailingDash_IsEscapedCorrectly()
    {
        // 旧 EscapeTrailingDash は classBody[^2] == '\\' だけを見て「既に \- でエスケープ済み」と
        // 判定していたため、`\\-` (バックスラッシュ偶数連続 + 裸ダッシュ) を誤判定して
        // subtraction 連結時に降順レンジ扱いの不正 regex を生成 → ArgumentException → ルール破棄
        // → fail-open 除外漏れになっていた。連続 '\' の偶奇で正しく判定されるべき。
        // ここでは `[\\-]` (= リテラル '\' またはリテラル '-' を含むクラス) が破棄されないことを担保。
        var m = GitignoreMatcher.Compile([@"[\\-]"]);
        Assert.True(m.HasRules);
        Assert.True(m.IsExcluded("-", isDirectory: false));
    }
}
