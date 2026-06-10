using Cube.FileSystem.SevenZip;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// <see cref="ArchiveCompressor.CompressFilesAsync"/> のパスワード保護経路に対する統合テスト。
/// 実 7z.dll (1llum1n4t1s.Sevenzip) を経由するので Windows 限定。
/// <see cref="NativeArchiveGate"/> で直列化されているため <c>[Collection("Sequential")]</c> を付与。
/// </summary>
[Collection("Sequential")]
public class ArchiveCompressorPasswordTests : IDisposable
{
    private readonly string _testDir;

    public ArchiveCompressorPasswordTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "ArchiveCompressorPasswordTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, recursive: true); }
        catch { /* テスト後始末の失敗は無視 */ }
    }

    private string CreateSourceFile(string name = "secret.txt", string content = "Top secret content 0xDEADBEEF")
    {
        var path = Path.Combine(_testDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task TarFormat_WithPassword_Throws()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "7z.dll 経路は Windows 限定");
        var src = CreateSourceFile();
        var archive = Path.Combine(_testDir, "out.tar");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ArchiveCompressor.CompressFilesAsync(
                [src], archive, Format.Tar,
                cancellationToken: TestContext.Current.CancellationToken,
                password: "any-password"));
    }

    [Fact]
    public async Task ZipFormat_WithPassword_CreatesNonEmptyArchive()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "7z.dll 経路は Windows 限定");
        var src = CreateSourceFile();
        var archive = Path.Combine(_testDir, "encrypted.zip");

        await ArchiveCompressor.CompressFilesAsync(
            [src], archive, Format.Zip,
            cancellationToken: TestContext.Current.CancellationToken,
            password: "mypassword");

        Assert.True(File.Exists(archive));
        var size = new FileInfo(archive).Length;
        Assert.True(size > 50, $"暗号化 ZIP のサイズが想定より小さい: {size} bytes");
    }

    [Fact]
    public async Task SevenZipFormat_WithPasswordAndHeaderEncryption_CreatesArchive()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "7z.dll 経路は Windows 限定");
        var src = CreateSourceFile();
        var archive = Path.Combine(_testDir, "encrypted.7z");

        await ArchiveCompressor.CompressFilesAsync(
            [src], archive, Format.SevenZip,
            cancellationToken: TestContext.Current.CancellationToken,
            password: "mypassword", encryptFileNames: true);

        Assert.True(File.Exists(archive));
        // 7z ヘッダ暗号化が掛かっていると、メタデータがランダム化されてエントロピが高くなる。
        // ここでは「ファイルが存在し、サイズが妥当」の最低限の検証にとどめる。
        var size = new FileInfo(archive).Length;
        Assert.True(size > 50, $"暗号化 7z のサイズが想定より小さい: {size} bytes");
    }

    [Fact]
    public async Task ZipFormat_WithPassword_IsActuallyAes256_NotZipCrypto()
    {
        // ライブラリソース監査で判明: CompressionOptionSetter.Invoke は ISetProperties.SetProperties の
        // HRESULT を破棄するため、仮に "em" 値が 7z.dll に拒否されても例外にならず
        // **サイレントに ZipCrypto へ落ちる**経路が存在する。ここでは生成物の bytes を直接
        // パースして WinZip AE (compression method 99 + extra field 0x9901, strength=3) を検証し、
        // ZipCrypto 落ちをビット単位で検出する (NuGet / 7z.dll 更新時の回帰検知)。
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "7z.dll 経路は Windows 限定");
        var src = CreateSourceFile();
        var archive = Path.Combine(_testDir, "aes-check.zip");

        await ArchiveCompressor.CompressFilesAsync(
            [src], archive, Format.Zip,
            cancellationToken: TestContext.Current.CancellationToken,
            password: "mypassword");

        var bytes = await File.ReadAllBytesAsync(archive, TestContext.Current.CancellationToken);

        // local file header: PK\x03\x04
        Assert.True(bytes.Length > 40, "ZIP が小さすぎる");
        Assert.Equal(0x50, bytes[0]);
        Assert.Equal(0x4B, bytes[1]);
        Assert.Equal(0x03, bytes[2]);
        Assert.Equal(0x04, bytes[3]);

        // general purpose bit flag bit0 = encrypted
        var flags = BitConverter.ToUInt16(bytes, 6);
        Assert.True((flags & 0x0001) != 0, "暗号化ビットが立っていない");

        // compression method 99 = AE-x (AES)。ZipCrypto なら元 method (8=Deflate) のまま。
        var method = BitConverter.ToUInt16(bytes, 8);
        Assert.True(method == 99, $"compression method が AE-x (99) でない: {method} (ZipCrypto に落ちている可能性)");

        // extra field 0x9901 (AE-x): vendorVersion(2) + vendorId(2,'AE') + strength(1) + actualMethod(2)
        var nameLen = BitConverter.ToUInt16(bytes, 26);
        var extraLen = BitConverter.ToUInt16(bytes, 28);
        var off = 30 + nameLen;
        var end = off + extraLen;
        var strength = -1;
        while (off + 4 <= end)
        {
            var id = BitConverter.ToUInt16(bytes, off);
            var sz = BitConverter.ToUInt16(bytes, off + 2);
            if (id == 0x9901)
            {
                strength = bytes[off + 4 + 4];
                break;
            }
            off += 4 + sz;
        }
        Assert.True(strength >= 0, "AES extra field 0x9901 が見つからない");
        Assert.True(strength == 3, $"AES 強度が 256bit (3) でない: {strength} (1=128, 2=192)");
    }

    [Fact]
    public async Task SevenZip_HeaderEncryption_HidesFileNamesWithoutPassword()
    {
        // he=on の実効検証: CustomParameters["he"]="on" が 7z.dll に届いていれば、
        // パスワード無しの reader はヘッダ (ファイル名一覧) 自体を読めない。
        // 「サイズ > 50」の存在チェックでは he 指定が無視されても検出できないため、
        // reader レベルで検証する。
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "7z.dll 経路は Windows 限定");
        var src = CreateSourceFile();
        var archive = Path.Combine(_testDir, "he-on.7z");

        await ArchiveCompressor.CompressFilesAsync(
            [src], archive, Format.SevenZip,
            cancellationToken: TestContext.Current.CancellationToken,
            password: "mypassword", encryptFileNames: true);

        // パスワード無し: ヘッダ復号不能で Items 列挙 (または open) が失敗する
        Assert.ThrowsAny<Exception>(() =>
        {
            using var reader = new ArchiveReader(archive);
            _ = reader.Items.Count;
        });

        // 正しいパスワード: ファイル名が見える
        using (var reader = new ArchiveReader(archive, "mypassword", new ArchiveOption()))
        {
            Assert.Contains(reader.Items, e => e.Name == "secret.txt");
        }
    }

    [Fact]
    public async Task SevenZip_WithoutHeaderEncryption_FileNamesVisibleWithoutPassword()
    {
        // he=on テストの対照: encryptFileNames=false なら本文のみ暗号化で、
        // ファイル名はパスワード無しでも列挙できる (7z 仕様通り)。
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "7z.dll 経路は Windows 限定");
        var src = CreateSourceFile();
        var archive = Path.Combine(_testDir, "he-off.7z");

        await ArchiveCompressor.CompressFilesAsync(
            [src], archive, Format.SevenZip,
            cancellationToken: TestContext.Current.CancellationToken,
            password: "mypassword", encryptFileNames: false);

        using var reader = new ArchiveReader(archive);
        Assert.Contains(reader.Items, e => e.Name == "secret.txt");
    }

    [Fact]
    public async Task SevenZipFormat_WithoutPassword_CreatesArchive()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "7z.dll 経路は Windows 限定");
        var src = CreateSourceFile();
        var archive = Path.Combine(_testDir, "plain.7z");

        await ArchiveCompressor.CompressFilesAsync(
            [src], archive, Format.SevenZip,
            cancellationToken: TestContext.Current.CancellationToken,
            password: null);

        Assert.True(File.Exists(archive));
        Assert.True(new FileInfo(archive).Length > 0);
    }

    [Fact]
    public async Task ZipFormat_WithNonAsciiPassword_FailsFastWithSpecificError()
    {
        // 同梱 7-Zip 26.00 は ZIP 作成時に非 ASCII パスワードを E_INVALIDARG で拒否する
        // (upstream regression、実機確認済み)。ネイティブの不透明な SevenZipException ではなく、
        // CreateArchiveWriter の guard が具体的なメッセージで fail-fast することを検証する。
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "7z.dll 経路は Windows 限定");
        var src = CreateSourceFile();
        var archive = Path.Combine(_testDir, "cjk.zip");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ArchiveCompressor.CompressFilesAsync(
                [src], archive, Format.Zip,
                cancellationToken: TestContext.Current.CancellationToken,
                password: "にほんごパスワード"));

        Assert.Contains("ZipPasswordAsciiOnly", ex.Message);
        Assert.False(File.Exists(archive));
    }

    [Fact]
    public async Task SevenZipFormat_WithNonAsciiPassword_Succeeds()
    {
        // 7z は非 ASCII パスワードで正常動作する (26.00 regression は ZIP 作成のみ)。
        // 同梱 7z.dll を更新して 7z 側にも regression が波及した場合の sentinel。
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "7z.dll 経路は Windows 限定");
        var src = CreateSourceFile();
        var archive = Path.Combine(_testDir, "cjk.7z");

        await ArchiveCompressor.CompressFilesAsync(
            [src], archive, Format.SevenZip,
            cancellationToken: TestContext.Current.CancellationToken,
            password: "にほんごパスワード", encryptFileNames: true);

        Assert.True(File.Exists(archive));
        // 正しいパスワードで開けることまで確認 (書けたが読めない、を検出)
        using var reader = new ArchiveReader(archive, "にほんごパスワード", new ArchiveOption());
        Assert.Contains(reader.Items, e => e.Name == "secret.txt");
    }

    [Fact]
    public void ContainsNonAscii_Boundaries()
    {
        // ASCII 全域 (0x20-0x7E) は false、0x7F (DEL) も ASCII 範囲内なので false。
        Assert.False(ArchiveCompressor.ContainsNonAscii("abc XYZ 012 !\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~"));
        Assert.False(ArchiveCompressor.ContainsNonAscii("\x7F"));
        // 0x80 以上は true (全角・かな・アクセント付きラテン・キリル)
        Assert.True(ArchiveCompressor.ContainsNonAscii("ｐａｓｓｗｏｒｄ"));
        Assert.True(ArchiveCompressor.ContainsNonAscii("ぱすわーど"));
        Assert.True(ArchiveCompressor.ContainsNonAscii("café"));
        Assert.True(ArchiveCompressor.ContainsNonAscii("пароль"));
    }

    [Fact]
    public async Task EmptySourcePaths_Throws()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "7z.dll 経路は Windows 限定");
        var archive = Path.Combine(_testDir, "out.zip");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            ArchiveCompressor.CompressFilesAsync(
                [], archive, Format.Zip,
                cancellationToken: TestContext.Current.CancellationToken,
                password: "any"));
    }
}
