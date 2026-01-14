using System.IO;
using System.IO.Compression;
using Cube.FileSystem.SevenZip;
using Cube.FileSystem;
using System.Threading;
using System.Text;

namespace Lhamiel.Util;

/// <summary>
/// アーカイブ圧縮機能
/// </summary>
public class ArchiveCompressor
{
    /// <summary>
    /// 圧縮ファイル名を取得する
    /// </summary>
    /// <param name="sourcePath">圧縮対象のパス</param>
    /// <param name="extension">圧縮形式の拡張子</param>
    /// <param name="outputDirectory">出力ディレクトリ</param>
    /// <param name="outputToSameDirectory">同じディレクトリに出力するかどうか</param>
    /// <returns>圧縮ファイルのパス</returns>
    public static string GetCompressedFileName(string sourcePath, string extension, string outputDirectory = "", bool outputToSameDirectory = false)
    {
        var directory = outputToSameDirectory 
            ? Path.GetDirectoryName(sourcePath) ?? "" 
            : outputDirectory;
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        
        return Path.Combine(directory, $"{fileName}.{extension}");
    }

    /// <summary>
    /// ファイルを圧縮する（非同期版）
    /// </summary>
    /// <param name="sourcePath">圧縮するファイル・フォルダのパス</param>
    /// <param name="outputPath">出力アーカイブのパス</param>
    /// <param name="format">圧縮形式</param>
    /// <param name="progress">進捗コールバック</param>
    /// <returns>圧縮処理の完了を表すTask</returns>
    public static async Task CompressAsync(string sourcePath, string outputPath, string format, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var compressor = new ArchiveCompressor();
        await Task.Run(() =>
        {
            var progressCallback = progress != null ? new Action<int>(p => progress.Report(p)) : null;
            compressor.CompressFiles(new[] { sourcePath }, outputPath, progressCallback, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// ファイルを圧縮する
    /// </summary>
    /// <param name="sourcePaths">圧縮するファイル・フォルダのパス</param>
    /// <param name="outputPath">出力アーカイブのパス</param>
    /// <param name="progressCallback">進捗コールバック</param>
    public void CompressFiles(IEnumerable<string> sourcePaths, string outputPath, Action<int>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        var sourceList = sourcePaths.ToList();
        if (!sourceList.Any())
        {
            throw new ArgumentException("圧縮するファイルが指定されていません。");
        }

        // 出力ディレクトリを作成
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // 圧縮形式を決定
        var format = GetFormatFromExtension(outputPath);

        // 設定から除外パターンを取得
        var settings = Settings.Load();
        var excludedPatterns = settings.ExcludedFilePatterns ?? new List<string>();

        var outputCreated = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ArchiveWriterを使用して圧縮
            using var writer = new ArchiveWriter(format);

            // 圧縮対象のファイルとディレクトリを追加
            foreach (var sourcePath in sourceList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(sourcePath))
                {
                    // ファイルが除外対象でない場合のみ追加
                    if (!ShouldExcludeFile(sourcePath, excludedPatterns))
                    {
                        writer.Add(sourcePath);
                    }
                }
                else if (Directory.Exists(sourcePath))
                {
                    // ディレクトリの場合、再帰的にファイルを取得して個別に追加
                    var files = GetFilesRecursively(sourcePath, excludedPatterns);
                    var directoryName = Path.GetFileName(sourcePath);

                    foreach (var file in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // アーカイブ内のパスを計算（元のディレクトリ構造を保持）
                        var relativePath = Path.GetRelativePath(Path.GetDirectoryName(sourcePath) ?? "", file);
                        writer.Add(file, relativePath);
                    }
                }
                else
                {
                    throw new FileNotFoundException($"指定されたパスが見つかりません: {sourcePath}");
                }
            }

            // 進捗報告を設定
            progressCallback?.Invoke(0);

            // 圧縮を実行
            outputCreated = true;
            writer.Save(outputPath);

            // 完了時の進捗報告
            progressCallback?.Invoke(100);

            Logger.Log($"圧縮完了: {outputPath}");
        }
        catch (OperationCanceledException)
        {
            if (outputCreated && File.Exists(outputPath))
            {
                try
                {
                    File.Delete(outputPath);
                }
                catch (Exception ex)
                {
                    Logger.Log($"キャンセル時の一時ファイル削除に失敗しました: {outputPath}, {ex.Message}");
                }
            }

            throw;
        }
        catch (Exception ex)
        {
            Logger.Log($"圧縮でエラーが発生しました: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// ファイル拡張子から圧縮形式を取得する
    /// </summary>
    /// <param name="outputPath">出力ファイルパス</param>
    /// <returns>圧縮形式</returns>
    private static Format GetFormatFromExtension(string outputPath)
    {
        var extension = Path.GetExtension(outputPath).ToLowerInvariant();
        return extension switch
        {
            ".zip" => Format.Zip,
            ".7z" => Format.SevenZip,
            ".tar" => Format.Tar,
            ".gz" => Format.GZip,
            ".bz2" => Format.BZip2,
            ".xz" => Format.XZ,
            ".cab" => Format.Cab,
            ".wim" => Format.Wim,
            _ => Format.Zip // デフォルトはZIP
        };
    }

    /// <summary>
    /// ディレクトリを圧縮する
    /// </summary>
    /// <param name="directoryPath">圧縮するディレクトリのパス</param>
    /// <param name="outputPath">出力アーカイブのパス</param>
    /// <param name="progressCallback">進捗コールバック</param>
    public void CompressDirectory(string directoryPath, string outputPath, Action<int>? progressCallback = null)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"ディレクトリが見つかりません: {directoryPath}");
        }

        // 出力ディレクトリを作成
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // 圧縮形式を決定
        var format = GetFormatFromExtension(outputPath);

        // 設定から除外パターンを取得
        var settings = Settings.Load();
        var excludedPatterns = settings.ExcludedFilePatterns ?? new List<string>();

        try
        {
            // ArchiveWriterを使用して圧縮
            using var writer = new ArchiveWriter(format);

            // ディレクトリ内のファイルを再帰的に取得して個別に追加
            var files = GetFilesRecursively(directoryPath, excludedPatterns);
            var directoryName = Path.GetFileName(directoryPath);

            foreach (var file in files)
            {
                // アーカイブ内のパスを計算（元のディレクトリ構造を保持）
                var relativePath = Path.GetRelativePath(Path.GetDirectoryName(directoryPath) ?? "", file);
                writer.Add(file, relativePath);
            }

            // 進捗報告を設定
            progressCallback?.Invoke(0);

            // 圧縮を実行
            writer.Save(outputPath);

            // 完了時の進捗報告
            progressCallback?.Invoke(100);

            Logger.Log($"ディレクトリ圧縮完了: {directoryPath} -> {outputPath}");
        }
        catch (Exception ex)
        {
            Logger.Log($"ディレクトリ圧縮でエラーが発生しました: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 同名フォルダが存在するかチェックする
    /// </summary>
    /// <param name="archivePath">アーカイブパス</param>
    /// <param name="folderName">チェックするフォルダ名</param>
    /// <returns>同名フォルダが存在するかどうか</returns>
    public bool HasFolderWithSameName(string archivePath, string folderName)
    {
        if (!File.Exists(archivePath))
        {
            return false;
        }

        try
        {
            using var reader = new ArchiveReader(archivePath);

            // Items プロパティを使用してアーカイブ内容をチェック
            return reader.Items.Any(item =>
                item.IsDirectory &&
                string.Equals(item.FullName, folderName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Logger.Log($"フォルダ構造のチェックに失敗しました: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// ファイルが除外パターンに一致するかチェックする
    /// </summary>
    /// <param name="path">チェックするパス</param>
    /// <param name="excludedPatterns">除外パターンのリスト</param>
    /// <returns>除外すべき場合はtrue</returns>
    private static bool ShouldExcludeFile(string path, List<string> excludedPatterns)
    {
        if (excludedPatterns == null || !excludedPatterns.Any())
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        var pathSegments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var pattern in excludedPatterns)
        {
            // ファイル名が完全一致する場合
            if (string.Equals(fileName, pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // パス内に除外パターンが含まれる場合（__MACOSXフォルダなど）
            if (pathSegments.Any(segment => string.Equals(segment, pattern, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ディレクトリ内のファイルを再帰的に取得する（除外フィルタ適用）
    /// </summary>
    /// <param name="directoryPath">ディレクトリパス</param>
    /// <param name="excludedPatterns">除外パターンのリスト</param>
    /// <returns>ファイルパスのリスト</returns>
    private static IEnumerable<string> GetFilesRecursively(string directoryPath, List<string> excludedPatterns)
    {
        var files = new List<string>();

        try
        {
            // ディレクトリ自体が除外対象かチェック
            if (ShouldExcludeFile(directoryPath, excludedPatterns))
            {
                return files;
            }

            // 現在のディレクトリのファイルを取得
            foreach (var file in Directory.GetFiles(directoryPath))
            {
                if (!ShouldExcludeFile(file, excludedPatterns))
                {
                    files.Add(file);
                }
            }

            // サブディレクトリを再帰的に処理
            foreach (var directory in Directory.GetDirectories(directoryPath))
            {
                if (!ShouldExcludeFile(directory, excludedPatterns))
                {
                    files.AddRange(GetFilesRecursively(directory, excludedPatterns));
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Log($"アクセス権限がありません: {directoryPath}, {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger.Log($"ファイル取得中にエラーが発生しました: {directoryPath}, {ex.Message}");
        }

        return files;
    }
}
