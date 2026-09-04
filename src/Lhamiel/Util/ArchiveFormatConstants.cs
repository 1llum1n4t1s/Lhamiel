namespace Lhamiel.Util;

/// <summary>
/// アーカイブ関連の共通定数。
/// 展開判定・関連付け・設定 UI・圧縮に分散していた拡張子リストをここに集約し、
/// 新フォーマット追加時の片側漏れを防ぐ。
/// </summary>
internal static class ArchiveFormatConstants
{
    /// <summary>
    /// 展開・ファイル関連付けの対象形式。拡張子はレジストリと UI で使うドットなし表記とし、
    /// 展開判定側だけドット付きへ投影する。
    /// </summary>
    internal static readonly (string Extension, string Description)[] SupportedArchiveFormats =
    [
        ("zip", "ZIP (.zip)"),
        ("7z", "7-Zip (.7z)"),
        ("tar", "TAR (.tar)"),
        ("gz", "GZIP (.gz)"),
        ("bz2", "BZIP2 (.bz2)"),
        ("lzma", "LZMA (.lzma)"),
        ("xz", "XZ (.xz)"),
        ("rar", "RAR (.rar)"),
        ("lzh", "LZH (.lzh)"),
        ("cab", "CAB (.cab)"),
        ("arj", "ARJ (.arj)"),
        ("z", "Z (.z)"),
        ("tgz", "TAR.GZ (.tgz)"),
        ("tbz2", "TAR.BZ2 (.tbz2)"),
        ("tbz", "TAR.BZ (.tbz)"),
        ("tlz", "TAR.LZMA (.tlz)"),
        ("txz", "TAR.XZ (.txz)"),
        ("tz", "TAR.Z (.tz)")
    ];

    /// <summary>
    /// 圧縮のみの拡張子（単体ではアーカイブでない、TAR と組み合わせて使う形式）。
    /// <see cref="ArchiveExtractor.GetArchiveBaseName"/> が「最外の拡張子を除去したあと .tar かどうか」で
    /// 二段除去するか判定するのに使う。
    /// </summary>
    internal static readonly HashSet<string> CompressionOnlyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".gz", ".bz2", ".xz", ".lzma", ".z"
    };

    /// <summary>
    /// 既知の複合拡張子（.tar 系の組み合わせ）。
    /// 圧縮ファイル名ステム分割（<see cref="ArchiveCompressor.SplitStemAndExtension"/>）で使用。
    /// <c>.tar.lz</c> / <c>.tar.zst</c> は 7z.dll で読める一方
    /// <see cref="CompressionOnlyExtensions"/> には含めていない（単体 .lz / .zst を既存仕様で扱わないため）ので、
    /// ここに明示的に列挙する。
    /// </summary>
    internal static readonly string[] CompoundTarExtensions =
    [
        ".tar.gz", ".tar.bz2", ".tar.xz", ".tar.lz", ".tar.lzma", ".tar.zst", ".tar.z"
    ];

}
