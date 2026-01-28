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

        var trimmedPath = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fileName = Path.GetFileNameWithoutExtension(trimmedPath);
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = Path.GetFileName(trimmedPath);
        }

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
        var progressCallback = progress != null ? new Action<ProgressInfo>(p => progress.Report(p)) : null;
        await CompressFilesAsync([sourcePath], outputPath, progressCallback, cancellationToken);
    }

    /// <summary>
    /// ファイルを圧縮する
    /// </summary>
    /// <param name="sourcePaths">圧縮するファイル・フォルダのパス</param>
    /// <param name="outputPath">出力アーカイブのパス</param>
    /// <param name="progressCallback">進捗コールバック</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public static async Task CompressFilesAsync(IEnumerable<string> sourcePaths, string outputPath, Action<ProgressInfo>? progressCallback = null, CancellationToken cancellationToken = default)
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
        var excludedPatterns = settings.ExcludedFilePatterns ?? [];

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
                        // ファイル単体の場合はアーカイブのルートに配置
                        filesToCompress.Add((sourcePath, Path.GetFileName(sourcePath)));
                    }
                }
                else if (Directory.Exists(sourcePath))
                {
                    // ディレクトリの場合、再帰的にファイルを取得して個別に追加
                    Logger.Log($"ディレクトリをスキャン中: {sourcePath}");

                    // ファイルスキャンを非同期で処理（全件を即座にリスト化せず、遅延評価を活用）
                    var files = GetFilesRecursively(sourcePath, excludedPatterns);
                    var parentDir = Path.GetDirectoryName(sourcePath) ?? "";

                    var fileCount = 0;
                    foreach (var file in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // アーカイブ内のパスを計算（元のディレクトリ構造を保持）
                        var relativePath = Path.GetRelativePath(parentDir, file);
                        filesToCompress.Add((file, relativePath));

                        fileCount++;
                        // 定期的に他のタスクに実行権を譲る
                        if (fileCount % 100 == 0)
                        {
                            await Task.Yield();
                        }
                    }

                    Logger.Log($"スキャン完了: {fileCount}個のファイルが見つかりました");
                }
                else
                {
                    throw new FileNotFoundException($"指定されたパスが見つかりません: {sourcePath}");
                }
            }

            Logger.Log($"圧縮対象のファイル総数: {filesToCompress.Count}個");

            // 圧縮を実行（IProgress<Report>で詳細な進捗を取得）
            outputCreated = true;
            Logger.Log("圧縮処理を開始します");

            try
            {
                // 重い処理全体を Task.Run で実行
                await Task.Run(() =>
                {
                    // ネイティブ側（7z.dll）との連携を確実に保護するため
                    // 全ての主要オブジェクトを Task.Run の内部スコープで管理する
                    using var writer = CreateArchiveWriter(format);

                    // ファイルを圧縮アーカイブに追加
                    foreach (var (fullPath, relativePath) in filesToCompress)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        writer.Add(fullPath, relativePath);
                    }

                    // 変数: 最後に報告した進捗率と時間（UIスレッドの負荷軽減用）
                    var lastPercentage = -1;
                    var lastReportTime = Environment.TickCount64;
                    const int reportInterval = 100; // 100ms間隔
                    // 進捗コールバックが複数スレッドから呼ばれる可能性に備えた同期用オブジェクト
                    var progressLock = new object();

                    // 進捗報告オブジェクトを生成
                    using var reportProgress = new CancellableProgress<Report>(report =>
                    {
                        // 進捗率を取得（ライブラリの GetRatio() と Report を信じる）
                        var ratio = report.GetRatio();
                        var percentage = (int)(ratio * 100);

                        lock (progressLock)
                        {
                            // 単調増加を保証（Ice アプリケーションの実装パターンに準拠）
                            if (percentage <= lastPercentage && percentage > 0 && percentage < 100)
                            {
                                return;
                            }

                            var currentTime = Environment.TickCount64;

                            // 以下のいずれかの条件を満たす場合のみ報告
                            // 1. 進捗が 0% または 100% (開始と完了を保証)
                            // 2. 前回の報告から 100ms 以上経過しており、かつ進捗率が変化している
                            if (percentage > 0 && percentage < 100)
                            {
                                if (percentage == lastPercentage) return;
                                if (currentTime - lastReportTime < reportInterval) return;
                            }

                            lastPercentage = percentage;
                            lastReportTime = currentTime;
                        }

                        progressCallback?.Invoke(new ProgressInfo(percentage, "圧縮処理中..."));
                    }, cancellationToken);

                    // ネイティブメソッドの呼び出し
                    writer.Save(outputPath, reportProgress);

                    // キャンセルされていたらここで一度だけスロー（コールバック内ではスローしない）
                    cancellationToken.ThrowIfCancellationRequested();

                    // Terminate で 100% を保証（Ice アプリケーションの実装パターンに準拠）
                    progressCallback?.Invoke(new ProgressInfo(100, "圧縮処理中..."));

                    // 全てのオブジェクトの生存を、ネイティブ処理完了直後に明示的に保証する
                    // これにより、JIT最適化による早期解放（およびそれに伴うアクセス違反）を防ぐ
                    NativeInteropHelper.KeepAliveCallbacks(writer, reportProgress, progressCallback);
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.Log($"圧縮処理実行中にエラーが発生しました: {ex.Message}");
                throw;
            }

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
            var options = new CompressionOption
            {
                CompressionLevel = CompressionLevel.Ultra,
                CompressionMethod = CompressionMethod.Lzma2,
                ThreadCount = threadCount
            };
            return new ArchiveWriter(format, options);
        }
        else if (format == Format.Zip)
        {
            // ZIP形式: Normal圧縮レベル + UTF-8エンコーディング
            var options = new CompressionOption
            {
                CompressionLevel = CompressionLevel.Normal,
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
            ".gz" => Format.GZip,
            ".tgz" => Format.GZip,
            ".bz2" => Format.BZip2,
            ".xz" => Format.XZ,
            _ => Format.Zip // デフォルトはZIP
        };
    }

    /// <summary>
    /// 同名フォルダが存在するかチェックする
    /// </summary>
    /// <param name="archivePath">アーカイブパス</param>
    /// <param name="folderName">チェックするフォルダ名</param>
    /// <returns>同名フォルダが存在するかどうか</returns>
    public static bool HasFolderWithSameName(string archivePath, string folderName)
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
                return [];
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

        return [];
    }
}
