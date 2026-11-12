using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// <see cref="Logger.ApplyRedaction"/> + <see cref="Logger.RegisterRedactionToken"/> のテスト。
/// 通常ケースと、境界値・overlapping token・自己 overlap の嫌がらせケース（旧
/// LoggerRedactionAdversarialTests）を 1 クラスに集約している。
/// static な redaction token 集合を共有するため、同一 Collection で直列実行する。
/// </summary>
[Collection("LoggerRedaction")]
public sealed class LoggerRedactionTests : IDisposable
{
    // 各テスト後に外部スコープへ token を漏らさないよう、登録した IDisposable を順次解放する。
    private readonly System.Collections.Generic.List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    private IDisposable Register(string token)
    {
        var d = Logger.RegisterRedactionToken(token);
        _disposables.Add(d);
        return d;
    }

    [Fact]
    public void ApplyRedaction_NoToken_ReturnsUnchanged()
    {
        Assert.Equal("hello world", Logger.ApplyRedaction("hello world"));
    }

    [Fact]
    public void ApplyRedaction_SingleToken_MasksOccurrence()
    {
        Register("secret123");
        Assert.Equal("password=*** end", Logger.ApplyRedaction("password=secret123 end"));
    }

    // 4 文字未満の token を登録しない契約は
    // 下の RegisterRedactionToken_TokenExactly3_IsNoOp に集約。

    [Fact]
    public void RegisterRedactionToken_NullOrEmpty_IsNoOp()
    {
        // null / 空文字列は登録されない (登録されないので解除でも例外を出さない)
        var d1 = Logger.RegisterRedactionToken(null);
        var d2 = Logger.RegisterRedactionToken(string.Empty);
        Assert.NotNull(d1);
        Assert.NotNull(d2);
        d1.Dispose();
        d2.Dispose();
    }

    [Fact]
    public void RegisterRedactionToken_Refcount_PreservesUntilLastDispose()
    {
        // codex #3381085196: 同一 token を複数 scope が登録した場合、
        // 全ての scope が dispose されるまで mask が効く。
        var a = Logger.RegisterRedactionToken("dup-token");
        var b = Logger.RegisterRedactionToken("dup-token");
        try
        {
            Assert.Equal("X*** Y", Logger.ApplyRedaction("Xdup-token Y"));
            a.Dispose();
            Assert.Equal("X*** Y", Logger.ApplyRedaction("Xdup-token Y"));
        }
        finally
        {
            b.Dispose();
        }
        Assert.Equal("Xdup-token Y", Logger.ApplyRedaction("Xdup-token Y"));
    }

    // ==============================
    // 🗡️ 嫌がらせテスト（境界値・overlapping token・自己 overlap）
    // ==============================

    [Fact]
    public void RegisterRedactionToken_TokenExactly3_IsNoOp()
    {
        // CodeRabbit #3382682610 / CLAUDE.md 契約: 4 文字未満の token は登録されない (no-op)。
        // 短い token を masking すると、その文字を含む通常ログ全体が *** に潰れて
        // 障害診断が壊れるため。3 文字以下のパスワードは暗号学的に無価値で redaction 効果も乏しい。
        // 境界値: 3 文字はマスクされない。
        Register("abc");
        Assert.Equal("Xabc Y", Logger.ApplyRedaction("Xabc Y"));

        // 2 文字・3 文字の token を複数登録しても、どちらもマスクされない
        // （旧 LoggerRedactionTests.ApplyRedaction_ShortToken_IsNotMasked の入力）。
        Register("ab");
        Register("xyz");
        Assert.Equal("=ab xyz=", Logger.ApplyRedaction("=ab xyz="));
    }

    [Fact]
    public void RegisterRedactionToken_TokenExactly4_IsMasked()
    {
        // 境界値: 4 文字はマスクされる。
        Register("abcd");
        Assert.Equal("X*** Y", Logger.ApplyRedaction("Xabcd Y"));
    }

    [Fact]
    public void MinCompressPasswordLength_PasswordIsAlwaysRedactable()
    {
        // codex P2 #3384761804: PasswordDialog (CompressNew) が受理する最小長のパスワードが
        // 必ず redaction の対象になることを担保する (UI の最小長と Logger の 4 文字下限の連動契約)。
        // どちらかの定数を変えてこの連動が崩れると「マスクされない圧縮パスワード」が生まれる。
        var pw = new string('x', Lhamiel.View.PasswordDialog.MinCompressPasswordLength);
        Register(pw);
        Assert.DoesNotContain(pw, Logger.ApplyRedaction($"password={pw};"), StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyRedaction_OverlappingDescendingLength_FullyMasked()
    {
        // codex #3382065857: `abcd` を先に置換すると "***ef" になる問題が、長さ降順では解消されている。
        // (`abcdef` を先に置換 → "***")
        Register("abcd");
        Register("abcdef");
        Assert.Equal("***", Logger.ApplyRedaction("abcdef"));
    }

    [Fact]
    public void ApplyRedaction_SelfOverlappingToken_FullyMasked()
    {
        // codex #3382276697: 自己 overlap する繰り返し token (`aaaa` in `aaaaa`) は
        // advance を `+ t.Length` にすると 2 文字目から始まる出現を skip → 最後の `a` が残る。
        // `+ 1` 進める実装なら全 5 文字が mask される (token は 4 文字以上契約に合わせる)。
        Register("aaaa");
        Assert.Equal("***", Logger.ApplyRedaction("aaaaa"));
    }

    [Fact]
    public void ApplyRedaction_SameLengthOverlapping_FullyMasked()
    {
        // round 6 adversarial: `abcd` + `cdef` (どちらも 4 文字、message=`abcdef`) で
        // 単純な順次 Replace では tie-breaking が不定で "***ef" や "ab***" になりうる。
        // bool 配列ベースの非破壊マークなら全 6 文字が連続 mask → "***" 1 個に圧縮される。
        Register("abcd");
        Register("cdef");
        Assert.Equal("***", Logger.ApplyRedaction("abcdef"));
    }

    [Fact]
    public void ApplyRedaction_NonOverlappingMultipleTokens_EachMasked()
    {
        Register("alpha");
        Register("beta");
        Assert.Equal("X*** Y***", Logger.ApplyRedaction("Xalpha Ybeta"));
    }

    [Fact]
    public void ApplyRedaction_AdjacentTokens_CollapseIntoSingleStar()
    {
        // 隣接した 2 token もまとめて 1 つの *** に圧縮される (token は 4 文字以上契約に合わせる)。
        Register("food");
        Register("bark");
        Assert.Equal("***", Logger.ApplyRedaction("foodbark"));
    }
}
