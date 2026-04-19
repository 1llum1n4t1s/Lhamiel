namespace Lhamiel.Util;

/// <summary>
/// アーカイブ関連の共通定数。
/// <see cref="ArchiveExtractor"/> と <see cref="ArchiveCompressor"/> に分散していた
/// 拡張子リストをここに集約し、新フォーマット追加時の片側漏れを防ぐ。
/// </summary>
internal static class ArchiveFormatConstants
{
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

    /// <summary>
    /// パス区切り文字の配列（<c>\</c> と <c>/</c>）。
    /// <see cref="string.Split(char[], StringSplitOptions)"/> 等に渡すとき
    /// 毎回配列生成するとホットパスでアロケが発生するため、読取専用定数として共有する。
    /// </summary>
    internal static readonly char[] PathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>
    /// パス区切り文字を `/` に正規化する共通ヘルパ（アーカイブ内パスの比較・表示用）。
    /// </summary>
    internal static string NormalizeToForwardSlash(string path) =>
        string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
}
