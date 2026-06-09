using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// <see cref="CompressionPasswordSession"/> の DPAPI ラッパに対するテスト。
/// Windows 限定の機能 (<c>ProtectedData</c> は Windows API) なので非 Windows ではスキップする。
/// </summary>
public class CompressionPasswordSessionTests
{
    [Fact]
    public void Protect_NullPlaintext_ReturnsNull()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "DPAPI は Windows 限定");
        Assert.Null(CompressionPasswordSession.Protect(null));
    }

    [Fact]
    public void Protect_EmptyPlaintext_ReturnsNull()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "DPAPI は Windows 限定");
        Assert.Null(CompressionPasswordSession.Protect(string.Empty));
    }

    [Fact]
    public void Protect_TooLongPlaintext_Throws()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "DPAPI は Windows 限定");
        var overflow = new string('a', CompressionPasswordSession.MaxPlaintextLength + 1);
        Assert.Throws<ArgumentException>(() => CompressionPasswordSession.Protect(overflow));
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("with space and symbol !@#$%^&*()_+-=")]
    [InlineData("日本語パスワード")]
    [InlineData("🔐🗝️絵文字パスワード")]
    public void ProtectThenUnprotect_RoundTrip_ReturnsSamePlaintext(string plaintext)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "DPAPI は Windows 限定");
        var ciphertext = CompressionPasswordSession.Protect(plaintext);
        Assert.NotNull(ciphertext);
        Assert.NotEmpty(ciphertext);

        var recovered = CompressionPasswordSession.TryUnprotect(ciphertext);
        Assert.Equal(plaintext, recovered);
    }

    [Fact]
    public void Protect_AtMaxLength_DoesNotThrow()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "DPAPI は Windows 限定");
        var maxLen = new string('x', CompressionPasswordSession.MaxPlaintextLength);
        var ciphertext = CompressionPasswordSession.Protect(maxLen);
        Assert.NotNull(ciphertext);

        var recovered = CompressionPasswordSession.TryUnprotect(ciphertext);
        Assert.Equal(maxLen, recovered);
    }

    [Fact]
    public void TryUnprotect_NullCiphertext_ReturnsNull()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "DPAPI は Windows 限定");
        Assert.Null(CompressionPasswordSession.TryUnprotect(null));
    }

    [Fact]
    public void TryUnprotect_EmptyCiphertext_ReturnsNull()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "DPAPI は Windows 限定");
        Assert.Null(CompressionPasswordSession.TryUnprotect([]));
    }

    [Fact]
    public void TryUnprotect_GarbageCiphertext_ReturnsNullWithoutThrowing()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "DPAPI は Windows 限定");
        // 意図的に壊れたバイト列 → CryptographicException が内部で握り潰されて null が返る
        var garbage = new byte[64];
        for (var i = 0; i < garbage.Length; i++) garbage[i] = (byte)(i * 7 + 13);

        var recovered = CompressionPasswordSession.TryUnprotect(garbage);
        Assert.Null(recovered);
    }

    [Fact]
    public void Protect_TwoCallsSamePlaintext_ProducesDifferentCiphertext()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "DPAPI は Windows 限定");
        // DPAPI は内部で entropy/salt を使うので、同じ平文でも 2 回呼ぶと異なる ciphertext が返る (replay 攻撃耐性)。
        const string pw = "samepassword";
        var c1 = CompressionPasswordSession.Protect(pw);
        var c2 = CompressionPasswordSession.Protect(pw);
        Assert.NotNull(c1);
        Assert.NotNull(c2);
        Assert.NotEqual(c1, c2);

        // ただし両方とも復号できる
        Assert.Equal(pw, CompressionPasswordSession.TryUnprotect(c1));
        Assert.Equal(pw, CompressionPasswordSession.TryUnprotect(c2));
    }
}
