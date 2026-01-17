using Cube.FileSystem.SevenZip;
using System.IO;
using CompressionMethod = Cube.FileSystem.SevenZip.CompressionMethod;

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
        var lowerExtension = extension.ToLowerInvariant();

        return Path.Combine(directory, $"{fileName}.{lowerExtension}");
    }

    /// <summary>
    /// ファイルを圧縮する（非同期版）
    /// </summary>
    /// <param name="sourcePath">圧縮するファイル・フォルダのパス</param>
    /// <param name="outputPath">出力アーカイブのパス</param>
    /// <param name="format">圧縮形式</param>
    /// <param name="progress">進捗コールバック</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>圧縮処理の完了を表すTask</returns>
    public static async Task CompressAsync(string sourcePath, string outputPath, string format, IProgress<ProgressInfo>? progress = null, CancellationToken cancellationToken = default)
    {
        var compressor = new ArchiveCompressor();
        var progressCallback = progress != null ? new Action<ProgressInfo>(p => progress.Report(p)) : null;
        await compressor.CompressFilesAsync(new[] { sourcePath }, outputPath, progressCallback, cancellationToken);
    }

    /// <summary>
    /// ファイルを圧縮する
    /// </summary>
    /// <param name="sourcePaths">圧縮するファイル・フォルダのパス</param>
    /// <param name="outputPath">出力アーカイブのパス</param>
    /// <param name="progressCallback">進捗コールバック</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public async Task CompressFilesAsync(IEnumerable<string> sourcePaths, string outputPath, Action<ProgressInfo>? progressCallback = null, CancellationToken cancellationToken = default)
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

            // ファイルリストを先に準備
            var filesToCompress = new List<(string fullPath, string relativePath)>();

            foreach (var sourcePath in sourceList)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(sourcePath))
                {
                    // ファイルが除外対象でない場合のみ追加
                    if (!ShouldExcludeFile(sourcePath, excludedPatterns))
                    {
                        filesToCompress.Add((sourcePath, sourcePath));
                    }
                }
                else if (Directory.Exists(sourcePath))
                {
                    // ディレクトリの場合、再帰的にファイルを取得して個別に追加
                    Logger.Log($"ディレクトリをスキャン中: {sourcePath}");

                    // ファイルスキャンを非同期で処理
                    var files = await Task.Run(() => GetFilesRecursively(sourcePath, excludedPatterns).ToList(), cancellationToken);
                    var parentDir = Path.GetDirectoryName(sourcePath) ?? "";

                    Logger.Log($"スキャン完了: {files.Count}個のファイルが見つかりました");

                    for (var i = 0; i < files.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var file = files[i];
                        // アーカイブ内のパスを計算（元のディレクトリ構造を保持）
                        var relativePath = Path.GetRelativePath(parentDir, file);
                        filesToCompress.Add((file, relativePath));

                        // 定期的にUIスレッドに処理を戻す
                        if (i % 100 == 0)
                        {
                            await Task.Delay(0, cancellationToken);
                        }
                    }
                }
                else
                {
                    throw new FileNotFoundException($"指定されたパスが見つかりません: {sourcePath}");
                }
            }

            Logger.Log($"圧縮対象のファイル総数: {filesToCompress.Count}個");

            // ArchiveWriterを使用して圧縮（形式に応じてオプションを設定）
            using var writer = CreateArchiveWriter(format);

            // ファイルを圧縮アーカイブに追加
            var totalFiles = filesToCompress.Count;
            Logger.Log($"圧縮するファイル数: {totalFiles}");

            foreach (var (fullPath, relativePath) in filesToCompress)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.Add(fullPath, relativePath);
            }

            // 圧縮を実行（IProgress<Report>で詳細な進捗を取得）
            outputCreated = true;
            Logger.Log("圧縮処理を開始します");

            var reportProgress = new Progress<Report>(report =>
            {
                // 進捗率をそのまま使用（GetRatio()で0～1を返す）
                var ratio = report.GetRatio();
                var percentage = (int)(ratio * 100);

                progressCallback?.Invoke(new ProgressInfo(percentage, "圧縮処理中..."));
            });

            await Task.Run(() =>
            {
                writer.Save(outputPath, reportProgress);
            }, cancellationToken);

            Logger.Log($"圧縮完了: {outputPath}（{filesToCompress.Count}個のファイル）");
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
    /// ファイルを圧縮する（同期版、互換性保持用）
    /// </summary>
    /// <param name="sourcePaths">圧縮するファイル・フォルダのパス</param>
    /// <param name="outputPath">出力アーカイブのパス</param>
    /// <param name="progressCallback">進捗コールバック</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public void CompressFiles(IEnumerable<string> sourcePaths, string outputPath, Action<ProgressInfo>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        CompressFilesAsync(sourcePaths, outputPath, progressCallback, cancellationToken).GetAwaiter().GetResult();
    }

    /// <summary>
    /// ArchiveWriterを作成する（スレッド数制御追加）
    /// </summary>
    /// <param name="format">圧縮形式</param>
    /// <param name="maxThreads">最大スレッド数（0または負の値で自動設定）</param>
    /// <returns>ArchiveWriterインスタンス</returns>
    private static ArchiveWriter CreateArchiveWriter(Format format, int maxThreads = -1)
    {
        // デフォルトはプロセッサ数、制限がある場合はその値
        var threadCount = maxThreads > 0 ? maxThreads : Environment.ProcessorCount;

        // 形式に応じたオプションを設定
        if (format == Format.SevenZip)
        {
            // 7z形式: Normal圧縮レベル + LZMA2 + スレッド数制御
            // ★ 修正: Ultraは圧縮率向上に対して時間・メモリコストが大きいため、Normalに変更
            // メモリ消費を抑えてアプリケーションのフリーズを回避しつつ、良好な圧縮率を維持
            var options = new CompressionOption
            {
                CompressionLevel = CompressionLevel.Normal,
                CompressionMethod = CompressionMethod.Lzma2,
                ThreadCount = threadCount
            };
            return new ArchiveWriter(format, options);
        }
        else if (format == Format.Zip)
        {
            // ZIP形式: Fastest圧縮レベル + UTF-8エンコーディング
            var options = new CompressionOption
            {
                CompressionLevel = CompressionLevel.Fast,
                CompressionMethod = CompressionMethod.Deflate,
                ThreadCount = threadCount,
                CodePage = CodePage.Utf8
            };
            return new ArchiveWriter(format, options);
        }
        else
        {
            // TAR形式など、その他の形式ではオプションを設定しない
            return new ArchiveWriter(format);
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
            _ => Format.Zip // デフォルトはZIP
        };
    }

    /// <summary>
    /// ディレクトリを圧縮する
    /// </summary>
    /// <param name="directoryPath">圧縮するディレクトリのパス</param>
    /// <param name="outputPath">出力アーカイブのパス</param>
    /// <param name="progressCallback">進捗コールバック</param>
    public static void CompressDirectory(string directoryPath, string outputPath, Action<ProgressInfo>? progressCallback = null)
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
            // 1. 圧縮対象のファイルを再帰的に取得（除外パターンを適用）
            var files = GetFilesRecursively(directoryPath, excludedPatterns).ToList();
            var totalFiles = files.Count;
            Logger.Log($"圧縮対象のファイル総数: {totalFiles}個");

            // アーカイブ内の相対パスを計算（ディレクトリ自体をルートに含める）
            var parentDir = Path.GetDirectoryName(directoryPath) ?? "";
            var filesToCompress = files.Select(f => (fullPath: f, relativePath: Path.GetRelativePath(parentDir, f))).ToList();

            // 2. その他の形式（ArchiveWriterを使用）
            using var writer = CreateArchiveWriter(format);

            foreach (var item in filesToCompress)
            {
                writer.Add(item.fullPath, item.relativePath);
            }

            // 圧縮を実行（IProgress<Report>で詳細な進捗を取得）
            Logger.Log("圧縮処理を開始します");

            var reportProgress = new Progress<Report>(report =>
            {
                // 進捗率をそのまま使用（GetRatio()で0～1を返す）
                var ratio = report.GetRatio();
                var percentage = (int)(ratio * 100);

                progressCallback?.Invoke(new ProgressInfo(percentage, "圧縮処理中..."));
            });

            writer.Save(outputPath, reportProgress);

            progressCallback?.Invoke(new ProgressInfo(100, "完了"));
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
        try
        {
            // ディレクトリ自体が除外対象かチェック
            if (ShouldExcludeFile(directoryPath, excludedPatterns))
            {
                return Enumerable.Empty<string>();
            }

            // Directory.EnumerateFiles を使用して効率的にファイルを取得
            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true // 権限エラーで止まらないようにする
            };

            return Directory.EnumerateFiles(directoryPath, "*", enumerationOptions)
                .Where(file => !ShouldExcludeFile(file, excludedPatterns));
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Log($"アクセス権限がありません: {directoryPath}, {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger.Log($"ファイル取得中にエラーが発生しました: {directoryPath}, {ex.Message}");
        }

        return Enumerable.Empty<string>();
    }
}
