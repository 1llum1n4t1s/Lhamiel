using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// <see cref="Logger.ApplyRedaction"/> + <see cref="Logger.RegisterRedactionToken"/> の
/// 通常ケースのテスト。
/// 境界値・overlapping token 系の嫌がらせテストは <see cref="LoggerRedactionAdversarialTests"/> を参照。
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

    [Fact]
    public void ApplyRedaction_ShortToken_IsNotMasked()
    {
        // CodeRabbit #3382682610 / CLAUDE.md 契約: 4 文字未満の token は登録されない (no-op)。
        // 短い token を masking すると、その文字を含む通常ログ全体が *** に潰れて
        // 障害診断が壊れるため。3 文字以下のパスワードは暗号学的に無価値で redaction 効果も乏しい。
        Register("ab");
        Register("xyz");
        Assert.Equal("=ab xyz=", Logger.ApplyRedaction("=ab xyz="));
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
