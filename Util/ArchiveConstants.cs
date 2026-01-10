namespace Lhamiel.Util;

/// <summary>
/// アーカイブ処理に関する定数定義
/// </summary>
public static class ArchiveConstants
{
    // ファイルサイズ定数
    public const long MinSelfExtractingSize = 1024 * 1024; // 1MB
    public const long MaxSelfExtractingSize = 2 * 1024 * 1024; // 2MB
    public const long SmallFileScanSize = 512 * 1024; // 512KB
    public const int BufferSize = 4096; // 4KB

    // MZ ヘッダー (Windows実行ファイル)
    public const byte MzHeaderFirstByte = 0x4D; // 'M'
    public const byte MzHeaderSecondByte = 0x5A; // 'Z'

    // PK ヘッダー (ZIP形式)
    public const byte PkHeaderFirstByte = 0x50; // 'P'
    public const byte PkHeaderSecondByte = 0x4B; // 'K'

    // 7z シグネチャ
    public static readonly byte[] SevenZipSignature = { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C };

    // RAR シグネチャ (v4.x)
    public static readonly byte[] RarSignature = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 };

    // RAR シグネチャ (v5.0+)
    public static readonly byte[] Rar5Signature = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 };

    // 検索文字列
    public const string SfxStubIdentifier = "7zS.sfx";
    public const string WinRarSfxIdentifier = "WinRAR";

    // スキャン戦略の開始位置
    public const int FirstScanOffset = 0;
    public const int SecondScanOffsetSmall = 256 * 1024; // 256KB後から開始
    public const int ThirdScanOffsetLarge = 512 * 1024; // 512KB後から開始
}
