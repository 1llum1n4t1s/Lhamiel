using Avalonia.Controls;
using Cube.FileSystem.SevenZip;
using Lhamiel.Util;
using Lhamiel.View;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// he=on (ヘッダ暗号化) 7z の構造解析・展開・CRC 検証フローのテスト (codex P2 #3384706128)。
/// 7z.dll はヘッダ暗号化アーカイブをパスワード無しで開くと ctor で SevenZipException
/// (IsNotArc) を投げ「破損」と区別できない (実機確認済み)。このため:
/// - <see cref="ArchiveExtractor.GetArchiveStructureInfo"/> は OpenFailed=true を返し、
///   呼び出し側 (ArchiveProcessor) がパスワード確認 → password 付き再解析を行う
/// - 検証済みパスワードは knownPassword として展開・CRC 検証へ引き回され、再ダイアログを防ぐ
/// PasswordDialogImpl を差し替えるため ArchiveProcessor コレクションで排他実行。
/// </summary>
[Collection("ArchiveProcessor")]
public class HeaderEncryptedArchiveTests : IDisposable
{
    private const string Password = "he-test-password";

    private readonly IPasswordDialogService _origPwd;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "LhamielHeTests_" + Guid.NewGuid().ToString("N"));

    public HeaderEncryptedArchiveTests()
    {
        _origPwd = ArchiveProcessor.PasswordDialogImpl;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        ArchiveProcessor.PasswordDialogImpl = _origPwd;
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// 展開ダイアログ呼び出しを記録するスタブ。null (キャンセル) を返す。
    /// knownPassword が正しく機能していればこのスタブは一度も呼ばれない。
    /// </summary>
    private sealed class CountingPasswordDialog : IPasswordDialogService
    {
        public int CallCount;

        public Task<string?> PromptForPasswordAsync(
            string archiveDisplayName,
            PasswordDialogMode mode,
            bool isRetry,
            Window? parentWindow,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CallCount);
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>
    /// アーカイブ名と同名のルートフォルダ 1 つ (中にファイル 1 つ) を持つ
    /// he=on ヘッダ暗号化 7z を作成する。
    /// </summary>
    private async Task<string> CreateHeaderEncryptedArchiveAsync(string baseName)
    {
        var folder = Path.Combine(_dir, baseName);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(
            Path.Combine(folder, "secret.txt"), "hello-he", TestContext.Current.CancellationToken);

        var archive = Path.Combine(_dir, baseName + ".7z");
        await ArchiveCompressor.CompressFilesAsync(
            [folder], archive, Format.SevenZip,
            cancellationToken: TestContext.Current.CancellationToken,
            settingsOverride: new Settings { DirectoryStructureMode = DirectoryStructureMode.IncludeRoot },
            password: Password, encryptFileNames: true);
        return archive;
    }

    [Fact]
    public async Task GetArchiveStructureInfo_HeaderEncrypted_WithoutPassword_ReportsOpenFailed()
    {
        var archive = await CreateHeaderEncryptedArchiveAsync("data1");

        var info = ArchiveExtractor.GetArchiveStructureInfo(archive);

        Assert.True(info.OpenFailed);
        Assert.False(info.ShouldSkipFolderCreation); // 構造不明なので判定不能 (デフォルト false)
    }

    [Fact]
    public async Task GetArchiveStructureInfo_HeaderEncrypted_WithWrongPassword_ReportsOpenFailed()
    {
        var archive = await CreateHeaderEncryptedArchiveAsync("data2");

        var info = ArchiveExtractor.GetArchiveStructureInfo(archive, "wrong-password");

        Assert.True(info.OpenFailed);
    }

    [Fact]
    public async Task GetArchiveStructureInfo_HeaderEncrypted_WithPassword_DetectsFolderNameMatch()
    {
        var archive = await CreateHeaderEncryptedArchiveAsync("data3");

        var info = ArchiveExtractor.GetArchiveStructureInfo(archive, Password);

        Assert.False(info.OpenFailed);
        // ルートフォルダ "data3" == アーカイブ名 "data3" → フォルダ作成スキップ (Foo/Foo 防止)
        Assert.True(info.ShouldSkipFolderCreation);
        Assert.Equal("data3", info.SingleRootItemName);
        Assert.True(info.TotalUncompressedSize > 0);
    }

    [Fact]
    public async Task ExtractArchive_WithKnownPassword_ExtractsWithoutDialog()
    {
        var archive = await CreateHeaderEncryptedArchiveAsync("data4");
        var stub = new CountingPasswordDialog();
        ArchiveProcessor.PasswordDialogImpl = stub;

        var outDir = Path.Combine(_dir, "out4");
        await ArchiveExtractor.ExtractArchive(
            archive, outDir,
            cancellationToken: TestContext.Current.CancellationToken,
            knownPassword: Password);

        Assert.Equal(0, stub.CallCount); // 検証済みパスワードで完結、再ダイアログなし
        var extracted = Path.Combine(outDir, "data4", "secret.txt");
        Assert.True(File.Exists(extracted));
        Assert.Equal("hello-he", await File.ReadAllTextAsync(extracted, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VerifyArchiveAsync_HeaderEncrypted_PasswordEnablesRealVerification()
    {
        var archive = await CreateHeaderEncryptedArchiveAsync("data5");

        // パスワード無し: ctor 失敗 → 「破損」と区別できず検証失敗扱い (既知の制約)
        var withoutPw = await ArchiveIntegrityVerifier.VerifyArchiveAsync(
            archive, TestContext.Current.CancellationToken);
        Assert.False(withoutPw.IsValid);

        // 検証済みパスワード付き: 実際に CRC 検証が走り正常判定になる
        var withPw = await ArchiveIntegrityVerifier.VerifyArchiveAsync(
            archive, TestContext.Current.CancellationToken, Password);
        Assert.True(withPw.IsValid);
    }
}
