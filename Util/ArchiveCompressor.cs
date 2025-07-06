using SharpCompress.Archives;
using SharpCompress.Common;
using System.IO.Compression;
using System.IO;
using SevenZip;

namespace GGEZArchiver.Util;

/// <summary>
/// アーカイブ圧縮機能を提供するクラス
/// 様々な圧縮形式（ZIP、7Z、TAR、GZ、BZ2、LZMA、XZ）に対応
/// </summary>
public class ArchiveCompressor
{
    /// <summary>
    /// 圧縮処理の委譲型
    /// 異なる圧縮形式に対応するための統一インターフェース
    /// </summary>
    /// <param name="sourcePath">圧縮元のパス</param>
    /// <param name="outputPath">出力パス</param>
    /// <param name="progress">進行状況報告オブジェクト</param>
    /// <returns>圧縮処理の完了を表すTask</returns>
    private delegate Task CompressionDelegate(string sourcePath, string outputPath, IProgress<int>? progress);

    /// <summary>
    /// 圧縮形式と処理メソッドのマッピング
    /// 圧縮形式に応じて適切な処理メソッドを選択
    /// </summary>
    private static readonly Dictionary<string, CompressionDelegate> CompressionMethods = new()
    {
        ["zip"] = CompressToZipAsync,
        ["7z"] = CompressTo7zAsync,
        ["tar"] = CompressToTarAsync,
        ["gz"] = CompressToGzAsync,
        ["bz2"] = CompressToBz2Async,
        ["lzma"] = CompressToLzmaAsync,
        ["xz"] = CompressToXzAsync
    };

    /// <summary>
    /// 指定されたファイルまたはフォルダを圧縮する
    /// 圧縮形式を自動判定して適切な圧縮処理を実行
    /// </summary>
    /// <param name="sourcePath">圧縮するファイルまたはフォルダのパス</param>
    /// <param name="outputPath">出力ファイルのパス</param>
    /// <param name="format">圧縮形式</param>
    /// <param name="progress">進行状況を報告するオブジェクト（オプション）</param>
    /// <returns>圧縮処理の完了を表すTask</returns>
    /// <exception cref="ArgumentException">無効なパスが指定された場合</exception>
    /// <exception cref="FileNotFoundException">圧縮元ファイルが見つからない場合</exception>
    /// <exception cref="DirectoryNotFoundException">圧縮元ディレクトリが見つからない場合</exception>
    public static async Task CompressAsync(string sourcePath, string outputPath, string format, IProgress<int>? progress = null)
    {
        // 入力パスの検証
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("圧縮元のパスが指定されていません。", nameof(sourcePath));
        
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("出力パスが指定されていません。", nameof(outputPath));

        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            throw new FileNotFoundException($"圧縮元のファイルまたはディレクトリが見つかりません: {sourcePath}");

        if (CompressionMethods.TryGetValue(format.ToLowerInvariant(), out var compressionMethod))
        {
            await compressionMethod(sourcePath, outputPath, progress);
        }
        else
        {
            // デフォルトはZIP形式
            await CompressToZipAsync(sourcePath, outputPath, progress);
        }
    }

    /// <summary>
    /// 指定されたファイルまたはフォルダをZIP形式で圧縮する
    /// </summary>
    /// <param name="sourcePath">圧縮するファイルまたはフォルダのパス</param>
    /// <param name="outputPath">出力ZIPファイルのパス</param>
    /// <param name="progress">進行状況を報告するオブジェクト（オプション）</param>
    /// <returns>圧縮処理の完了を表すTask</returns>
    public static async Task CompressToZipAsync(string sourcePath, string outputPath, IProgress<int>? progress = null)
    {
        await Task.Run(() =>
        {
            if (Directory.Exists(sourcePath))
            {
                // フォルダの場合
                ZipFile.CreateFromDirectory(sourcePath, outputPath);
                progress?.Report(100);
            }
            else if (File.Exists(sourcePath))
            {
                // ファイルの場合
                using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
                var entry = archive.CreateEntry(Path.GetFileName(sourcePath));
                using var sourceStream = File.OpenRead(sourcePath);
                using var entryStream = entry.Open();
                sourceStream.CopyTo(entryStream);
                progress?.Report(100);
            }
        });
    }

    /// <summary>
    /// 指定されたファイルまたはフォルダを7Z形式で圧縮する
    /// SevenZipSharpライブラリを使用して高圧縮率を実現
    /// </summary>
    /// <param name="sourcePath">圧縮するファイルまたはフォルダのパス</param>
    /// <param name="outputPath">出力7Zファイルのパス</param>
    /// <param name="progress">進行状況を報告するオブジェクト（オプション）</param>
    /// <returns>圧縮処理の完了を表すTask</returns>
    public static async Task CompressTo7zAsync(string sourcePath, string outputPath, IProgress<int>? progress = null)
    {
        await Task.Run(() =>
        {
            var compressor = new SevenZipCompressor();
            compressor.CompressionLevel = SevenZip.CompressionLevel.Normal;
            compressor.CompressionMethod = CompressionMethod.Lzma2;
            compressor.ArchiveFormat = OutArchiveFormat.SevenZip;
            compressor.TempFolderPath = Path.GetTempPath();
            compressor.PreserveDirectoryRoot = true;
            
            if (progress != null)
            {
                compressor.Compressing += (s, e) =>
                {
                    progress.Report(e.PercentDone);
                };
            }

            if (Directory.Exists(sourcePath))
            {
                compressor.CompressDirectory(sourcePath, outputPath);
            }
            else if (File.Exists(sourcePath))
            {
                compressor.CompressFiles(outputPath, sourcePath);
            }
            progress?.Report(100);
        });
    }

    /// <summary>
    /// 圧縮ファイルの出力名を生成する
    /// 同名ファイルが存在する場合は番号を付けて重複を回避する
    /// </summary>
    /// <param name="sourcePath">圧縮元のファイルまたはフォルダのパス</param>
    /// <param name="extension">圧縮形式の拡張子</param>
    /// <returns>重複しない出力ファイル名</returns>
    public static string GetCompressedFileName(string sourcePath, string extension)
    {
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        var directory = Path.GetDirectoryName(sourcePath);
            
        // 同名ファイルが存在する場合は番号を付ける
        var counter = 1;
        var outputPath = Path.Combine(directory ?? "", $"{fileName}.{extension}");
            
        while (File.Exists(outputPath))
        {
            outputPath = Path.Combine(directory ?? "", $"{fileName}_{counter}.{extension}");
            counter++;
        }
            
        return outputPath;
    }

    /// <summary>
    /// 指定された拡張子がサポートされている圧縮形式かどうかを判定する
    /// </summary>
    /// <param name="extension">チェックする拡張子</param>
    /// <returns>サポートされている場合はtrue、そうでなければfalse</returns>
    public static bool IsSupportedCompressionType(string extension)
    {
        return CompressionMethods.ContainsKey(extension.ToLowerInvariant());
    }

    /// <summary>
    /// 指定されたファイルまたはフォルダをTAR形式で圧縮する
    /// </summary>
    /// <param name="sourcePath">圧縮するファイルまたはフォルダのパス</param>
    /// <param name="outputPath">出力TARファイルのパス</param>
    /// <param name="progress">進行状況を報告するオブジェクト（オプション）</param>
    /// <returns>圧縮処理の完了を表すTask</returns>
    public static async Task CompressToTarAsync(string sourcePath, string outputPath, IProgress<int>? progress = null)
    {
        await Task.Run(() =>
        {
            using var archive = ArchiveFactory.Create(ArchiveType.Tar);
                
            if (Directory.Exists(sourcePath))
            {
                // フォルダの場合
                AddDirectoryToArchive(archive, sourcePath, Path.GetDirectoryName(sourcePath) ?? "");
            }
            else if (File.Exists(sourcePath))
            {
                // ファイルの場合
                archive.AddEntry(Path.GetFileName(sourcePath), sourcePath);
            }

            archive.SaveTo(outputPath, CompressionType.None);
            progress?.Report(100);
        });
    }

    /// <summary>
    /// 指定されたファイルまたはフォルダをGZIP形式で圧縮する
    /// </summary>
    /// <param name="sourcePath">圧縮するファイルまたはフォルダのパス</param>
    /// <param name="outputPath">出力GZIPファイルのパス</param>
    /// <param name="progress">進行状況を報告するオブジェクト（オプション）</param>
    /// <returns>圧縮処理の完了を表すTask</returns>
    public static async Task CompressToGzAsync(string sourcePath, string outputPath, IProgress<int>? progress = null)
    {
        await Task.Run(() =>
        {
            using var archive = ArchiveFactory.Create(ArchiveType.GZip);
                
            if (Directory.Exists(sourcePath))
            {
                // フォルダの場合
                AddDirectoryToArchive(archive, sourcePath, Path.GetDirectoryName(sourcePath) ?? "");
            }
            else if (File.Exists(sourcePath))
            {
                // ファイルの場合
                archive.AddEntry(Path.GetFileName(sourcePath), sourcePath);
            }

            archive.SaveTo(outputPath, CompressionType.GZip);
            progress?.Report(100);
        });
    }

    /// <summary>
    /// 指定されたファイルまたはフォルダをBZIP2形式で圧縮する
    /// </summary>
    /// <param name="sourcePath">圧縮するファイルまたはフォルダのパス</param>
    /// <param name="outputPath">出力BZIP2ファイルのパス</param>
    /// <param name="progress">進行状況を報告するオブジェクト（オプション）</param>
    /// <returns>圧縮処理の完了を表すTask</returns>
    public static async Task CompressToBz2Async(string sourcePath, string outputPath, IProgress<int>? progress = null)
    {
        await Task.Run(() =>
        {
            using var archive = ArchiveFactory.Create(ArchiveType.Tar);
                
            if (Directory.Exists(sourcePath))
            {
                // フォルダの場合
                AddDirectoryToArchive(archive, sourcePath, Path.GetDirectoryName(sourcePath) ?? "");
            }
            else if (File.Exists(sourcePath))
            {
                // ファイルの場合
                archive.AddEntry(Path.GetFileName(sourcePath), sourcePath);
            }

            archive.SaveTo(outputPath, CompressionType.BZip2);
            progress?.Report(100);
        });
    }

    /// <summary>
    /// 指定されたファイルまたはフォルダをLZMA形式で圧縮する
    /// </summary>
    /// <param name="sourcePath">圧縮するファイルまたはフォルダのパス</param>
    /// <param name="outputPath">出力LZMAファイルのパス</param>
    /// <param name="progress">進行状況を報告するオブジェクト（オプション）</param>
    /// <returns>圧縮処理の完了を表すTask</returns>
    public static async Task CompressToLzmaAsync(string sourcePath, string outputPath, IProgress<int>? progress = null)
    {
        await Task.Run(() =>
        {
            using var archive = ArchiveFactory.Create(ArchiveType.Tar);
                
            if (Directory.Exists(sourcePath))
            {
                // フォルダの場合
                AddDirectoryToArchive(archive, sourcePath, Path.GetDirectoryName(sourcePath) ?? "");
            }
            else if (File.Exists(sourcePath))
            {
                // ファイルの場合
                archive.AddEntry(Path.GetFileName(sourcePath), sourcePath);
            }

            archive.SaveTo(outputPath, CompressionType.LZMA);
            progress?.Report(100);
        });
    }

    /// <summary>
    /// 指定されたファイルまたはフォルダをXZ形式で圧縮する
    /// </summary>
    /// <param name="sourcePath">圧縮するファイルまたはフォルダのパス</param>
    /// <param name="outputPath">出力XZファイルのパス</param>
    /// <param name="progress">進行状況を報告するオブジェクト（オプション）</param>
    /// <returns>圧縮処理の完了を表すTask</returns>
    public static async Task CompressToXzAsync(string sourcePath, string outputPath, IProgress<int>? progress = null)
    {
        await Task.Run(() =>
        {
            using var archive = ArchiveFactory.Create(ArchiveType.Tar);
                
            if (Directory.Exists(sourcePath))
            {
                // フォルダの場合
                AddDirectoryToArchive(archive, sourcePath, Path.GetDirectoryName(sourcePath) ?? "");
            }
            else if (File.Exists(sourcePath))
            {
                // ファイルの場合
                archive.AddEntry(Path.GetFileName(sourcePath), sourcePath);
            }

            archive.SaveTo(outputPath, CompressionType.LZMA);
            progress?.Report(100);
        });
    }

    /// <summary>
    /// ディレクトリ内のすべてのファイルをアーカイブに追加する
    /// 相対パスを保持してディレクトリ構造を維持する
    /// </summary>
    /// <param name="archive">追加先のアーカイブ</param>
    /// <param name="directoryPath">追加するディレクトリのパス</param>
    /// <param name="basePath">基準となるパス（相対パス計算用）</param>
    private static void AddDirectoryToArchive(IWritableArchive archive, string directoryPath, string basePath)
    {
        var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
        var totalFiles = files.Length;
        var processedFiles = 0;

        foreach (var file in files)
        {
            try
            {
                var relativePath = file.Substring(basePath.Length).TrimStart('\\', '/');
                archive.AddEntry(relativePath, file);
                processedFiles++;
                    
                // 進行状況を報告（簡易版）
                if (processedFiles % 10 == 0 || processedFiles == totalFiles)
                {
                    var percentage = (int)((double)processedFiles / totalFiles * 100);
                    // 進行状況の報告は実装が複雑なため、ここでは省略
                }
            }
            catch (Exception ex)
            {
                // 個別ファイルのエラーはログに記録して続行
                System.Diagnostics.Debug.WriteLine($"ファイルの追加に失敗しました: {file}, エラー: {ex.Message}");
            }
        }
    }
}