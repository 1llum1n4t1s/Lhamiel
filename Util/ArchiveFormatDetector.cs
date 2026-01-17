using System.IO;

namespace Lhamiel.Util;

/// <summary>
/// アーカイブ形式の検出を行うユーティリティクラス
/// </summary>
public static class ArchiveFormatDetector
{
    /// <summary>
    /// 指定されたファイルが自己展開アーカイブかどうかを判定する
    /// </summary>
    /// <param name="filePath">判定対象のファイルパス</param>
    /// <returns>自己展開アーカイブの場合はtrue、それ以外はfalse</returns>
    /// <remarks>
    /// このメソッドは、ファイルを3つの異なる戦略でスキャンして、
    /// 実行可能ファイル内に埋め込まれた圧縮データを検出します：
    /// 1. ファイル末尾から2MBをスキャン（末尾に圧縮データがある場合）
    /// 2. ファイル中央付近から1MBをスキャン（中央に圧縮データがある場合）
    /// 3. ファイル先頭から1MBをスキャン（先頭に圧縮データがある場合）
    /// </remarks>
    public static bool IsSelfExtractingArchive(string filePath)
    {
        try
        {
            Logger.Log($"IsSelfExtractingArchive開始: {filePath}", LogLevel.Debug);

            // ファイルサイズを確認（小さすぎるファイルは自己展開圧縮ファイルではない）
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length < ArchiveConstants.MinSelfExtractingSize)
            {
                Logger.Log($"ファイルサイズが小さすぎます: {fileInfo.Length} bytes", LogLevel.Debug);
                return false;
            }

            // ファイルの先頭バイトを読み取って、自己展開圧縮ファイルの特徴を確認
            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream);

            // ファイルの先頭から4バイトを読み取り
            var header = reader.ReadBytes(4);

            // MZヘッダー（DOS実行可能ファイル）の確認
            if (header[0] != ArchiveConstants.MzHeaderFirstByte ||
                header[1] != ArchiveConstants.MzHeaderSecondByte)
            {
                Logger.Log("MZヘッダーが見つかりませんでした", LogLevel.Debug);
                return false;
            }

            Logger.Log("MZヘッダーを確認（DOS実行可能ファイル）", LogLevel.Debug);

            // 複数のスキャン戦略を試行
            var scanStrategies = new (long Start, long Size)[]
            {
                // 戦略1: ファイル末尾から2MBをスキャン
                (Math.Max(0, fileInfo.Length - ArchiveConstants.MaxSelfExtractingSize),
                 Math.Min(ArchiveConstants.MaxSelfExtractingSize, fileInfo.Length)),
                // 戦略2: ファイル中央付近から1MBをスキャン
                (Math.Max(0, fileInfo.Length / 2 - ArchiveConstants.SmallFileScanSize),
                 Math.Min(ArchiveConstants.MinSelfExtractingSize, fileInfo.Length)),
                // 戦略3: ファイル先頭から1MBをスキャン（実行可能ファイルのヘッダー部分を除く）
                (ArchiveConstants.BufferSize,
                 Math.Min(ArchiveConstants.MinSelfExtractingSize, fileInfo.Length - ArchiveConstants.BufferSize))
            };

            foreach (var strategy in scanStrategies)
            {
                if (strategy.Size <= 0) continue;

                Logger.Log($"圧縮データスキャン戦略: 開始位置={strategy.Start}, スキャンサイズ={strategy.Size}", LogLevel.Debug);

                try
                {
                    stream.Seek(strategy.Start, SeekOrigin.Begin);
                    var scanBytes = reader.ReadBytes((int)strategy.Size);

                    // 各アーカイブ形式の特徴を検出
                    if (DetectZipSignature(scanBytes, strategy.Start) ||
                        DetectSevenZipSignature(scanBytes) ||
                        DetectRarSignature(scanBytes) ||
                        DetectArchiveKeywords(scanBytes))
                    {
                        Logger.Log($"自己展開圧縮ファイルを確認: {filePath}");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"スキャン戦略でエラー: {ex.Message}", LogLevel.Warning);
                    continue;
                }
            }

            Logger.Log("圧縮ファイルの特徴が見つかりませんでした", LogLevel.Debug);
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogException("自己展開圧縮ファイルの判定中にエラーが発生", ex);
            return false;
        }
    }

    /// <summary>
    /// ZIPファイルのシグネチャ（PK\x03\x04）を検出
    /// </summary>
    private static bool DetectZipSignature(byte[] scanBytes, long startOffset)
    {
        for (int i = 0; i <= scanBytes.Length - 4; i++)
        {
            if (scanBytes[i] == ArchiveConstants.PkHeaderFirstByte &&
                scanBytes[i + 1] == ArchiveConstants.PkHeaderSecondByte &&
                scanBytes[i + 2] == 0x03 &&
                scanBytes[i + 3] == 0x04)
            {
                Logger.Log($"ZIPファイルの特徴を発見: オフセット={startOffset + i}", LogLevel.Debug);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 7-Zipファイルのシグネチャを検出
    /// </summary>
    private static bool DetectSevenZipSignature(byte[] scanBytes)
    {
        if (MatchSignature(scanBytes, ArchiveConstants.SevenZipSignature))
        {
            Logger.Log("7-Zipファイルの特徴を発見", LogLevel.Debug);
            return true;
        }
        return false;
    }

    /// <summary>
    /// RARファイルのシグネチャを検出 (RAR v4.x および v5.0+)
    /// </summary>
    private static bool DetectRarSignature(byte[] scanBytes)
    {
        // RAR v4.x
        if (MatchSignature(scanBytes, ArchiveConstants.RarSignature))
        {
            Logger.Log("RAR v4.x ファイルの特徴を発見", LogLevel.Debug);
            return true;
        }

        // RAR v5.0+
        if (MatchSignature(scanBytes, ArchiveConstants.Rar5Signature))
        {
            Logger.Log("RAR v5.0+ ファイルの特徴を発見", LogLevel.Debug);
            return true;
        }

        return false;
    }

    /// <summary>
    /// バイト配列内から特定のシグネチャを検索する共通メソッド
    /// </summary>
    private static bool MatchSignature(byte[] scanBytes, byte[] signature)
    {
        for (int i = 0; i <= scanBytes.Length - signature.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < signature.Length; j++)
            {
                if (scanBytes[i + j] != signature[j])
                {
                    match = false;
                    break;
                }
            }

            if (match) return true;
        }
        return false;
    }

    /// <summary>
    /// アーカイブ関連のキーワードを検出
    /// </summary>
    private static bool DetectArchiveKeywords(byte[] scanBytes)
    {
        var searchStrings = new[]
        {
            "PKZIP", "unzipsfx", "Info-ZIP", "SFX", "ZIP", "PKWARE",
            "WinZip", "7-Zip", "RAR", "self-extracting", "self extracting",
            "extract", "unzip", "decompress", "archive"
        };

        var fileContent = System.Text.Encoding.ASCII.GetString(scanBytes);

        foreach (var searchString in searchStrings)
        {
            if (fileContent.Contains(searchString, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log($"unzipsfxの特徴を発見: {searchString}", LogLevel.Debug);
                return true;
            }
        }

        return false;
    }
}
