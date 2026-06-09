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
    [InlineData("REMEMBER", "Remember")]
    [InlineData("prompteachtime", "PromptEachTime")]
    public void SanitizeAfterLoad_PasswordModeCaseInsensitive_Normalized(string input, string expected)
    {
        // CodeRabbit (outside-diff Review Run 6d98e252): Assert.Contains だけだと
        // 「SupportedPasswordModes のいずれかに含まれる」 (= 大文字小文字を保ったまま OK)
        // とも誤読されうるので、SanitizeAfterLoad が「PromptEachTime / Remember の正規綴り」
        // に厳密に揃えることを Assert.Equal で明示する。
        var s = new Settings
        {
            PasswordMode = input,
            IsPasswordProtectionEnabled = true,
            EncryptedCompressionPassword = string.Equals(input, "REMEMBER", StringComparison.OrdinalIgnoreCase)
                ? new byte[] { 0x01 } : null,
        };
        s.SanitizeAfterLoad();
        Assert.Equal(expected, s.PasswordMode);
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
    public void SanitizeAfterLoad_RememberWithoutCiphertext_PreservesRemember()
    {
        // codex P2 #3381313190: Remember を選んで初回圧縮前にアプリを閉じた場合や
        // 保存パスワードを削除した直後でも、Remember 選好を保持する。
        // TryResolveCompressionPasswordAsync 側で null ciphertext を「初回プロンプト → 保存」
        // として扱うので、ここで PromptEachTime に巻き戻さない。
        var s = new Settings
        {
            IsPasswordProtectionEnabled = true,
            PasswordMode = "Remember",
            EncryptedCompressionPassword = null,
        };
        s.SanitizeAfterLoad();
        Assert.Equal("Remember", s.PasswordMode);
    }

    [Fact]
    public void SanitizeAfterLoad_RememberWithEmptyCiphertext_PreservesRemember()
    {
        // 空 byte[] も null と同じく「未保存」として扱う。Remember は保持。
        var s = new Settings
        {
            IsPasswordProtectionEnabled = true,
            PasswordMode = "Remember",
            EncryptedCompressionPassword = [],
        };
        s.SanitizeAfterLoad();
        Assert.Equal("Remember", s.PasswordMode);
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
    public void SanitizeAfterLoad_TarWithProtectionOn_DisablesProtection()
    {
        // codex P2 #3384524013: 永続層の「TAR + 保護 ON」矛盾状態は load 時に矯正する。
        // この状態が残るとシェル/CLI 圧縮 (App.axaml.cs → 永続設定の CompressionFormat=TAR) が
        // TryResolveCompressionPasswordAsync の TAR fail-loud guard で必ず失敗する。
        var s = new Settings
        {
            CompressionFormat = "TAR",
            IsPasswordProtectionEnabled = true,
            PasswordMode = "Remember",
            EncryptedCompressionPassword = new byte[] { 0x01 },
        };
        s.SanitizeAfterLoad();
        Assert.False(s.IsPasswordProtectionEnabled);
        // ZIP/7z 用の選好 (Mode / ciphertext) は保持する
        Assert.Equal("Remember", s.PasswordMode);
        Assert.NotNull(s.EncryptedCompressionPassword);
    }

    [Theory]
    [InlineData("ZIP")]
    [InlineData("7z")]
    public void SanitizeAfterLoad_NonTarWithProtectionOn_PreservesProtection(string format)
    {
        var s = new Settings
        {
            CompressionFormat = format,
            IsPasswordProtectionEnabled = true,
            PasswordMode = "PromptEachTime",
        };
        s.SanitizeAfterLoad();
        Assert.True(s.IsPasswordProtectionEnabled);
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
