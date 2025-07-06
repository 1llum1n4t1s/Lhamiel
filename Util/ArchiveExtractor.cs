using SharpCompress.Archives;
using SharpCompress.Common;
using System.IO.Compression;
using System.IO;
using SevenZip;

namespace GGEZArchiver.Util;

/// <summary>
/// アーカイブ展開機能を提供するクラス
/// 様々な圧縮形式（ZIP、7Z、TAR、GZ、BZ2、LZMA、XZ、RAR、LZH、CAB、ARJ、Z）に対応
/// </summary>
public class ArchiveExtractor
{
    /// <summary>
    /// 指定されたアーカイブファイルを展開する
    /// ファイル形式を自動判定して適切な展開処理を実行する
    /// </summary>
    /// <param name="archivePath">展開するアーカイブファイルのパス</param>
    /// <param name="outputDirectory">展開先のディレクトリパス</param>
    /// <param name="progress">進行状況を報告するオブジェクト（オプション）</param>
    /// <returns>展開処理の完了を表すTask</returns>
    /// <exception cref="ArgumentException">無効なパスが指定された場合</exception>
    /// <exception cref="FileNotFoundException">アーカイブファイルが見つからない場合</exception>
    public static async Task ExtractArchiveAsync(string archivePath, string outputDirectory, IProgress<int>? progress = null)
    {
        // 入力パスの検証
        if (string.IsNullOrWhiteSpace(archivePath))
            throw new ArgumentException("アーカイブファイルのパスが指定されていません。", nameof(archivePath));
        
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("展開先ディレクトリが指定されていません。", nameof(outputDirectory));

        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"アーカイブファイルが見つかりません: {archivePath}");

        await Task.Run(() =>
        {
            var extension = Path.GetExtension(archivePath).ToLowerInvariant();
            
            // 7Z形式の場合はSevenZipSharpを使用
            if (extension == ".7z")
            {
                Extract7zArchive(archivePath, outputDirectory, progress);
            }
            // その他の形式はSharpCompressを使用
            else
            {
                ExtractWithSharpCompress(archivePath, outputDirectory, progress);
            }
        });
    }

    /// <summary>
    /// SharpCompressライブラリを使用してアーカイブを展開する
    /// ZIP、TAR、GZ、BZ2、LZMA、XZ、RAR、LZH、CAB、ARJ、Z形式に対応
    /// </summary>
    /// <param name="archivePath">展開するアーカイブファイルのパス</param>
    /// <param name="outputDirectory">展開先のディレクトリパス</param>
    /// <param name="progress">進行状況を報告するオブジェクト（オプション）</param>
    private static void ExtractWithSharpCompress(string archivePath, string outputDirectory, IProgress<int>? progress)
    {
        using var archive = ArchiveFactory.Open(archivePath);
        var entries = archive.Entries.ToList();
        var totalEntries = entries.Count;
        var processedEntries = 0;

        foreach (var entry in entries)
        {
            if (!entry.IsDirectory)
            {
                var outputPath = Path.Combine(outputDirectory, entry.Key);
                var outputDir = Path.GetDirectoryName(outputPath);
                
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                using var entryStream = entry.OpenEntryStream();
                using var fileStream = File.Create(outputPath);
                entryStream.CopyTo(fileStream);
            }

            processedEntries++;
            var percentage = (int)((double)processedEntries / totalEntries * 100);
            progress?.Report(percentage);
        }
    }

    /// <summary>
    /// SevenZipSharpライブラリを使用して7Zアーカイブを展開する
    /// 高圧縮率の7Z形式に特化した展開処理を提供
    /// </summary>
    /// <param name="archivePath">展開する7Zファイルのパス</param>
    /// <param name="outputDirectory">展開先のディレクトリパス</param>
    /// <param name="progress">進行状況を報告するオブジェクト（オプション）</param>
    private static void Extract7zArchive(string archivePath, string outputDirectory, IProgress<int>? progress)
    {
        using var extractor = new SevenZipExtractor(archivePath);
        
        if (progress != null)
        {
            extractor.Extracting += (s, e) =>
            {
                progress.Report(e.PercentDone);
            };
        }

        extractor.ExtractArchive(outputDirectory);
    }

    /// <summary>
    /// 展開先のディレクトリパスを決定する
    /// 設定で指定された出力ディレクトリとアーカイブファイル名を組み合わせて生成
    /// 出力先パターンが「元のファイルと同じディレクトリ」の場合は元ファイルのディレクトリを使用
    /// </summary>
    /// <param name="archivePath">展開するアーカイブファイルのパス</param>
    /// <param name="defaultOutputDir">デフォルトの出力ディレクトリ</param>
    /// <param name="outputToSameDirectory">元のファイルと同じディレクトリに出力するかどうか</param>
    /// <returns>展開先のディレクトリパス</returns>
    public static string GetOutputDirectory(string archivePath, string defaultOutputDir, bool outputToSameDirectory = false)
    {
        string outputDir;
        
        if (outputToSameDirectory)
        {
            // 元のファイルと同じディレクトリに出力
            var archiveDir = Path.GetDirectoryName(archivePath);
            var fileName = Path.GetFileNameWithoutExtension(archivePath);
            outputDir = Path.Combine(archiveDir ?? string.Empty, fileName);
        }
        else
        {
            // 指定されたディレクトリに出力
            var fileName = Path.GetFileNameWithoutExtension(archivePath);
            outputDir = Path.Combine(defaultOutputDir, fileName);
        }
        
        // 同名ディレクトリが存在する場合は番号を付ける
        var counter = 1;
        var originalOutputDir = outputDir;
        
        while (Directory.Exists(outputDir))
        {
            outputDir = $"{originalOutputDir}_{counter}";
            counter++;
        }
        
        return outputDir;
    }

    /// <summary>
    /// 指定されたファイルがサポートされている展開形式かどうかを判定する
    /// </summary>
    /// <param name="filePath">チェックするファイルのパス</param>
    /// <returns>サポートされている場合はtrue、そうでなければfalse</returns>
    public static bool IsSupportedArchiveType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var supportedTypes = new[] { ".zip", ".7z", ".tar", ".gz", ".bz2", ".lzma", ".xz", ".rar", ".lzh", ".cab", ".arj", ".z" };
        return supportedTypes.Contains(extension);
    }

    /// <summary>
    /// 指定されたファイルが展開専用形式（圧縮不可）かどうかを判定する
    /// RAR、LZH、CAB、ARJ、Z形式は展開のみサポート
    /// </summary>
    /// <param name="filePath">チェックするファイルのパス</param>
    /// <returns>展開専用形式の場合はtrue、そうでなければfalse</returns>
    public static bool IsExtractOnlyType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var extractOnlyTypes = new[] { ".rar", ".lzh", ".cab", ".arj", ".z" };
        return extractOnlyTypes.Contains(extension);
    }
}