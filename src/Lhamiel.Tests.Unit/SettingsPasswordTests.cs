using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// <see cref="Settings"/> のパスワード保護関連フィールド
/// (<see cref="Settings.IsPasswordProtectionEnabled"/> /
/// <see cref="Settings.PasswordMode"/> /
/// <see cref="Settings.EncryptedCompressionPassword"/>) に対する
/// <see cref="Settings.SanitizeAfterLoad"/> と <see cref="Settings.TryRecoverFromJsonDocument"/> のテスト。
/// </summary>
public class SettingsPasswordTests
{
    [Fact]
    public void SanitizeAfterLoad_UnknownPasswordMode_DowngradesToPromptEachTime()
    {
        var s = new Settings { PasswordMode = "GarbageMode" };
        s.SanitizeAfterLoad();
        Assert.Equal("PromptEachTime", s.PasswordMode);
    }

    [Theory]
    [InlineData("PromptEachTime")]
    [InlineData("Remember")]
    public void SanitizeAfterLoad_ValidPasswordMode_Preserved(string mode)
    {
        // Remember + ciphertext あり、で degrade が起きないように ciphertext を仕込む
        var s = new Settings
        {
            PasswordMode = mode,
            IsPasswordProtectionEnabled = true,
            EncryptedCompressionPassword = mode == "Remember" ? new byte[] { 0x01, 0x02 } : null,
        };
        s.SanitizeAfterLoad();
        Assert.Equal(mode, s.PasswordMode);
    }

    [Theory]
    [InlineData("REMEMBER")]
    [InlineData("prompteachtime")]
    public void SanitizeAfterLoad_PasswordModeCaseInsensitive_Normalized(string input)
    {
        var s = new Settings
        {
            PasswordMode = input,
            IsPasswordProtectionEnabled = true,
            EncryptedCompressionPassword = string.Equals(input, "REMEMBER", StringComparison.OrdinalIgnoreCase)
                ? new byte[] { 0x01 } : null,
        };
        s.SanitizeAfterLoad();
        Assert.Contains(s.PasswordMode, Settings.SupportedPasswordModes);
    }

    [Fact]
    public void SanitizeAfterLoad_OverlongCiphertext_Discarded()
    {
        var s = new Settings { EncryptedCompressionPassword = new byte[5000] };
        s.SanitizeAfterLoad();
        Assert.Null(s.EncryptedCompressionPassword);
    }

    [Fact]
    public void SanitizeAfterLoad_BoundaryCiphertext4096_Preserved()
    {
        var s = new Settings { EncryptedCompressionPassword = new byte[4096] };
        s.SanitizeAfterLoad();
        // 境界値 4096 はちょうど上限なので破棄されない
        Assert.NotNull(s.EncryptedCompressionPassword);
        Assert.Equal(4096, s.EncryptedCompressionPassword!.Length);
    }

    [Fact]
    public void SanitizeAfterLoad_RememberWithoutCiphertext_DegradesToPromptEachTime()
    {
        var s = new Settings
        {
            IsPasswordProtectionEnabled = true,
            PasswordMode = "Remember",
            EncryptedCompressionPassword = null,
        };
        s.SanitizeAfterLoad();
        Assert.Equal("PromptEachTime", s.PasswordMode);
    }

    [Fact]
    public void SanitizeAfterLoad_RememberWithEmptyCiphertext_DegradesToPromptEachTime()
    {
        var s = new Settings
        {
            IsPasswordProtectionEnabled = true,
            PasswordMode = "Remember",
            EncryptedCompressionPassword = [],
        };
        s.SanitizeAfterLoad();
        Assert.Equal("PromptEachTime", s.PasswordMode);
    }

    [Fact]
    public void SanitizeAfterLoad_RememberWithCiphertextButProtectionOff_DoesNotDegrade()
    {
        // 保護 OFF だと PasswordMode=Remember のまま保持して問題ない (使われないので)
        var s = new Settings
        {
            IsPasswordProtectionEnabled = false,
            PasswordMode = "Remember",
            EncryptedCompressionPassword = null,
        };
        s.SanitizeAfterLoad();
        Assert.Equal("Remember", s.PasswordMode);
    }

    [Fact]
    public void ResetToDefaults_PasswordFieldsAreReset()
    {
        var s = new Settings
        {
            IsPasswordProtectionEnabled = true,
            PasswordMode = "Remember",
            EncryptedCompressionPassword = new byte[] { 0x01, 0x02, 0x03 },
        };
        s.ResetToDefaults();
        Assert.False(s.IsPasswordProtectionEnabled);
        Assert.Equal("PromptEachTime", s.PasswordMode);
        Assert.Null(s.EncryptedCompressionPassword);
    }

    [Fact]
    public void Snapshot_CiphertextIsSharedNotCloned()
    {
        // wholesale-replace 規約のため byte[] は参照共有で OK (Array.Clear/in-place mutation 禁止)。
        // Snapshot した byte[] と元の byte[] が同一参照であることを確認 (= 浅いコピー)。
        var original = new byte[] { 0x10, 0x20 };
        var s = new Settings { EncryptedCompressionPassword = original };
        var snap = s.Snapshot();
        Assert.Same(original, snap.EncryptedCompressionPassword);
    }

    [Fact]
    public void EncryptFileNames_DefaultIsTrue()
    {
        var s = new Settings();
        Assert.True(s.EncryptFileNames);
    }
}
