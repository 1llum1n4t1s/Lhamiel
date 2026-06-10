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
    /// 常に間違ったパスワードを返すスタブ (呼び出し回数を記録)。
    /// 構造解析の試行上限テストで「上限後に展開段の再プロンプトが出ない」ことの検証に使う。
    /// </summary>
    private sealed class WrongPasswordDialog : IPasswordDialogService
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
            return Task.FromResult<string?>("definitely-wrong-password");
        }
    }

    /// <summary>
    /// 同時に開いているプロンプト数を追跡するスタブ。一定時間保持してから null (キャンセル) を返す。
    /// StructurePasswordPromptGate が機能していれば MaxObservedConcurrency は 1 を超えない。
    /// </summary>
    private sealed class ConcurrencyTrackingPasswordDialog : IPasswordDialogService
    {
        private int _active;
        public int CallCount;
        public int MaxObservedConcurrency;

        public async Task<string?> PromptForPasswordAsync(
            string archiveDisplayName,
            PasswordDialogMode mode,
            bool isRetry,
            Window? parentWindow,
            CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref _active);
            int seen;
            while (now > (seen = Volatile.Read(ref MaxObservedConcurrency)))
                Interlocked.CompareExchange(ref MaxObservedConcurrency, now, seen);
            Interlocked.Increment(ref CallCount);
            // ゲートが無い場合に 2 つ目のプロンプトと確実に重なる猶予を作る
            await Task.Delay(150, cancellationToken);
            Interlocked.Decrement(ref _active);
            return null;
        }
    }

    /// <summary>
    /// アーカイブ名と同名のルートフォルダ 1 つ (中にファイル 1 つ) を持つ
    /// パスワード付き 7z を作成する。encryptFileNames=true で he=on (ヘッダ暗号化)、
    /// false でヘッダ可視・中身のみ暗号化 (展開中の AsyncPasswordQuery 経路に入る)。
    /// </summary>
    private async Task<string> CreateHeaderEncryptedArchiveAsync(string baseName, bool encryptFileNames = true)
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
            password: Password, encryptFileNames: encryptFileNames);
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

    [Fact]
    public async Task ExtractArchiveAsync_HeaderEncrypted_CancelPrompt_CancelsExtraction()
    {
        // codex P2 #3385210131: 構造解析プロンプトの明示キャンセルは従来経路へ合流せず
        // 展開ごと中止する (合流すると展開中の AsyncPasswordQuery が再ダイアログを出す)。
        var archive = await CreateHeaderEncryptedArchiveAsync("data6");
        var stub = new CountingPasswordDialog(); // null = キャンセルを返す
        ArchiveProcessor.PasswordDialogImpl = stub;

        var outDir = Path.Combine(_dir, "out6");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ArchiveProcessor.ExtractArchiveAsync(archive, outDir, outputToSameDirectory: false, progressWindow: null));

        Assert.Equal(1, stub.CallCount); // キャンセル後に 2 つ目のダイアログが出ない
        Assert.False(Directory.Exists(outDir)); // 何も展開されていない
    }

    [Fact]
    public async Task ExtractArchiveAsync_ParallelHeaderEncrypted_PromptsAreSerialized()
    {
        // codex P2 #3385210128: バッチ並列で複数の he=on アーカイブが同時に構造解析へ到達しても
        // StructurePasswordPromptGate がプロンプトを 1 つずつに直列化する。
        var a1 = await CreateHeaderEncryptedArchiveAsync("data7");
        var a2 = await CreateHeaderEncryptedArchiveAsync("data8");
        var stub = new ConcurrencyTrackingPasswordDialog();
        ArchiveProcessor.PasswordDialogImpl = stub;

        var t1 = ArchiveProcessor.ExtractArchiveAsync(a1, Path.Combine(_dir, "out7"), outputToSameDirectory: false, progressWindow: null);
        var t2 = ArchiveProcessor.ExtractArchiveAsync(a2, Path.Combine(_dir, "out8"), outputToSameDirectory: false, progressWindow: null);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => t1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => t2);

        Assert.Equal(2, stub.CallCount);
        Assert.Equal(1, stub.MaxObservedConcurrency); // ダイアログが積み重ならない
    }

    [Fact]
    public async Task ExtractArchiveAsync_HeaderEncrypted_ExhaustedWrongPasswords_NoSecondPromptSet()
    {
        // codex P2 #3386575724: 構造解析で 3 回パスワードを間違えたら、従来経路の
        // AsyncPasswordQuery でさらに 3 回プロンプトを出さず、キャンセル扱いで中止する。
        var archive = await CreateHeaderEncryptedArchiveAsync("data10");
        var stub = new WrongPasswordDialog();
        ArchiveProcessor.PasswordDialogImpl = stub;

        var outDir = Path.Combine(_dir, "out10");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ArchiveProcessor.ExtractArchiveAsync(archive, outDir, outputToSameDirectory: false, progressWindow: null));

        Assert.Equal(3, stub.CallCount); // 構造解析の 3 回のみ。展開段の再プロンプト無し
    }

    [Fact]
    public async Task ExtractArchiveAsync_MixedEncryptedBatch_DialogsAreSerialized()
    {
        // codex P2 #3386575715: he=on (構造解析プロンプト) とヘッダ可視の暗号化アーカイブ
        // (展開中の AsyncPasswordQuery プロンプト) が並列バッチで混在しても、
        // ExtractionPasswordDialogGate がダイアログ表示を 1 つずつに直列化する。
        var heOn = await CreateHeaderEncryptedArchiveAsync("data11");
        var heOff = await CreateHeaderEncryptedArchiveAsync("data12", encryptFileNames: false);
        var stub = new ConcurrencyTrackingPasswordDialog();
        ArchiveProcessor.PasswordDialogImpl = stub;

        var t1 = ArchiveProcessor.ExtractArchiveAsync(heOn, Path.Combine(_dir, "out11"), outputToSameDirectory: false, progressWindow: null);
        var t2 = ArchiveProcessor.ExtractArchiveAsync(heOff, Path.Combine(_dir, "out12"), outputToSameDirectory: false, progressWindow: null);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => t1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => t2);

        Assert.Equal(2, stub.CallCount); // 構造解析側 1 回 + 展開中クエリ側 1 回
        Assert.Equal(1, stub.MaxObservedConcurrency); // 経路をまたいでも積み重ならない
    }
}
