using Cube.FileSystem.SevenZip;
using CompressionMethod = Cube.FileSystem.SevenZip.CompressionMethod;

namespace Lhamiel.Util;

/// <summary>
/// アーカイブ圧縮機能
/// </summary>
public class ArchiveCompressor
{
    /// <summary>
    /// 一時コピーディレクトリのプレフィックス。
    /// ソーススキャンで出力先配下に残った一時ディレクトリを除外するために使用。
    /// </summary>
    private const string TempDirPrefix = "Lhamiel_compress_";

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
    /// <param name="resolvedFiles">衝突解決済みのファイルリスト（指定時はsourcePathsのスキャンをスキップ）</param>
    public static async Task CompressFilesAsync(IEnumerable<string> sourcePaths, string outputPath, Format format, Action<ProgressInfo>? progressCallback = null, CancellationToken cancellationToken = default, List<(string fullPath, string relativePath)>? resolvedFiles = null)
    {
        var sourceList = sourcePaths.ToList();
        if (sourceList.Count == 0)
        {
            throw new ArgumentException(App.Text("Error.NoFilesToCompress"));
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
        var tempDir = string.Empty;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 解決済みリストが渡された場合はそのまま使用、なければスキャン
            var filesToCompress = resolvedFiles ?? await ScanSourceFiles(sourceList, excludedPatternSet, cancellationToken);

            Logger.Log($"圧縮対象のファイル総数: {filesToCompress.Count}個");

            // 一時コピー準備中を通知（マーキー表示）
            progressCallback?.Invoke(new ProgressInfo(App.Text("Progress.PreparingFiles")));

            // 全ファイルを一時ディレクトリにコピー（ロック中ファイルも読み取り可能にする）
            (filesToCompress, tempDir) = await CopyFilesToTempAsync(filesToCompress, cancellationToken, outputPath);

            // 一時コピー完了、圧縮に移行
            progressCallback?.Invoke(new ProgressInfo(0, "圧縮処理中..."));

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

                    // ファイルとディレクトリを圧縮アーカイブに追加
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
                            progressCallback?.Invoke(new ProgressInfo(percentage, ""));
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
        finally
        {
            // ロック中ファイルの一時コピーを削除
            if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (Exception ex)
                {
                    Logger.Log($"一時ディレクトリの削除に失敗しました: {tempDir}, {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// ソースパスからファイルリストを構築する（まとめ圧縮の前段階）。
    /// 戻り値は (fullPath, relativePath) のリスト。衝突解決はまだ行われない。
    /// </summary>
    public static async Task<List<(string fullPath, string relativePath)>> ScanSourceFiles(
        List<string> sourceList, HashSet<string> excludedPatternSet, CancellationToken cancellationToken = default,
        DirectoryStructureMode? dirModeOverride = null)
    {
        var filesToCompress = new List<(string fullPath, string relativePath)>();
        var dirMode = dirModeOverride ?? SettingsManager.Instance.Current.DirectoryStructureMode;

        foreach (var sourcePath in sourceList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(sourcePath))
            {
                if (!ShouldExcludeFile(sourcePath, excludedPatternSet))
                {
                    filesToCompress.Add((sourcePath, Path.GetFileName(sourcePath)));
                }
            }
            else if (Directory.Exists(sourcePath))
            {
                Logger.Log($"ディレクトリをスキャン中: {sourcePath}");

                var files = GetFilesRecursively(sourcePath, excludedPatternSet);
                var parentDir = dirMode == DirectoryStructureMode.IncludeRoot
                    ? (Path.GetDirectoryName(sourcePath) ?? "")
                    : sourcePath;

                // ファイルが存在するディレクトリを記録（空ディレクトリ検出用）
                var directoriesWithFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var fileCount = 0;
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var relativePath = dirMode == DirectoryStructureMode.Flat
                        ? Path.GetFileName(file)
                        : Path.GetRelativePath(parentDir, file);
                    filesToCompress.Add((file, relativePath));

                    // このファイルの全祖先ディレクトリを記録
                    var fileDir = Path.GetDirectoryName(file);
                    while (fileDir != null && fileDir.Length >= sourcePath.Length)
                    {
                        directoriesWithFiles.Add(fileDir);
                        fileDir = Path.GetDirectoryName(fileDir);
                    }

                    fileCount++;
                    if (fileCount % 100 == 0)
                    {
                        await Task.Yield();
                    }
                }

                // Flatモードでなければ空ディレクトリを収集してエントリに追加
                if (dirMode != DirectoryStructureMode.Flat)
                {
                    var emptyDirs = CollectEmptyDirectories(sourcePath, excludedPatternSet, directoriesWithFiles);
                    foreach (var emptyDir in emptyDirs)
                    {
                        var relativePath = Path.GetRelativePath(parentDir, emptyDir);
                        // ディレクトリエントリは末尾に / を付与（アーカイブ仕様）
                        filesToCompress.Add((emptyDir, relativePath + "/"));
                    }

                    if (emptyDirs.Count > 0)
                        Logger.Log($"空ディレクトリ: {emptyDirs.Count}個を追加");
                }

                Logger.Log($"スキャン完了: {fileCount}個のファイルが見つかりました");
            }
            else
            {
                throw new FileNotFoundException(App.Text("Error.PathNotFound", sourcePath));
            }
        }

        return filesToCompress;
    }

    /// <summary>
    /// ファイルリストから衝突グループを検出する。
    /// 衝突がない場合は空リストを返す。
    /// </summary>
    public static List<Models.FileConflictGroup> DetectConflicts(List<(string fullPath, string relativePath)> files)
    {
        var groups = files
            .GroupBy(f => f.relativePath, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => new Models.FileConflictGroup
            {
                ConflictingName = g.Key,
                Entries = g.Select(f =>
                {
                    var info = new FileInfo(f.fullPath);
                    return new Models.FileConflictEntry(
                        f.fullPath,
                        f.relativePath,
                        info.Exists ? info.Length : 0,
                        info.Exists ? info.LastWriteTime : DateTime.MinValue);
                }).ToList()
            })
            .ToList();

        return groups;
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
    /// アーカイブ内の相対パスが衝突するファイルを解決する
    /// </summary>
    /// <param name="files">ファイルリスト（fullPath, relativePath）</param>
    /// <param name="preservePath">true: 親フォルダ名をプレフィックスとして付与、false: 連番サフィックスで自動リネーム</param>
    /// <returns>衝突が解決されたファイルリスト</returns>
    internal static List<(string fullPath, string relativePath)> ResolveRelativePathConflicts(
        List<(string fullPath, string relativePath)> files, bool preservePath)
    {
        // 衝突があるか高速チェック（大半のケースはここで終了）
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasConflict = false;
        foreach (var (_, relativePath) in files)
        {
            if (!seen.Add(relativePath))
            {
                hasConflict = true;
                break;
            }
        }

        if (!hasConflict)
            return files;

        Logger.Log($"アーカイブ内の相対パスに衝突を検出。解決方式: {(preservePath ? "パス保持" : "リネーム")}");

        if (preservePath)
            return ResolveByPreservingPath(files);

        return ResolveByRenaming(files);
    }

    /// <summary>
    /// パス保持方式: 親フォルダ名をプレフィックスとして付与して衝突を解決
    /// </summary>
    private static List<(string fullPath, string relativePath)> ResolveByPreservingPath(
        List<(string fullPath, string relativePath)> files)
    {
        var result = new List<(string fullPath, string relativePath)>(files.Count);
        foreach (var (fullPath, relativePath) in files)
        {
            // 既にサブフォルダ付き（ディレクトリスキャン由来）ならそのまま
            if (relativePath.Contains(Path.DirectorySeparatorChar) || relativePath.Contains(Path.AltDirectorySeparatorChar))
            {
                result.Add((fullPath, relativePath));
                continue;
            }

            // ファイル単体: 親フォルダ名をプレフィックスとして付与
            var parentDir = Path.GetDirectoryName(fullPath);
            var parentName = parentDir != null ? Path.GetFileName(parentDir) : "";
            var newRelativePath = string.IsNullOrEmpty(parentName)
                ? relativePath
                : Path.Combine(parentName, relativePath);

            result.Add((fullPath, newRelativePath));
        }

        return result;
    }

    /// <summary>
    /// 複合拡張子（.tar.gz 等）を考慮してファイル名をステムと拡張子に分割する。
    /// 例: "archive.tar.gz" → ("archive", ".tar.gz")
    ///      "photo.jpg"      → ("photo", ".jpg")
    ///      "Makefile"       → ("Makefile", "")
    ///      ".gitignore"     → ("", ".gitignore")
    /// </summary>
    internal static (string stem, string extension) SplitStemAndExtension(string fileName)
    {
        // 既知の複合拡張子パターン
        ReadOnlySpan<string> compoundExtensions = [".tar.gz", ".tar.bz2", ".tar.xz", ".tar.lz", ".tar.zst"];

        foreach (var compoundExt in compoundExtensions)
        {
            if (fileName.EndsWith(compoundExt, StringComparison.OrdinalIgnoreCase) && fileName.Length > compoundExt.Length)
            {
                return (fileName[..^compoundExt.Length], fileName[^compoundExt.Length..]);
            }
        }

        // 通常の拡張子分割
        var ext = Path.GetExtension(fileName);
        var stem = ext.Length > 0 ? fileName[..^ext.Length] : fileName;
        return (stem, ext);
    }

    /// <summary>
    /// リネーム方式: 衝突するファイル名に連番サフィックスを付与して解決
    /// </summary>
    private static List<(string fullPath, string relativePath)> ResolveByRenaming(
        List<(string fullPath, string relativePath)> files)
    {
        var result = new List<(string fullPath, string relativePath)>(files.Count);
        var usedPaths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (fullPath, relativePath) in files)
        {
            var key = relativePath;
            if (!usedPaths.TryGetValue(key, out var count))
            {
                // 初出: そのまま使用
                usedPaths[key] = 1;
                result.Add((fullPath, relativePath));
            }
            else
            {
                // 衝突: 連番サフィックスを付与（001_1.jpg, 001_2.jpg, ...）
                var dir = Path.GetDirectoryName(relativePath) ?? "";
                var nameOnly = Path.GetFileName(relativePath);
                var (name, ext) = SplitStemAndExtension(nameOnly);
                string newPath;
                do
                {
                    newPath = string.IsNullOrEmpty(dir)
                        ? $"{name}_{count}{ext}"
                        : Path.Combine(dir, $"{name}_{count}{ext}");
                    count++;
                } while (usedPaths.ContainsKey(newPath));

                usedPaths[key] = count;
                usedPaths[newPath] = 1;
                result.Add((fullPath, newPath));
                Logger.Log($"同名ファイルをリネーム: {relativePath} → {newPath}");
            }
        }

        return result;
    }

    /// <summary>
    /// 出力ファイルパスの衝突を連番サフィックスで回避する
    /// </summary>
    /// <param name="outputPath">希望する出力パス</param>
    /// <returns>衝突しないユニークなパス</returns>
    public static string GetUniqueOutputPath(string outputPath)
    {
        if (!File.Exists(outputPath) && !Directory.Exists(outputPath))
            return outputPath;

        var dir = Path.GetDirectoryName(outputPath) ?? "";
        var nameOnly = Path.GetFileName(outputPath);
        var (name, ext) = SplitStemAndExtension(nameOnly);

        for (var i = 1; i < 10000; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException(App.Text("Error.UniqueNameFailed", outputPath));
    }

    /// <summary>
    /// ファイルを含まない空ディレクトリを再帰的に収集する
    /// </summary>
    /// <param name="rootDir">ルートディレクトリ</param>
    /// <param name="excludedPatternSet">除外パターン</param>
    /// <param name="directoriesWithFiles">ファイルが存在するディレクトリのセット</param>
    /// <returns>空ディレクトリのパスリスト</returns>
    private static List<string> CollectEmptyDirectories(string rootDir, HashSet<string> excludedPatternSet, HashSet<string> directoriesWithFiles)
    {
        var emptyDirs = new List<string>();
        try
        {
            var allDirs = Directory.EnumerateDirectories(rootDir, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true
            });

            foreach (var dir in allDirs)
            {
                if (ShouldExcludeFile(dir, excludedPatternSet))
                    continue;

                // ファイルを含むディレクトリ（またはその祖先）でなければ空ディレクトリ
                if (!directoriesWithFiles.Contains(dir))
                    emptyDirs.Add(dir);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"空ディレクトリ収集中にエラー: {ex.Message}");
        }

        return emptyDirs;
    }

    /// <summary>
    /// 全ファイルを一時ディレクトリにコピーし、コピー先パスに差し替えたリストを返す。
    /// FileShare.ReadWrite | FileShare.Delete で開くため、プロセスにロックされたファイルも読み取れる。
    /// ファイルが0件またはディレクトリエントリのみの場合は一時ディレクトリを作成しない。
    /// </summary>
    private static async Task<(List<(string fullPath, string relativePath)> files, string tempDir)> CopyFilesToTempAsync(
        List<(string fullPath, string relativePath)> files, CancellationToken cancellationToken, string? outputPath = null)
    {
        var tempDir = string.Empty;
        var result = new List<(string fullPath, string relativePath)>(files.Count);

        // ファイルのみのリストとディレクトリエントリを分離
        var fileEntries = new List<(string fullPath, string relativePath)>();
        foreach (var (fullPath, relativePath) in files)
        {
            if (relativePath.EndsWith('/'))
                result.Add((fullPath, relativePath));
            else
                fileEntries.Add((fullPath, relativePath));
        }

        if (fileEntries.Count == 0)
            return (result, tempDir);

        // ファイルサイズを事前集計（ChooseTempBase のドライブ容量チェックで使用）
        var totalFileSize = 0L;
        foreach (var (fullPath, _) in fileEntries)
        {
            try { totalFileSize += new FileInfo(fullPath).Length; }
            catch { /* アクセス不可のファイルは無視 */ }
        }

        // 一時ディレクトリを出力先と同一ドライブに作成（ドライブ跨ぎI/Oを回避）
        // ただし出力先ドライブの空き容量が不足する場合は %TEMP% にフォールバック
        var tempBase = ChooseTempBase(outputPath, fileEntries, totalFileSize);
        tempDir = Path.Combine(tempBase, $"Lhamiel_compress_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // 並列コピー（Parallel.ForEachAsync で同時実行数を制限、タスク数の爆発を防止）
            // I/Oバウンドのため CPU コア数の半分（最低2、最大8）を使用
            var maxParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
            var copyResults = new (string destPath, string relativePath)[fileEntries.Count];

            await Parallel.ForEachAsync(
                Enumerable.Range(0, fileEntries.Count),
                new ParallelOptions { MaxDegreeOfParallelism = maxParallelism, CancellationToken = cancellationToken },
                async (i, ct) =>
                {
                    var (fullPath, relativePath) = fileEntries[i];

                    // relative パスのサブディレクトリ構造を保持してコピー
                    var destPath = Path.Combine(tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir); // CreateDirectory は既存なら何もしない

                    // FileShare.ReadWrite | FileShare.Delete で開くため、ロック中ファイルも読み取り可能
                    await using var src = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete, bufferSize: 81920, useAsync: true);
                    await using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write,
                        FileShare.None, bufferSize: 81920, useAsync: true);
                    await src.CopyToAsync(dst, ct);
                    copyResults[i] = (destPath, relativePath);
                });

            result.AddRange(copyResults);
        }
        catch
        {
            // キャンセル等で途中終了した場合、作成済みの一時ディレクトリをクリーンアップする
            if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); }
                catch (Exception ex) { Logger.Log($"一時ディレクトリの緊急削除に失敗: {tempDir}, {ex.Message}"); }
            }
            throw;
        }

        return (result, tempDir);
    }

    /// <summary>
    /// 一時コピー先のベースディレクトリを選択する。
    /// 出力先と同一ドライブに十分な空きがあればそちらを使い、
    /// なければ %TEMP% にフォールバックする。
    /// </summary>
    private static string ChooseTempBase(string? outputPath, List<(string fullPath, string relativePath)> fileEntries, long totalFileSize)
    {
        var fallback = Path.GetTempPath();

        if (string.IsNullOrEmpty(outputPath))
            return fallback;

        var outputDir = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
            return fallback;

        try
        {
            // 出力先ドライブの空き容量をチェック（一時コピー + アーカイブ出力の余裕を確保）
            var root = Path.GetPathRoot(outputDir);
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady && drive.AvailableFreeSpace > totalFileSize * 2)
                {
                    // outputDir 配下に一時ディレクトリ名 + 最長 relativePath を足した長さが
                    // MAX_PATH (260) を超える場合は %TEMP% にフォールバック
                    // 一時ディレクトリ名: "Lhamiel_compress_" + GUID(32) = 49文字
                    var maxRelLen = 0;
                    foreach (var (_, rel) in fileEntries)
                    {
                        if (rel.Length > maxRelLen) maxRelLen = rel.Length;
                    }
                    var estimatedMaxPath = outputDir.Length + 1 + 49 + 1 + maxRelLen;
                    if (estimatedMaxPath >= 260)
                    {
                        Logger.Log($"出力先パスが長すぎるため %TEMP% にフォールバック（推定最長: {estimatedMaxPath}文字）");
                        return fallback;
                    }

                    // サブフォルダ作成権限をプローブ（SMB共有等で Create Folders 権限がない場合に備える）
                    var probe = Path.Combine(outputDir, $".lhamiel_probe_{Guid.NewGuid():N}");
                    try
                    {
                        Directory.CreateDirectory(probe);
                        Directory.Delete(probe);
                        return outputDir;
                    }
                    catch
                    {
                        Logger.Log($"出力先ディレクトリにサブフォルダ作成不可、%TEMP% にフォールバック");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"ドライブ空き容量チェックに失敗、%TEMP% にフォールバック: {ex.Message}");
        }

        return fallback;
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
                .Where(file => !ShouldExcludeFile(file, excludedPatternSet) && !IsInsideTempDir(file));
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Log($"アクセス権限がありません: {directoryPath}, {ex.Message}");
            return [];
        }
        catch (IOException ex)
        {
            Logger.Log($"ファイル取得中にI/Oエラー: {directoryPath}, {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// パスが Lhamiel の一時コピーディレクトリ内にあるかチェックする。
    /// 出力先配下に残った一時ディレクトリ（cleanup失敗時の残骸や並列タスク）を
    /// ソーススキャンから除外するために使用。
    /// </summary>
    private static bool IsInsideTempDir(string path)
    {
        ReadOnlySpan<char> separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
        Span<Range> ranges = stackalloc Range[64];
        var count = path.AsSpan().SplitAny(ranges, separators, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < count; i++)
        {
            var segment = path.AsSpan()[ranges[i]];
            if (segment.StartsWith(TempDirPrefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
