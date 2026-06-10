using Avalonia.Controls;
using Lhamiel.Util;
using Lhamiel.View;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// <see cref="ArchiveProcessor.TryResolveCompressionPasswordAsync"/> のテスト。
/// internal static の <see cref="ArchiveProcessor.PasswordDialogImpl"/> /
/// <see cref="ArchiveProcessor.MessageServiceImpl"/> /
/// <see cref="ArchiveProcessor.UiDispatcherImpl"/> をスタブに差し替えて検証。
///
/// 注: Remember + ciphertext 未保存ケースは <see cref="SettingsManager"/> への副作用 (settings.json 書込)
/// を伴うため、ここではテストしない。保護 OFF / PromptEachTime / Remember + ciphertext 既存復号成功 /
/// Remember + ciphertext 既存復号失敗 (garbage) の 4 シナリオに絞る。
/// </summary>
[Collection("ArchiveProcessor")]
public class ArchiveProcessorPasswordTests : IDisposable
{
    private readonly IMessageService _origMsg;
    private readonly IUiDispatcher _origUi;
    private readonly IPasswordDialogService _origPwd;

    public ArchiveProcessorPasswordTests()
    {
        _origMsg = ArchiveProcessor.MessageServiceImpl;
        _origUi = ArchiveProcessor.UiDispatcherImpl;
        _origPwd = ArchiveProcessor.PasswordDialogImpl;
    }

    public void Dispose()
    {
        ArchiveProcessor.MessageServiceImpl = _origMsg;
        ArchiveProcessor.UiDispatcherImpl = _origUi;
        ArchiveProcessor.PasswordDialogImpl = _origPwd;
    }

    // --- スタブ ---

    private sealed class StubMsg : IMessageService
    {
        public List<string> Errors { get; } = [];
        public Task ShowError(string message, string? title = null)
        {
            Errors.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class StubUi : IUiDispatcher
    {
        public void Post(Action action) => action();
        public Task InvokeAsync(Func<Task> callback) => callback();
        public Task<T> InvokeAsync<T>(Func<Task<T>> callback) => callback();
    }

    private sealed class StubPasswordDialog : IPasswordDialogService
    {
        public string? Plaintext { get; init; }

        /// <summary>
        /// 呼び出しごとに順次返す値 (ZIP ASCII 制約の再プロンプト検証用)。
        /// 設定時は <see cref="Plaintext"/> より優先し、空になったら Plaintext にフォールバックする。
        /// </summary>
        public Queue<string?>? Sequence { get; init; }

        public List<PasswordDialogMode> Calls { get; } = [];
        public List<bool> IsRetryFlags { get; } = [];

        public Task<string?> PromptForPasswordAsync(
            string archiveDisplayName,
            PasswordDialogMode mode,
            bool isRetry,
            Window? parentWindow,
            CancellationToken cancellationToken)
        {
            Calls.Add(mode);
            IsRetryFlags.Add(isRetry);
            var result = Sequence is { Count: > 0 } ? Sequence.Dequeue() : Plaintext;
            return Task.FromResult(result);
        }
    }

    // --- テスト ---

    [Fact]
    public async Task IsPasswordProtectionEnabledFalse_ReturnsEmptyState_WithoutPrompt()
    {
        var pwdStub = new StubPasswordDialog { Plaintext = "should-not-be-asked" };
        ArchiveProcessor.PasswordDialogImpl = pwdStub;
        ArchiveProcessor.MessageServiceImpl = new StubMsg();
        ArchiveProcessor.UiDispatcherImpl = new StubUi();

        var settings = new Settings { IsPasswordProtectionEnabled = false };
        var result = await ArchiveProcessor.TryResolveCompressionPasswordAsync(
            settings, "test.zip", null, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Null(result!.Password);
        Assert.False(result.EncryptFileNames);
        Assert.Empty(pwdStub.Calls); // プロンプトは出ない
    }

    [Fact]
    public async Task TarFormatHint_WithProtectionEnabled_SkipsPassword_WithoutPrompt()
    {
        // codex P2 #3384620480: 明示 `--format TAR` (シェル/CLI) は ZIP/7z 用の保護選好が
        // ON のままでも到達する正規経路。throw すると「ZIP の保護設定を OFF にしないと
        // TAR 圧縮できない」誤爆になるため、UI ドロップ経路 (VM の強制 false) と同じく
        // 「保護なし」へ coerce する。プロンプトも出さない。
        // 「非 null password が TAR writer に届く」本物のバグは
        // ArchiveCompressor.CreateArchiveWriter の fail-loud guard が検知する
        // (TarFormat_WithPassword_Throws テスト参照)。
        var pwdStub = new StubPasswordDialog { Plaintext = "should-not-be-asked" };
        ArchiveProcessor.PasswordDialogImpl = pwdStub;
        ArchiveProcessor.MessageServiceImpl = new StubMsg();
        ArchiveProcessor.UiDispatcherImpl = new StubUi();

        var settings = new Settings
        {
            IsPasswordProtectionEnabled = true,
            PasswordMode = "PromptEachTime",
        };

        var result = await ArchiveProcessor.TryResolveCompressionPasswordAsync(
            settings, "test.tar", null, TestContext.Current.CancellationToken, "TAR");

        Assert.NotNull(result);
        Assert.Null(result!.Password);
        Assert.False(result.EncryptFileNames);
        Assert.Empty(pwdStub.Calls); // プロンプトは出ない
    }

    [Fact]
    public async Task PromptEachTime_UserEntersPassword_ReturnsResolvedState()
    {
        var pwdStub = new StubPasswordDialog { Plaintext = "mypassword" };
        ArchiveProcessor.PasswordDialogImpl = pwdStub;
        ArchiveProcessor.MessageServiceImpl = new StubMsg();
        ArchiveProcessor.UiDispatcherImpl = new StubUi();

        var settings = new Settings
        {
            IsPasswordProtectionEnabled = true,
            PasswordMode = "PromptEachTime",
            EncryptFileNames = true,
        };
        var result = await ArchiveProcessor.TryResolveCompressionPasswordAsync(
            settings, "test.7z", null, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("mypassword", result!.Password);
        Assert.True(result.EncryptFileNames);
        Assert.Single(pwdStub.Calls);
        Assert.Equal(PasswordDialogMode.CompressNew, pwdStub.Calls[0]);
    }

    [Fact]
    public async Task PromptEachTime_UserCancels_ReturnsNull()
    {
        var pwdStub = new StubPasswordDialog { Plaintext = null };
        ArchiveProcessor.PasswordDialogImpl = pwdStub;
        ArchiveProcessor.MessageServiceImpl = new StubMsg();
        ArchiveProcessor.UiDispatcherImpl = new StubUi();

        var settings = new Settings
        {
            IsPasswordProtectionEnabled = true,
            PasswordMode = "PromptEachTime",
        };
        var result = await ArchiveProcessor.TryResolveCompressionPasswordAsync(
            settings, "test.zip", null, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task Remember_ValidCiphertext_DecryptsAndReturns_WithoutPrompt()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "DPAPI は Windows 限定");

        // 事前に DPAPI で本物の ciphertext を作る
        const string original = "saved-password-42";
        var ciphertext = CompressionPasswordSession.Protect(original);
        Assert.NotNull(ciphertext);

        var pwdStub = new StubPasswordDialog { Plaintext = "SHOULD-NOT-BE-CALLED" };
        ArchiveProcessor.PasswordDialogImpl = pwdStub;
        ArchiveProcessor.MessageServiceImpl = new StubMsg();
        ArchiveProcessor.UiDispatcherImpl = new StubUi();

        var settings = new Settings
        {
            IsPasswordProtectionEnabled = true,
            PasswordMode = "Remember",
            EncryptedCompressionPassword = ciphertext,
            EncryptFileNames = false,
        };
        var result = await ArchiveProcessor.TryResolveCompressionPasswordAsync(
            settings, "test.7z", null, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(original, result!.Password);
        Assert.False(result.EncryptFileNames);
        Assert.Empty(pwdStub.Calls); // 復号成功時はプロンプトを出さない
    }

    [Fact]
    public async Task Remember_GarbageCiphertext_NotifiesError_DoesNotPrompt()
    {
        // ciphertext がガベージ → 復号失敗 → 通知後に再プロンプト。
        // ただし再プロンプトの結果を保存する MutateAndSave 経路は SettingsManager.Instance に副作用が出るので、
        // ここではプロンプト「キャンセル」(返値 null) させて MutateAndSave に到達させない。
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "DPAPI は Windows 限定");

        var garbage = new byte[64];
        for (var i = 0; i < garbage.Length; i++) garbage[i] = (byte)(i * 11 + 5);

        var msgStub = new StubMsg();
        var pwdStub = new StubPasswordDialog { Plaintext = null }; // ユーザーキャンセル
        ArchiveProcessor.PasswordDialogImpl = pwdStub;
        ArchiveProcessor.MessageServiceImpl = msgStub;
        ArchiveProcessor.UiDispatcherImpl = new StubUi();

        var settings = new Settings
        {
            IsPasswordProtectionEnabled = true,
            PasswordMode = "Remember",
            EncryptedCompressionPassword = garbage,
        };
        var result = await ArchiveProcessor.TryResolveCompressionPasswordAsync(
            settings, "test.zip", null, TestContext.Current.CancellationToken);

        Assert.Null(result); // ユーザーキャンセル
        Assert.Single(msgStub.Errors); // 復号失敗の通知が 1 回出る
        Assert.Single(pwdStub.Calls); // 通知後にプロンプト 1 回
        Assert.Equal(PasswordDialogMode.CompressNew, pwdStub.Calls[0]);
    }

    [Fact]
    public async Task Remember_NoCiphertextSaved_SilentlyPromptsWithoutErrorNotification()
    {
        // ciphertext 未保存 (初回 Remember 利用) なので、復号失敗の通知 (SavedPasswordDecryptFailed) は出さない。
        var pwdStub = new StubPasswordDialog { Plaintext = null }; // ユーザーキャンセル
        var msgStub = new StubMsg();
        ArchiveProcessor.PasswordDialogImpl = pwdStub;
        ArchiveProcessor.MessageServiceImpl = msgStub;
        ArchiveProcessor.UiDispatcherImpl = new StubUi();

        var settings = new Settings
        {
            IsPasswordProtectionEnabled = true,
            PasswordMode = "Remember",
            EncryptedCompressionPassword = null, // 初回
        };
        var result = await ArchiveProcessor.TryResolveCompressionPasswordAsync(
            settings, "test.7z", null, TestContext.Current.CancellationToken);

        Assert.Null(result); // キャンセル
        Assert.Empty(msgStub.Errors); // 復号失敗通知は出ない
        Assert.Single(pwdStub.Calls); // プロンプトは出る
    }

    // --- ZIP ASCII パスワード制約 (同梱 7-Zip 26.00 の upstream regression 対応) ---

    [Fact]
    public async Task PromptEachTime_ZipHint_NonAsciiPassword_RepromptedThenAccepted()
    {
        // 1 回目に日本語パスワード → エラー通知 + isRetry=true で再プロンプト → 2 回目の ASCII を採用。
        var msgStub = new StubMsg();
        var pwdStub = new StubPasswordDialog { Sequence = new Queue<string?>(["にほんごパスワード", "ascii-pass"]) };
        ArchiveProcessor.PasswordDialogImpl = pwdStub;
        ArchiveProcessor.MessageServiceImpl = msgStub;
        ArchiveProcessor.UiDispatcherImpl = new StubUi();

        var settings = new Settings
        {
            IsPasswordProtectionEnabled = true,
            PasswordMode = "PromptEachTime",
        };
        var result = await ArchiveProcessor.TryResolveCompressionPasswordAsync(
            settings, "test.zip", null, TestContext.Current.CancellationToken, "ZIP");

        Assert.NotNull(result);
        Assert.Equal("ascii-pass", result!.Password);
        Assert.Equal(2, pwdStub.Calls.Count);
        Assert.Equal([false, true], pwdStub.IsRetryFlags);
        Assert.Single(msgStub.Errors); // ZipPasswordAsciiOnly の通知 1 回
    }

    [Fact]
    public async Task PromptEachTime_ZipHint_PersistentNonAscii_AbortsAfterMaxAttempts()
    {
        // 非 ASCII 入力が続く場合は上限 (5 回) でキャンセル扱いにして無限ループを防ぐ。
        var msgStub = new StubMsg();
        var pwdStub = new StubPasswordDialog { Plaintext = "ずっと日本語" };
        ArchiveProcessor.PasswordDialogImpl = pwdStub;
        ArchiveProcessor.MessageServiceImpl = msgStub;
        ArchiveProcessor.UiDispatcherImpl = new StubUi();

        var settings = new Settings
        {
            IsPasswordProtectionEnabled = true,
            PasswordMode = "PromptEachTime",
        };
        var result = await ArchiveProcessor.TryResolveCompressionPasswordAsync(
            settings, "test.zip", null, TestContext.Current.CancellationToken, "ZIP");

        Assert.Null(result);
        Assert.Equal(5, pwdStub.Calls.Count);
        Assert.Equal(5, msgStub.Errors.Count);
    }

    [Fact]
    public async Task PromptEachTime_SevenZipHint_NonAsciiPassword_AcceptedWithoutValidation()
    {
        // 7z は非 ASCII パスワードで正常動作するため検証しない (ライブラリ実機確認済み)。
        var msgStub = new StubMsg();
        var pwdStub = new StubPasswordDialog { Plaintext = "にほんごパスワード" };
        ArchiveProcessor.PasswordDialogImpl = pwdStub;
        ArchiveProcessor.MessageServiceImpl = msgStub;
        ArchiveProcessor.UiDispatcherImpl = new StubUi();

        var settings = new Settings
        {
            IsPasswordProtectionEnabled = true,
            PasswordMode = "PromptEachTime",
        };
        var result = await ArchiveProcessor.TryResolveCompressionPasswordAsync(
            settings, "test.7z", null, TestContext.Current.CancellationToken, "7z");

        Assert.NotNull(result);
        Assert.Equal("にほんごパスワード", result!.Password);
        Assert.Single(pwdStub.Calls);
        Assert.Empty(msgStub.Errors);
    }

    [Fact]
    public async Task Remember_SavedNonAsciiPassword_ZipHint_PromptsTemporaryWithoutOverwrite()
    {
        // 保存済みパスワードが非 ASCII + ZIP → 通知して一時パスワードを再プロンプト。
        // 7z 用に有効な保存値は上書きしない (MutateAndSave に到達しないこともこの経路の仕様)。
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "DPAPI は Windows 限定");

        var ciphertext = CompressionPasswordSession.Protect("にほんごパスワード");
        Assert.NotNull(ciphertext);

        var msgStub = new StubMsg();
        var pwdStub = new StubPasswordDialog { Plaintext = "temp-ascii" };
        ArchiveProcessor.PasswordDialogImpl = pwdStub;
        ArchiveProcessor.MessageServiceImpl = msgStub;
        ArchiveProcessor.UiDispatcherImpl = new StubUi();

        var settings = new Settings
        {
            IsPasswordProtectionEnabled = true,
            PasswordMode = "Remember",
            EncryptedCompressionPassword = ciphertext,
        };
        var result = await ArchiveProcessor.TryResolveCompressionPasswordAsync(
            settings, "test.zip", null, TestContext.Current.CancellationToken, "ZIP");

        Assert.NotNull(result);
        Assert.Equal("temp-ascii", result!.Password);
        Assert.Single(msgStub.Errors); // SavedPasswordZipAsciiOnly の通知 1 回 (復号失敗通知は出ない)
        Assert.Single(pwdStub.Calls);
    }

    [Fact]
    public async Task Remember_SavedNonAsciiPassword_SevenZipHint_UsedDirectly()
    {
        // 7z なら保存済みの非 ASCII パスワードをそのまま使う (プロンプト・通知なし)。
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "DPAPI は Windows 限定");

        var ciphertext = CompressionPasswordSession.Protect("にほんごパスワード");
        Assert.NotNull(ciphertext);

        var msgStub = new StubMsg();
        var pwdStub = new StubPasswordDialog { Plaintext = "SHOULD-NOT-BE-CALLED" };
        ArchiveProcessor.PasswordDialogImpl = pwdStub;
        ArchiveProcessor.MessageServiceImpl = msgStub;
        ArchiveProcessor.UiDispatcherImpl = new StubUi();

        var settings = new Settings
        {
            IsPasswordProtectionEnabled = true,
            PasswordMode = "Remember",
            EncryptedCompressionPassword = ciphertext,
        };
        var result = await ArchiveProcessor.TryResolveCompressionPasswordAsync(
            settings, "test.7z", null, TestContext.Current.CancellationToken, "7z");

        Assert.NotNull(result);
        Assert.Equal("にほんごパスワード", result!.Password);
        Assert.Empty(pwdStub.Calls);
        Assert.Empty(msgStub.Errors);
    }
}
