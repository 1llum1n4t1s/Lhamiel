using Cube.FileSystem.SevenZip;
using CompressionMethod = Cube.FileSystem.SevenZip.CompressionMethod;

namespace Lhamiel.Util;

/// <summary>
/// アーカイブ圧縮機能
/// </summary>
public static class ArchiveCompressor
{
    /// <summary>
    /// ライブラリが書き込み可能な全形式（Settings.SupportedCompressionFormats はUI選択肢のサブセット）
    /// </summary>
    internal static readonly HashSet<string> WritableFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "zip", "7z", "tar", "gz", "bz2", "xz"
    };

    /// <summary>
    /// ファイルシステム上の絶対パス（fullPath）比較に使う OS 依存コンパラ。
    /// Windows は NTFS 既定で case-insensitive、Linux/macOS は case-sensitive なので、
    /// OrdinalIgnoreCase 固定にすると case-sensitive FS 上で `A.txt` と `a.txt` が
    /// 同一視されて別ファイルを取りこぼす/誤マージする問題が起きる。
    /// </summary>
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// <see cref="PathPairComparer"/> のステートレスな共有インスタンス。
    /// <see cref="DeduplicateByIdentity"/> のようなホットパスで毎回 new すると
    /// 無駄なアロケーションになるため、<see cref="PathComparer"/> と同じく静的にキャッシュする。
    /// </summary>
    private static readonly PathPairComparer DefaultPathPairComparer = new(PathComparer);

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
    /// <param name="progress">進捗レポータ。<see cref="ArchiveExtractor.ExtractArchiveAsync"/> と同じ <see cref="IProgress{T}"/> 契約に統一。</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <param name="resolvedFiles">衝突解決済みのファイルリスト（指定時はsourcePathsのスキャンをスキップ）</param>
    /// <param name="settingsOverride">使用する設定のスナップショット（並列処理時の race を避けるため呼び出し側で明示）</param>
    public static async Task CompressFilesAsync(IEnumerable<string> sourcePaths, string outputPath, Format format, IProgress<ProgressInfo>? progress = null, CancellationToken cancellationToken = default, List<(string fullPath, string relativePath)>? resolvedFiles = null, Settings? settingsOverride = null)
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

        // 設定スナップショットを取得（race を避けるため処理開始時点で1回だけ）
        var settings = settingsOverride ?? SettingsManager.Instance.CreateSnapshot();
        var excludedPatternSet = new HashSet<string>(
            settings.ExcludedFilePatterns ?? [],
            StringComparer.OrdinalIgnoreCase);

        var outputCreated = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 解決済みリストが渡された場合はそのまま使用、なければスキャン
            var filesToCompress = resolvedFiles ?? await ScanSourceFiles(sourceList, excludedPatternSet, cancellationToken, settings.DirectoryStructureMode, settings.NormalizeUnicodeFileNames);

            Logger.Log($"圧縮対象のファイル総数: {filesToCompress.Count}個");

            // 圧縮処理を開始（ロック中ファイルはライブラリ側で自動的に一時コピーされる）
            progress?.Report(new ProgressInfo(0, App.Text("Compressor.Processing")));

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
                    // スキャン後にファイルが削除されている場合はスキップする
                    foreach (var (fullPath, relativePath) in filesToCompress)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!relativePath.EndsWith('/') && !File.Exists(fullPath))
                        {
                            Logger.Log($"ファイルが見つかりません（スキップ）: {fullPath}");
                            continue;
                        }
                        writer.Add(fullPath, relativePath);
                    }

                    // 進捗スロットリング（UIスレッド負荷軽減用）
                    var throttler = new ProgressThrottler();

                    // 進捗報告オブジェクトを生成
                    using var reportProgress = new CancellableProgress<Report>(report =>
                    {
                        var percentage = (int)(report.GetRatio() * 100);
                        if (throttler.ShouldReport(percentage))
                            progress?.Report(new ProgressInfo(percentage, ""));
                    }, cancellationToken);

                    // ネイティブメソッドの呼び出し
                    writer.Save(outputPath, reportProgress);

                    // キャンセルされていたらここで一度だけスロー（コールバック内ではスローしない）
                    cancellationToken.ThrowIfCancellationRequested();

                    // Terminate で 100% を保証（Ice アプリケーションの実装パターンに準拠）
                    progress?.Report(new ProgressInfo(100, App.Text("Compressor.Processing")));

                    // 全てのオブジェクトの生存を、ネイティブ処理完了直後に明示的に保証する
                    // これにより、JIT最適化による早期解放（およびそれに伴うアクセス違反）を防ぐ
                    NativeInteropHelper.KeepAliveCallbacks(writer, reportProgress, progress);
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
    /// ソースパスからファイルリストを構築する（まとめ圧縮の前段階）。
    /// 戻り値は (fullPath, relativePath) のリスト。衝突解決はまだ行われない。
    /// </summary>
    public static async Task<List<(string fullPath, string relativePath)>> ScanSourceFiles(
        List<string> sourceList, HashSet<string> excludedPatternSet, CancellationToken cancellationToken = default,
        DirectoryStructureMode? dirModeOverride = null, bool? normalizeUnicodeOverride = null)
    {
        var filesToCompress = new List<(string fullPath, string relativePath)>();
        var dirMode = dirModeOverride ?? SettingsManager.Instance.Current.DirectoryStructureMode;
        var normalizeUnicode = normalizeUnicodeOverride ?? SettingsManager.Instance.NormalizeUnicodeFileNames;

        foreach (var sourcePath in sourceList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(sourcePath))
            {
                if (!ShouldExcludeFile(sourcePath, excludedPatternSet))
                {
                    filesToCompress.Add((sourcePath, NormalizeNfc(Path.GetFileName(sourcePath), normalizeUnicode)));
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

                    var relativePath = NormalizeNfc(
                        dirMode == DirectoryStructureMode.Flat
                            ? Path.GetFileName(file)
                            : Path.GetRelativePath(parentDir, file),
                        normalizeUnicode);
                    filesToCompress.Add((file, relativePath));

                    // このファイルの全祖先ディレクトリを記録。
                    // 既に登録済みの祖先に到達したら break（他のファイルで登録済みの場合は上位まで登録済み）。
                    // これにより O(N × D) から平均 O(N) に近づく。
                    var fileDir = Path.GetDirectoryName(file);
                    while (fileDir != null && fileDir.Length >= sourcePath.Length)
                    {
                        if (!directoriesWithFiles.Add(fileDir)) break;
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
                        var relativePath = NormalizeNfc(Path.GetRelativePath(parentDir, emptyDir), normalizeUnicode);
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

        // 同一 (fullPath, relativePath) の重複を最終的に排除する。
        // 呼び出し側が同じパスを複数回渡した場合（CLI で重複引数、複数選択時の
        // 偶発的重複など）や、sourceList に祖先と子孫が同時に含まれて走査で重複した
        // ケースに備える。DetectConflicts は「異なる fullPath が同じ relativePath に
        // 衝突するか」で判定するため、同一 fullPath 重複は衝突検出で素通しされてしまい、
        // このステップで除去しないと ArchiveWriter が同名エントリを重複追加してしまう。
        return DeduplicateByIdentity(filesToCompress);
    }

    /// <summary>
    /// (fullPath, relativePath) ペアで同一のエントリを除去する。順序は維持し、先勝ち。
    /// fullPath 部分は OS 依存（Windows=ignore case / Linux=case sensitive）、
    /// relativePath 部分も同じコンパラで判定する（Windows 上では `A/` と `a/` が同じ
    /// アーカイブエントリ扱いになる既存セマンティクスを維持）。
    /// </summary>
    /// <remarks>
    /// 旧実装は `"{fullPath}|{relativePath}"` の文字列結合をキーにしていたが、`|` は
    /// Linux/macOS では正当なファイル名文字になりうるため、例えば `("a","b|c")` と
    /// `("a|b","c")` がキー衝突してしまう。ValueTuple + <see cref="PathPairComparer"/>
    /// に置き換えて衝突を構造的に回避する。
    /// </remarks>
    private static string NormalizeNfc(string path, bool enabled)
    {
        if (!enabled || path.IsNormalized(System.Text.NormalizationForm.FormC))
            return path;
        return path.Normalize(System.Text.NormalizationForm.FormC);
    }

    private static List<(string fullPath, string relativePath)> DeduplicateByIdentity(
        List<(string fullPath, string relativePath)> files)
    {
        if (files.Count <= 1) return files;

        var seen = new HashSet<(string fullPath, string relativePath)>(DefaultPathPairComparer);
        var result = new List<(string fullPath, string relativePath)>(files.Count);
        var skipped = 0;
        foreach (var entry in files)
        {
            if (seen.Add(entry))
                result.Add(entry);
            else
                skipped++;
        }

        if (skipped > 0)
            Logger.Log($"同一 (fullPath, relativePath) の重複 {skipped} 件を除去しました");
        return result;
    }

    /// <summary>
    /// (fullPath, relativePath) ValueTuple に対して OS 依存のパス比較を適用する
    /// <see cref="IEqualityComparer{T}"/> 実装。
    /// </summary>
    private sealed class PathPairComparer(StringComparer comparer) : IEqualityComparer<(string fullPath, string relativePath)>
    {
        public bool Equals((string fullPath, string relativePath) x, (string fullPath, string relativePath) y) =>
            comparer.Equals(x.fullPath, y.fullPath) && comparer.Equals(x.relativePath, y.relativePath);

        public int GetHashCode((string fullPath, string relativePath) obj) =>
            HashCode.Combine(
                comparer.GetHashCode(obj.fullPath ?? string.Empty),
                comparer.GetHashCode(obj.relativePath ?? string.Empty));
    }

    /// <summary>
    /// ファイルリストから衝突グループを検出する。
    /// 衝突がない場合は空リストを返す。
    /// </summary>
    /// <remarks>
    /// FileInfo は同一 fullPath に対して 1 回だけ構築して使い回すことで、重複 stat syscall を回避する。
    /// </remarks>
    public static List<Models.FileConflictGroup> DetectConflicts(List<(string fullPath, string relativePath)> files)
    {
        // 同一 fullPath の FileInfo を 1 回だけ生成してキャッシュする（衝突グループ内の stat 多発を回避）。
        // キーは fullPath なので PathComparer を使って OS のファイルシステム semantics に合わせる。
        var fileInfoCache = new Dictionary<string, (long length, DateTime lastWrite)>(PathComparer);
        (long length, DateTime lastWrite) GetInfo(string fullPath)
        {
            if (fileInfoCache.TryGetValue(fullPath, out var cached)) return cached;
            var info = new FileInfo(fullPath);
            var value = info.Exists
                ? (info.Length, info.LastWriteTime)
                : (0L, DateTime.MinValue);
            fileInfoCache[fullPath] = value;
            return value;
        }

        // 衝突判定は「同じ relativePath に対して 2 つ以上の異なる fullPath が存在する」場合のみ。
        // 同一 fullPath が重複しているだけのケース（呼び出し側の不注意な重複入力等）は
        // 衝突扱いせず、代表 1 件だけ残すのが期待挙動。
        // ScanSourceFiles 側で fullPath は既に絶対パス化されているため、ここでの
        // Path.GetFullPath 再正規化は不要（大量ファイル時のオーバーヘッド回避）。
        // 相対パス（アーカイブ内パス）は ZIP/7z 仕様に合わせて OrdinalIgnoreCase、
        // fullPath（実 FS パス）は OS 依存の PathComparer を使うことで、case-sensitive FS
        // 上の `A.txt` と `a.txt` を正しく別ファイルとして扱う。
        var groups = files
            .GroupBy(f => f.relativePath, StringComparer.OrdinalIgnoreCase)
            .Where(g => g
                .Select(f => f.fullPath)
                .Distinct(PathComparer)
                .Skip(1)
                .Any())
            .Select(g => new Models.FileConflictGroup
            {
                ConflictingName = g.Key,
                Kind = Models.ConflictKind.CompressionList,
                Entries = g.Select(f =>
                {
                    var (len, lastWrite) = GetInfo(f.fullPath);
                    return new Models.FileConflictEntry(f.fullPath, f.relativePath, len, lastWrite);
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
        const int StackBufSize = 64;
        Span<Range> ranges = stackalloc Range[StackBufSize]; // パスセグメント数の上限（通常十分）
        var count = path.AsSpan().SplitAny(ranges, separators, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < count; i++)
        {
            if (excludedPatternSet.Contains(path[ranges[i]]))
            {
                return true;
            }
        }

        // バッファ上限到達時は後段セグメントを見落としている可能性があるので、
        // アロケーションを受け入れて全セグメントを再走査する（node_modules 等の
        // 中間ディレクトリ除外も確実に判定するため）。
        // 64 階層超えは WSL マウントや深い UNC のレアケースなので、この経路のコストは許容する。
        // セパレータ配列は ArchiveFormatConstants の共有 static フィールドを使い、
        // 呼出毎のアロケを避ける。
        if (count == StackBufSize)
        {
            var segments = path.Split(
                ArchiveFormatConstants.PathSeparators,
                StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                if (excludedPatternSet.Contains(segment))
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
        // 既知の複合拡張子パターン（ArchiveFormatConstants で一元管理）
        foreach (var compoundExt in ArchiveFormatConstants.CompoundTarExtensions)
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
    /// リネーム方式: 衝突するファイル名に連番サフィックスを付与して解決する。
    /// usedPaths の値を「次に試す連番」として使い、do-while で毎回先頭から探索する
    /// O(K²) の挙動を避けて O(K) で解決する。
    /// </summary>
    private static List<(string fullPath, string relativePath)> ResolveByRenaming(
        List<(string fullPath, string relativePath)> files)
    {
        var result = new List<(string fullPath, string relativePath)>(files.Count);
        // key = 元の relativePath、value = 次に試す連番（次回は value++ したものを使う）
        var nextSuffix = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // 既に使用済みの実パス集合（衝突回避用）
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (fullPath, relativePath) in files)
        {
            if (!usedPaths.Contains(relativePath))
            {
                // 初出: そのまま使用
                usedPaths.Add(relativePath);
                nextSuffix[relativePath] = 1;
                result.Add((fullPath, relativePath));
                continue;
            }

            // 衝突: 連番サフィックスを付与（foo_1.jpg, foo_2.jpg, ...）
            var dir = Path.GetDirectoryName(relativePath) ?? "";
            var nameOnly = Path.GetFileName(relativePath);
            var (name, ext) = SplitStemAndExtension(nameOnly);
            var count = nextSuffix.TryGetValue(relativePath, out var prev) ? prev : 1;
            string newPath;
            do
            {
                newPath = string.IsNullOrEmpty(dir)
                    ? $"{name}_{count}{ext}"
                    : Path.Combine(dir, $"{name}_{count}{ext}");
                count++;
            } while (usedPaths.Contains(newPath));

            nextSuffix[relativePath] = count; // 次回はここから開始
            usedPaths.Add(newPath);
            result.Add((fullPath, newPath));
            Logger.Log($"同名ファイルをリネーム: {relativePath} → {newPath}");
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
    /// ファイルを含まない空ディレクトリを再帰的に収集する。
    /// </summary>
    /// <remarks>
    /// <c>GetFilesRecursively</c> でファイル列挙時に記録した <paramref name="directoriesWithFiles"/>
    /// の補集合を返す。`Directory.EnumerateDirectories` による 2 回目の走査を避けるため、
    /// HashSet 差分で高速に算出する。
    /// </remarks>
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
            return [];
        }
        catch (IOException ex)
        {
            Logger.Log($"ファイル取得中にI/Oエラー: {directoryPath}, {ex.Message}");
            return [];
        }
    }

}
