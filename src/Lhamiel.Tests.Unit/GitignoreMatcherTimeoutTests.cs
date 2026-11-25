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

    // === タイムアウト件数の集計（ログ抑制）(旧 GitignoreMatcherLayeringAndTimeoutLogTests) ===

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

}
