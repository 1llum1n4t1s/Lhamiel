using Cube.FileSystem.SevenZip;
using CompressionMethod = Cube.FileSystem.SevenZip.CompressionMethod;

namespace Lhamiel.Util;

/// <summary>
/// アーカイブ圧縮機能
/// </summary>
public class ArchiveCompressor
{
    /// <summary>
    /// ライブラリがサポートする圧縮可能な全形式（内部バリデーション用）
    /// </summary>
    internal static readonly HashSet<string> SupportedCompressionFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "zip", "7z", "tar", "gz", "bz2", "xz"
    };

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

        var trimmedPath = Path.TrimEndingDirectorySeparator(sourcePath);
        var name = Path.GetFileName(trimmedPath);
        // ドットで始まるフォルダ名（.cursor など）は GetFileNameWithoutExtension が空を返すため、
        // ファイル（拡張子あり）の場合のみ拡張子を除去する
        var fileName = Directory.Exists(sourcePath) ? name : (Path.GetFileNameWithoutExtension(trimmedPath) is { Length: > 0 } stem ? stem : name);

        var lowerExtension = extension.ToLowerInvariant();

        return Path.Combine(directory, $"{fileName}.{lowerExtension}");
    }

    /// <summary>
    /// 設定文字列の圧縮形式を Format enum に直接変換する
    /// </summary>
    /// <param name="format">圧縮形式の文字列（"ZIP", "7z", "TAR" など）</param>
    /// <returns>対応する Format enum 値</returns>
    public static Format ParseFormat(string format) => format.ToUpperInvariant() switch
    {
        "ZIP" => Format.Zip,
        "7Z" => Format.SevenZip,
        "TAR" => Format.Tar,
        "GZ" => Format.GZip,
        "BZ2" => Format.BZip2,
        "XZ" => Format.XZ,
        _ => Format.Zip
    };

    /// <summary>
    /// ファイルを圧縮する
    /// </summary>
    /// <param name="sourcePaths">圧縮するファイル・フォルダのパス</param>
    /// <param name="outputPath">出力アーカイブのパス</param>
    /// <param name="format">圧縮形式</param>
    /// <param name="progressCallback">進捗コールバック</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public static async Task CompressFilesAsync(IEnumerable<string> sourcePaths, string outputPath, Format format, Action<ProgressInfo>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        var sourceList = sourcePaths.ToList();
        if (sourceList.Count == 0)
        {
            throw new ArgumentException("圧縮するファイルが指定されていません。");
        }

        // 出力ディレクトリを作成
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // 設定から除外パターンを取得し、HashSet化してO(1)照合を実現
        var settings = SettingsManager.Instance.Current;
        var excludedPatternSet = new HashSet<string>(
            settings.ExcludedFilePatterns ?? [],
            StringComparer.OrdinalIgnoreCase);

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
                    if (!ShouldExcludeFile(sourcePath, excludedPatternSet))
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
                    var files = GetFilesRecursively(sourcePath, excludedPatternSet);
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
                    using var writer = CreateArchiveWriter(format, settings);

                    // ファイルを圧縮アーカイブに追加
                    foreach (var (fullPath, relativePath) in filesToCompress)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        writer.Add(fullPath, relativePath);
                    }

                    // 進捗スロットリング（UIスレッド負荷軽減用）
                    var throttler = new ProgressThrottler();

                    // 進捗報告オブジェクトを生成
                    using var reportProgress = new CancellableProgress<Report>(report =>
                    {
                        var percentage = (int)(report.GetRatio() * 100);
                        if (throttler.ShouldReport(percentage))
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
    /// <param name="settings">設定オブジェクト</param>
    /// <param name="maxThreads">最大スレッド数（0または負の値で自動設定）</param>
    /// <returns>ArchiveWriterインスタンス</returns>
    private static ArchiveWriter CreateArchiveWriter(Format format, Settings settings, int maxThreads = -1)
    {
        // デフォルトはプロセッサ数、制限がある場合はその値
        var threadCount = maxThreads > 0 ? maxThreads : Environment.ProcessorCount;

        // 形式に応じたオプションを設定
        if (format == Format.SevenZip)
        {
            // 7z形式: LZMA2 + スレッド数制御
            var options = new CompressionOption
            {
                CompressionLevel = (CompressionLevel)settings.SevenZipCompressionLevel,
                CompressionMethod = CompressionMethod.Lzma2,
                ThreadCount = threadCount
            };
            return new ArchiveWriter(format, options);
        }
        if (format == Format.Zip)
        {
            // ZIP形式: UTF-8エンコーディング
            var options = new CompressionOption
            {
                CompressionLevel = (CompressionLevel)settings.ZipCompressionLevel,
                CompressionMethod = CompressionMethod.Deflate,
                ThreadCount = threadCount,
                CodePage = CodePage.Utf8
            };
            return new ArchiveWriter(format, options);
        }
        // TAR形式など、その他の形式ではオプションを設定しない
        return new ArchiveWriter(format);
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
    /// HashSet による O(1) 照合でパスセグメント数に対して O(n) で完了する
    /// </summary>
    /// <param name="path">チェックするパス</param>
    /// <param name="excludedPatternSet">除外パターンの HashSet（大文字小文字無視）</param>
    /// <returns>除外すべき場合はtrue</returns>
    internal static bool ShouldExcludeFile(string path, HashSet<string> excludedPatternSet)
    {
        if (excludedPatternSet.Count == 0)
        {
            return false;
        }

        // パスセグメントを走査し、いずれかが除外パターンに一致すればtrue
        // ファイル名もパスセグメントの一部なので、個別チェック不要
        // MemoryExtensions.Split + stackalloc で配列アロケーションを回避
        ReadOnlySpan<char> separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
        Span<Range> ranges = stackalloc Range[64]; // パスセグメント数の上限（通常十分）
        var count = path.AsSpan().SplitAny(ranges, separators, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < count; i++)
        {
            if (excludedPatternSet.Contains(path[ranges[i]]))
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
    /// <param name="excludedPatternSet">除外パターンの HashSet</param>
    /// <returns>ファイルパスのリスト</returns>
    private static IEnumerable<string> GetFilesRecursively(string directoryPath, HashSet<string> excludedPatternSet)
    {
        try
        {
            // ディレクトリ自体が除外対象かチェック
            if (ShouldExcludeFile(directoryPath, excludedPatternSet))
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
                .Where(file => !ShouldExcludeFile(file, excludedPatternSet));
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
