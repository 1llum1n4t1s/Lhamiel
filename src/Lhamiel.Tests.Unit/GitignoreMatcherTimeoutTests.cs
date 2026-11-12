using System.Text.RegularExpressions;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// <see cref="GitignoreMatcher"/> の正規表現タイムアウト（ReDoS 防御）時の倒し方を検証する。
/// <para>
/// 方針: タイムアウトは「除外側」に倒す。除外ルールは一致扱い（除外）、否定（再包含）ルールは不一致扱い
/// （再包含しない）。flat 経路・traversalMode（DFS 枝刈り）の両方で同じ向きになることを確認する。
/// </para>
/// <para>
/// 実際の 100ms タイムアウトは環境依存で不安定なため、<see cref="GitignoreMatcher.MatchOverrideForTests"/>
/// で特定パターンの照合だけ <see cref="RegexMatchTimeoutException"/> を決定的に投げさせる。
/// フックは <see cref="AsyncLocal{T}"/> なので並列実行される他テストには影響しない。
/// </para>
/// </summary>
public sealed class GitignoreMatcherTimeoutTests
{
    /// <summary>
    /// 正規表現文字列に <paramref name="marker"/> を含むルールの照合だけタイムアウトさせ、
    /// それ以外は通常どおり照合するフックを設定して <paramref name="body"/> を実行する。
    /// </summary>
    private static void WithTimeoutFor(string marker, Action body)
    {
        GitignoreMatcher.MatchOverrideForTests.Value = (regex, input) =>
        {
            if (regex.ToString().Contains(marker, StringComparison.Ordinal))
                throw new RegexMatchTimeoutException(input, regex.ToString(), TimeSpan.FromMilliseconds(100));
            return regex.IsMatch(input);
        };
        try
        {
            body();
        }
        finally
        {
            GitignoreMatcher.MatchOverrideForTests.Value = null;
        }
    }

    [Fact]
    public void ExcludeRule_Timeout_TreatsFileAsExcluded()
    {
        // 旧実装はタイムアウトを「不一致」にしていたため、除外したい secret.txt が黙ってアーカイブに入っていた。
        var m = GitignoreMatcher.Compile(["secret*.txt"]);

        WithTimeoutFor("secret", () =>
        {
            Assert.True(m.IsExcluded("secret.txt", isDirectory: false));
            Assert.True(m.IsExcluded("sub/secret-2.txt", isDirectory: false));
        });
    }

    [Fact]
    public void ExcludeRule_Timeout_TraversalMode_PrunesDirectoryAndExcludesFile()
    {
        var m = GitignoreMatcher.Compile(["secretdir/", "secret*.txt"]);

        WithTimeoutFor("secret", () =>
        {
            // traversalMode のディレクトリ枝刈りでも除外側（= 降りない）に倒れる。
            Assert.True(m.IsExcluded("secretdir", isDirectory: true, traversalMode: true));
            Assert.True(m.IsExcluded("secret.txt", isDirectory: false, traversalMode: true));
        });
    }

    [Fact]
    public void DirectoryOnlyExcludeRule_Timeout_ExcludesFileViaParentDirectory()
    {
        // flat 経路でファイルの親ディレクトリを directoryOnly ルールへ照合する分岐もタイムアウト時は除外する。
        var m = GitignoreMatcher.Compile(["secretdir/"]);

        WithTimeoutFor("secret", () =>
            Assert.True(m.IsExcluded("secretdir/a.txt", isDirectory: false)));
    }

    [Fact]
    public void NegatedRule_Timeout_DoesNotReinclude()
    {
        var m = GitignoreMatcher.Compile(["*.key", "!keep.key"]);

        // 対照: タイムアウトしなければ否定ルールで再包含される。
        Assert.False(m.IsExcluded("keep.key", isDirectory: false));

        WithTimeoutFor("keep", () =>
        {
            // 否定ルールのタイムアウトは不一致扱い → 先行の除外ルールが残り、ファイルは除外側。
            Assert.True(m.IsExcluded("keep.key", isDirectory: false));
            Assert.True(m.IsExcluded("keep.key", isDirectory: false, traversalMode: true));
            // タイムアウトしない除外ルールの判定は通常どおり。
            Assert.True(m.IsExcluded("other.key", isDirectory: false));
            Assert.False(m.IsExcluded("other.txt", isDirectory: false));
        });
    }

    [Fact]
    public void NegatedDirectoryRule_Timeout_TraversalMode_DoesNotReincludeDirectory()
    {
        var m = GitignoreMatcher.Compile(["*", "!keepdir/"]);

        // 対照: タイムアウトしなければディレクトリが再包含（traversable）になる。
        Assert.False(m.IsExcluded("keepdir", isDirectory: true, traversalMode: true));

        WithTimeoutFor("keepdir", () =>
        {
            Assert.True(m.IsExcluded("keepdir", isDirectory: true, traversalMode: true));
            Assert.True(m.IsExcluded("keepdir", isDirectory: true));
        });
    }

    [Fact]
    public void MatchOverride_IsClearedAfterScope()
    {
        var m = GitignoreMatcher.Compile(["secret*.txt"]);

        WithTimeoutFor("secret", () => Assert.True(m.IsExcluded("other-secret-free.bin", isDirectory: false)));

        // フック解除後は通常照合に戻る（他テストへ漏れない）。
        Assert.Null(GitignoreMatcher.MatchOverrideForTests.Value);
        Assert.False(m.IsExcluded("other-secret-free.bin", isDirectory: false));
    }

    // === タイムアウト件数の集計（ログ抑制）とフックの反映タイミング (旧 GitignoreMatcherLayeringAndTimeoutLogTests) ===

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    [InlineData(20, false)]
    [InlineData(100, true)]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    [InlineData(1_000_000_000, true)]
    public void IsPowerOfTen_MarksAggregateLogMilestones(int count, bool expected)
    {
        // 初回（1）は詳細ログ、以降は 10・100・1000… の節目だけ集計ログを出す。
        Assert.Equal(expected, GitignoreMatcher.IsPowerOfTen(count));
    }

    [Fact]
    public void RepeatedTimeouts_AreCountedPerRule_AndStayFailClosed()
    {
        var matcher = GitignoreMatcher.Compile(["secret*.txt", "*.bak"]);
        GitignoreMatcher.MatchOverrideForTests.Value = (regex, input) =>
        {
            if (regex.ToString().Contains("secret", StringComparison.Ordinal))
                throw new RegexMatchTimeoutException(input, regex.ToString(), TimeSpan.FromMilliseconds(100));
            return regex.IsMatch(input);
        };
        try
        {
            // ログを抑制しても判定（除外側に倒す）は毎回同じ。
            for (var i = 0; i < 25; i++)
                Assert.True(matcher.IsExcluded($"secret{i}.txt", isDirectory: false));
            Assert.Equal(25, matcher.RegexTimeoutCount);

            // 別のパスでも secret 規則は毎回照合されてタイムアウトし、同じ規則の件数へ加算される。
            Assert.True(matcher.IsExcluded("x.bak", isDirectory: false));
            Assert.Equal(25 + 1, matcher.RegexTimeoutCount);
        }
        finally
        {
            GitignoreMatcher.MatchOverrideForTests.Value = null;
        }
    }

    [Fact]
    public void TimeoutCount_IsSharedWithDerivedLayeredMatchers()
    {
        // フォルダーごとに CompileLayered で派生するマッチャーも規則オブジェクトを共有するので、
        // 初回詳細ログは規則ごとに 1 回で済む（件数は共有元に集計される）。
        var baseMatcher = GitignoreMatcher.Compile(["secret*.txt"]);
        var derived = GitignoreMatcher.CompileLayered(baseMatcher, [("sub", ["*.tmp"])]);
        GitignoreMatcher.MatchOverrideForTests.Value = (regex, input) =>
        {
            if (regex.ToString().Contains("secret", StringComparison.Ordinal))
                throw new RegexMatchTimeoutException(input, regex.ToString(), TimeSpan.FromMilliseconds(100));
            return regex.IsMatch(input);
        };
        try
        {
            Assert.True(baseMatcher.IsExcluded("secret.txt", isDirectory: false));
            Assert.True(derived.IsExcluded("sub/secret.txt", isDirectory: false));
            Assert.Equal(2, baseMatcher.RegexTimeoutCount);
            Assert.Equal(2, derived.RegexTimeoutCount);
        }
        finally
        {
            GitignoreMatcher.MatchOverrideForTests.Value = null;
        }
    }

    [Fact]
    public void MatchOverride_ChangedBetweenCalls_IsHonoredOnNextCall()
    {
        // フックは IsExcluded の先頭で 1 回だけ読む。呼び出しをまたいだ変更は次の呼び出しから反映される。
        var matcher = GitignoreMatcher.Compile(["*.txt"]);
        Assert.True(matcher.IsExcluded("a.txt", isDirectory: false));

        GitignoreMatcher.MatchOverrideForTests.Value = static (_, _) => false;
        try
        {
            Assert.False(matcher.IsExcluded("a.txt", isDirectory: false));
        }
        finally
        {
            GitignoreMatcher.MatchOverrideForTests.Value = null;
        }

        Assert.True(matcher.IsExcluded("a.txt", isDirectory: false));
    }
}
