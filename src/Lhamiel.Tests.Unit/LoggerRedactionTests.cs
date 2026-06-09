using System;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// <see cref="Logger.ApplyRedaction"/> + <see cref="Logger.RegisterRedactionToken"/> の
/// 動作を直接検証するテスト。
/// CodeRabbit / codex round 6 adversarial で発見した「同長 overlapping token」シナリオを
/// regression として固める。
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

    [Fact]
    public void ApplyRedaction_ShortToken_IsAlsoMasked()
    {
        // codex #3381905948: 1〜3 文字も登録されたらマスクする
        Register("ab");
        Assert.Equal("=*** =", Logger.ApplyRedaction("=ab ="));
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
        // 隣接した 2 token もまとめて 1 つの *** に圧縮される。
        Register("foo");
        Register("bar");
        Assert.Equal("***", Logger.ApplyRedaction("foobar"));
    }

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
}
