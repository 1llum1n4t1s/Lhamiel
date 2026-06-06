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
        // 除外パターンは .lhaignore（gitignore 互換）から圧縮実行毎に読み直す。
        // RespectNestedGitignore=true なら各サブツリーの .gitignore も layered matcher として合成する。
        var lhaignoreLines = LhaignoreFile.ReadLines();
        var ignoreMatcher = GitignoreMatcher.Compile(lhaignoreLines);

        var outputCreated = false;
        // 空ディレクトリエントリ用の空マーカーディレクトリ（遅延作成）。後段 finally で必ず掃除する。
        string? emptyDirMarker = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 解決済みリストが渡された場合はそのまま使用、なければスキャン
            var filesToCompress = resolvedFiles ?? await ScanSourceFiles(
                sourceList,
                ignoreMatcher,
                cancellationToken,
                settings.DirectoryStructureMode,
                settings.NormalizeUnicodeFileNames,
                settings.IncludeHiddenAndSystemEntries,
                respectNestedGitignore: settings.RespectNestedGitignore,
                globalIgnoreLines: lhaignoreLines);

            Logger.Log($"圧縮対象のファイル総数: {filesToCompress.Count}個");

            // 圧縮処理を開始（ロック中ファイルはライブラリ側で自動的に一時コピーされる）
            progress?.Report(new ProgressInfo(0, App.Text("Compressor.Processing")));

            // 圧縮を実行（IProgress<Report>で詳細な進捗を取得）
            Logger.Log("圧縮処理を開始します");

            try
            {
                // ネイティブ 7z.dll 直列化ゲート: ライブラリ (1llum1n4t1s.Sevenzip) の共有シングルトン
                // SevenZipLibrary は ArchiveWriter の並行動作をサポートしないため、writer の
                // 生成 → Add → Save → Dispose 全体を 1 スロットに直列化する（バッチ圧縮の
                // IoBoundParallelism 並列実行時もネイティブ接触が重ならないよう保証）。
                using var nativeGate = await NativeArchiveGate.EnterAsync(cancellationToken);

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
                        if (relativePath.EndsWith('/'))
                        {
                            // 空ディレクトリエントリ: 実ディレクトリを writer.Add(realDir, "rel/") で渡すと
                            // ライブラリの AddRecursive が realDir を再走査し、スキャンで除外したはず
                            // （隠し/システム属性・.lhaignore 該当）のファイルを復活させてしまう
                            // （中身ゼロ判定のディレクトリでも実体には除外ファイルが残るため）。
                            // 空のマーカーディレクトリを src に渡すと再走査しても中身が無いので、
                            // 意図したディレクトリエントリだけがアーカイブに追加される。
                            emptyDirMarker ??= CreateEmptyDirectoryMarker();
                            writer.Add(emptyDirMarker, relativePath);
                            continue;
                        }
                        if (!File.Exists(fullPath))
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

                    // outputCreated を writer.Save の直前にセットする (Codex P2 指摘対応)。
                    // writer.Save が outputPath を実際に開く / 上書きを開始するのはここから。
                    // Save 前 (CreateArchiveWriter / writer.Add 等) で例外が出た場合、outputPath
                    // はまだ書かれていないため、既存アーカイブを上書き対象として指定していた
                    // ケースで誤ってユーザーの有効ファイルを削除しないようにする。
                    outputCreated = true;

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
        catch (OperationCanceledException oce) when (!cancellationToken.IsCancellationRequested && oce.InnerException is not null)
        {
            // ユーザー主導でないキャンセル。ライブラリ (1llum1n4t1s.Sevenzip 1.0.73) には
            // I/O 失敗 (ディスク満杯・デバイス切断等) を OperationCanceledException で包んで返す
            // 経路があり、これをキャンセル扱いすると本当の失敗が握り潰される。内側の実例外を
            // 昇格させてエラーとして処理する（修正済みライブラリでは SevenZipException で返るため
            // この経路には入らない）。
            Logger.Log($"非キャンセル要因の中断を検出、内部例外へ昇格: {oce.InnerException.Message}");
            TryDeletePartialOutput(outputPath, outputCreated, "圧縮エラー");
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(oce.InnerException);
        }
        catch (OperationCanceledException)
        {
            // キャンセル時: 書きかけの出力ファイルを掃除する。
            TryDeletePartialOutput(outputPath, outputCreated, "キャンセル");
            throw;
        }
        catch (Exception ex)
        {
            // 異常終了時 (SevenZipException, IOException, USB 切断による ERROR_DEV_NOT_EXIST など):
            // writer.Save の途中で例外が出た場合、書きかけの 7z/zip ファイルが Next Header (中央
            // ディレクトリ) 欠損の破損状態で残る。これを掃除してユーザーに「壊れた成果物」を
            // 渡さないようにする。削除自体が device error で失敗することもあるが best-effort で良い。
            Logger.Log($"圧縮でエラーが発生しました: {ex.Message}");
            TryDeletePartialOutput(outputPath, outputCreated, "圧縮エラー");
            throw;
        }
        finally
        {
            // 空ディレクトリマーカーを掃除する（best-effort）。成功・失敗・キャンセルいずれでも実行。
            if (emptyDirMarker is not null)
                FileOperations.CleanupTemporaryPath(emptyDirMarker, m => Logger.Log(m, LogLevel.Warning));
        }
    }

    /// <summary>
    /// 空ディレクトリエントリ追加用の「空のマーカーディレクトリ」を %TEMP% に作成して返す。
    /// 中身が常に空であることが重要（<see cref="ArchiveWriter.Add(string, string)"/> に渡すと
    /// ライブラリが src を再走査するため、実ディレクトリではなく空マーカーを使うことで
    /// 除外済みファイルの混入を防ぐ）。
    /// </summary>
    private static string CreateEmptyDirectoryMarker()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"Lhamiel_emptydir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 圧縮中断時の書きかけ出力ファイルを掃除する best-effort ヘルパ。
    /// <paramref name="outputCreated"/> が false（writer.Save まで到達していない）のときは何もしない。
    /// File.Delete 自身が失敗（device not exist 等）した場合は警告ログだけ残して呑む。
    /// </summary>
    private static void TryDeletePartialOutput(string outputPath, bool outputCreated, string reason)
    {
        if (!outputCreated || !File.Exists(outputPath))
            return;
        try
        {
            File.Delete(outputPath);
            Logger.Log($"{reason}時の部分ファイルを削除しました: {outputPath}");
        }
        catch (Exception ex)
        {
            Logger.Log($"{reason}時の部分ファイル削除に失敗しました: {outputPath}, {ex.Message}", LogLevel.Warning);
        }
    }

    /// <summary>
    /// ソースパスからファイルリストを構築する（まとめ圧縮の前段階）。
    /// 戻り値は (fullPath, relativePath) のリスト。衝突解決はまだ行われない。
    /// </summary>
    public static async Task<List<(string fullPath, string relativePath)>> ScanSourceFiles(
        List<string> sourceList, GitignoreMatcher matcher, CancellationToken cancellationToken = default,
        DirectoryStructureMode? dirModeOverride = null, bool? normalizeUnicodeOverride = null,
        bool? includeHiddenAndSystemEntriesOverride = null,
        bool respectNestedGitignore = false,
        IReadOnlyList<string>? globalIgnoreLines = null)
    {
        var filesToCompress = new List<(string fullPath, string relativePath)>();
        var dirMode = dirModeOverride ?? SettingsManager.Instance.Current.DirectoryStructureMode;
        var normalizeUnicode = normalizeUnicodeOverride ?? SettingsManager.Instance.NormalizeUnicodeFileNames;
        var includeHiddenAndSystemEntries = includeHiddenAndSystemEntriesOverride
            ?? SettingsManager.Instance.Current.IncludeHiddenAndSystemEntries;

        foreach (var sourcePath in sourceList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(sourcePath))
            {
                // 単一ファイル: ファイル名のみで判定（ソースルートが無いため）
                if (!ShouldExcludeFile(sourcePath, matcher, rootDir: null, isDirectory: false))
                {
                    filesToCompress.Add((sourcePath, NormalizeNfc(Path.GetFileName(sourcePath), normalizeUnicode)));
                }
            }
            else if (Directory.Exists(sourcePath))
            {
                Logger.Log($"ディレクトリをスキャン中: {sourcePath}");

                // 圧縮対象ディレクトリ内に .gitignore があれば layered matcher を構築する（その source 限定）。
                // .lhaignore (= matcher 引数) で枝刈りしながら探索するので、node_modules/ 内の .gitignore は読まない。
                // ⚠️ Codex P2 指摘対応 (#3305241279): BuildLayeredMatcherForSource は matcher (= fallbackMatcher) を
                // 必ず base layer として保持するので、globalIgnoreLines が null でも .lhaignore ルールは保証される。
                var effectiveMatcher = respectNestedGitignore
                    ? BuildLayeredMatcherForSource(
                        sourcePath,
                        globalIgnoreLines,
                        matcher,
                        includeHiddenAndSystemEntries)
                    : matcher;

                var files = GetFilesRecursively(sourcePath, effectiveMatcher, includeHiddenAndSystemEntries);
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
                    var emptyDirs = CollectEmptyDirectories(sourcePath, effectiveMatcher, directoriesWithFiles, includeHiddenAndSystemEntries);
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
    /// 圧縮対象ディレクトリ <paramref name="sourceDir"/> 配下から <c>.gitignore</c> を発見し、
    /// 各 <c>.gitignore</c> をその親ディレクトリ相対の layer として合成した <see cref="GitignoreMatcher"/> を返す。
    /// 既に <c>.lhaignore</c> で除外されるディレクトリ（例: <c>node_modules/</c>）配下の <c>.gitignore</c> は読み込まない。
    /// <para>
    /// Codex P2 指摘対応: <paramref name="globalIgnoreLines"/> が <c>null</c> の場合でも
    /// <paramref name="fallbackMatcher"/> のルールを必ず保持する。旧実装は <c>globalIgnoreLines ?? []</c> で
    /// フォールバックしていたが、それだと呼び出し元が既に <c>.lhaignore</c> から compile 済みの matcher を
    /// 持っている場合（生 lines を保持していないケース）に <c>fallbackMatcher</c> のルールが silent ドロップされていた。
    /// </para>
    /// </summary>
    internal static GitignoreMatcher BuildLayeredMatcherForSource(
        string sourceDir,
        IReadOnlyList<string>? globalIgnoreLines,
        GitignoreMatcher fallbackMatcher,
        bool includeHiddenAndSystemEntries)
    {
        ArgumentNullException.ThrowIfNull(fallbackMatcher);

        // ベース matcher は呼び出し元から渡された fallbackMatcher (= 既にコンパイル済みの .lhaignore matcher)。
        // globalIgnoreLines が非 null なら、それを source root スコープの追加 layer として最初に重ねる。
        // 通常パスでは fallbackMatcher 自体が .lhaignore からビルドされているため、ルール重複が発生する場合
        // もあるが、gitignore セマンティクスでは同一ルールの重複評価は最終 excluded 値に影響しない（後勝ちで同じ結果）。
        var additionalLayers = new List<(string baseRelativePath, IEnumerable<string> lines)>();
        if (globalIgnoreLines is { Count: > 0 })
        {
            additionalLayers.Add((string.Empty, globalIgnoreLines));
        }

        // 枝刈り用 prune matcher は fallback + global lines + root .gitignore (もしあれば) を合成する。
        // DiscoverGitignoreFiles が yield する layer は呼び出し時点でこの prune matcher で枝刈り済み。
        foreach (var (relativeDir, lines) in DiscoverGitignoreFiles(
                     sourceDir, fallbackMatcher, globalIgnoreLines, includeHiddenAndSystemEntries))
        {
            additionalLayers.Add((relativeDir, lines));
        }

        return additionalLayers.Count == 0
            ? fallbackMatcher
            : GitignoreMatcher.CompileLayered(fallbackMatcher, additionalLayers);
    }

    private static IEnumerable<(string relativeDir, string[] lines)> DiscoverGitignoreFiles(
        string sourceDir,
        GitignoreMatcher fallbackMatcher,
        IReadOnlyList<string>? globalIgnoreLines,
        bool includeHiddenAndSystemEntries)
    {
        // source root 自身の .gitignore（あれば）を先に読む。
        // ⚠️ ここで読んだ root の .gitignore は、その後のサブディレクトリ走査 (= さらなる nested
        // .gitignore を探す再帰探索) でも枝刈りに使う。これをしないと、root .gitignore で除外される
        // 大規模サブツリー (vendor/, build/, node_modules/.pnpm/ 等) も毎回完全に走査されて
        // O(tree size) の無駄なスキャンコストが発生する。RTK レビュー Codex P2 指摘対応。
        string[]? rootLines = null;
        var rootGitignore = Path.Combine(sourceDir, ".gitignore");
        if (File.Exists(rootGitignore))
        {
            rootLines = TryReadGitignoreLines(rootGitignore);
            if (rootLines is not null)
                yield return (string.Empty, rootLines);
        }

        // 枝刈り matcher = fallbackMatcher + global lines + root .gitignore の 3 段合流。
        // fallbackMatcher のルールを必ず保持することで、呼び出し元から渡された .lhaignore ルールが
        // silent ドロップされる経路を排除する (Codex P2 指摘 #3305241279)。
        var pruneAdditional = new List<(string baseRelativePath, IEnumerable<string> lines)>();
        if (globalIgnoreLines is { Count: > 0 })
            pruneAdditional.Add((string.Empty, globalIgnoreLines));
        if (rootLines is { Length: > 0 })
            pruneAdditional.Add((string.Empty, rootLines));

        var pruneMatcher = pruneAdditional.Count == 0
            ? fallbackMatcher
            : GitignoreMatcher.CompileLayered(fallbackMatcher, pruneAdditional);

        // ディレクトリツリーを併合 matcher で枝刈りしながら走査
        foreach (var dir in EnumerateDirectoriesWithPruning(sourceDir, pruneMatcher, includeHiddenAndSystemEntries))
        {
            var giPath = Path.Combine(dir, ".gitignore");
            if (!File.Exists(giPath))
                continue;
            var lines = TryReadGitignoreLines(giPath);
            if (lines is null)
                continue;
            var rel = Path.GetRelativePath(sourceDir, dir);
            yield return (rel, lines);
        }
    }

    private static string[]? TryReadGitignoreLines(string path)
    {
        try
        {
            return File.ReadAllLines(path);
        }
        catch (Exception ex)
        {
            Logger.Log($".gitignore の読み込みに失敗しました: {path}, {ex.Message}", LogLevel.Warning);
            return null;
        }
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
        // スレッド数を [1, 論理プロセッサ数] に丸める。
        // ・maxThreads が未指定/0以下のときは論理プロセッサ数を既定とする。
        // ・将来 maxThreads を設定値から渡す場合でも、コア数を超える oversubscribe や
        //   0/負値（ライブラリ側 CompressionOption.Validate が ArgumentOutOfRangeException を
        //   投げる）が 7z.dll に届かないようにする。
        // ・7z (LZMA2) の実効並列度とメモリ使用量はメソッド毎に 7z.dll 側で内部制限されるため、
        //   ここでは論理コア数を上限とするに留め、実機での圧縮性能は据え置く。
        var threadCount = Math.Clamp(
            maxThreads > 0 ? maxThreads : Environment.ProcessorCount,
            1,
            Environment.ProcessorCount);

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
            // ネイティブ 7z.dll 直列化ゲート（reader より外側で取得して生成→使用→Dispose を覆う）
            using var nativeGate = NativeArchiveGate.Enter();
            using var reader = new ArchiveReader(archivePath);

            // アーカイブ内のディレクトリエントリは "folder/" のように末尾区切り文字を伴うことが
            // あるため、比較前に両者の末尾 '/' '\' を除去して突き合わせる（末尾区切りの有無だけで
            // 同名フォルダ判定が外れるのを防ぐ）。
            var normalizedTarget = folderName.TrimEnd('/', '\\');

            // Items プロパティを使用してアーカイブ内容をチェック
            return reader.Items.Any(item =>
                item.IsDirectory &&
                string.Equals(item.FullName.TrimEnd('/', '\\'), normalizedTarget, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Logger.Log($"フォルダ構造のチェックに失敗しました: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 指定されたパスが除外対象か判定する。<paramref name="rootDir"/> が null の場合は単一ファイルモードで
    /// パス全体を <see cref="GitignoreMatcher"/> に照合する（ファイル名だけでなく、親ディレクトリの
    /// セグメントも directoryOnly ルールで判定される）。<paramref name="rootDir"/> が指定されたときは
    /// <see cref="Path.GetRelativePath(string, string)"/> で相対パス化して照合する。
    /// </summary>
    /// <param name="path">チェックするパス（絶対 or 相対）</param>
    /// <param name="matcher"><see cref="GitignoreMatcher"/>（.lhaignore および各 .gitignore から構築済）</param>
    /// <param name="rootDir">ソースルート（指定時は path をルート相対化）。<c>null</c> で単一ファイルモード</param>
    /// <param name="isDirectory">対象がディレクトリの場合は <c>true</c></param>
    /// <param name="traversalMode">
    /// DFS 枝刈りと併用する走査モード。<c>true</c> のとき各エントリを「自分自身のレベル」だけで照合し、
    /// 除外の推移性は DFS 側の枝刈りに委ねる（中間ディレクトリの否定再包含を git 同等に扱うため）。
    /// 単発ファイル判定など DFS を伴わない呼び出しは <c>false</c>（既定）。
    /// </param>
    /// <returns>除外すべき場合は <c>true</c></returns>
    internal static bool ShouldExcludeFile(string path, GitignoreMatcher matcher, string? rootDir = null, bool isDirectory = false, bool traversalMode = false)
    {
        if (!matcher.HasRules)
            return false;

        string relative;
        bool singleFileMode;
        if (rootDir is null)
        {
            // 単一ファイル: パス全体を / 区切りに正規化して渡す。
            // ファイル名だけでは "node_modules/a.js" のようなパスで `node_modules/` ルールが効かないため、
            // 親セグメントも照合できるよう IsExcluded 側でディレクトリ限定ルールが親ディレクトリ部にも適用される。
            // ルート相対のアンカードパターン（"/build" 等）は singleFileMode=true でスキップする。
            relative = GitignoreMatcher.NormalizePath(path).TrimStart('/');
            if (string.IsNullOrEmpty(relative))
                return false;
            singleFileMode = true;
        }
        else
        {
            relative = Path.GetRelativePath(rootDir, path);
            if (relative == "." || string.IsNullOrEmpty(relative))
                return false;
            singleFileMode = false;
        }

        return matcher.IsExcluded(GitignoreMatcher.NormalizePath(relative), isDirectory, singleFileMode, traversalMode);
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
    private static List<string> CollectEmptyDirectories(
        string rootDir,
        GitignoreMatcher matcher,
        HashSet<string> directoriesWithFiles,
        bool includeHiddenAndSystemEntries)
    {
        var emptyDirs = new List<string>();
        try
        {
            // 枝刈り対応の DFS で全ディレクトリを列挙する。matcher で除外されたディレクトリは
            // 自身も配下も対象外。
            var dirs = EnumerateDirectoriesWithPruning(rootDir, matcher, includeHiddenAndSystemEntries);
            foreach (var dir in dirs)
            {
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

    private static IEnumerable<string> EnumerateDirectoriesWithPruning(string root, GitignoreMatcher matcher, bool includeHiddenAndSystemEntries)
    {
        var enumOpts = CreateNonRecursiveEnumerationOptions(includeHiddenAndSystemEntries);

        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            // Directory.EnumerateDirectories は遅延評価で UnauthorizedAccessException / IOException は
            // foreach 中に発生する。yield return より前に ToArray() で確定させて、列挙中の例外もここで
            // catch できるようにする。
            string[] dirs;
            try
            {
                dirs = Directory.EnumerateDirectories(current, "*", enumOpts).ToArray();
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }

            foreach (var dir in dirs)
            {
                if (ShouldExcludeFile(dir, matcher, root, isDirectory: true, traversalMode: true))
                    continue;
                yield return dir;
                stack.Push(dir);
            }
        }
    }

    /// <summary>
    /// ディレクトリ内のファイルを再帰的に取得する（除外フィルタ適用）
    /// </summary>
    /// <param name="directoryPath">ディレクトリパス</param>
    /// <param name="excludedPatternSet">除外パターンの HashSet</param>
    /// <returns>ファイルパスのリスト</returns>
    private static IEnumerable<string> GetFilesRecursively(string directoryPath, GitignoreMatcher matcher, bool includeHiddenAndSystemEntries)
    {
        // ユーザーが圧縮対象として明示的に指定したソースルートそのものは除外しない。
        // gitignore のセマンティクスでは ignore ルールは「子エントリ」に適用されるべきで、
        // anchored パターン (例: "/build") は親基準でルート直下にマッチするものであって、
        // ソースルート自身を意味しない。basename だけで判定すると "build" という名前の
        // フォルダを圧縮しようとしただけで空アーカイブになる回帰が起きるので、ルート
        // 自身の除外判定は行わない（配下のサブディレクトリ・ファイルは
        // EnumerateFilesWithPruning 内で root 相対パスにより正しく枝刈りされる）。
        return EnumerateFilesWithPruning(directoryPath, matcher, includeHiddenAndSystemEntries);
    }

    private static IEnumerable<string> EnumerateFilesWithPruning(string root, GitignoreMatcher matcher, bool includeHiddenAndSystemEntries)
    {
        var enumOpts = CreateNonRecursiveEnumerationOptions(includeHiddenAndSystemEntries);

        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            // Directory.Enumerate* は遅延評価で、UnauthorizedAccessException / IOException は
            // foreach 中に発生する。yield return より前に ToArray() で確定させて、列挙中の例外も
            // ここで catch できるようにする。
            string[] files;
            string[] dirs;
            try
            {
                files = Directory.EnumerateFiles(current, "*", enumOpts).ToArray();
                dirs = Directory.EnumerateDirectories(current, "*", enumOpts).ToArray();
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.Log($"アクセス権限がありません: {current}, {ex.Message}");
                continue;
            }
            catch (IOException ex)
            {
                Logger.Log($"ファイル取得中にI/Oエラー: {current}, {ex.Message}");
                continue;
            }

            foreach (var file in files)
            {
                if (!ShouldExcludeFile(file, matcher, root, isDirectory: false, traversalMode: true))
                    yield return file;
            }

            foreach (var dir in dirs)
            {
                if (!ShouldExcludeFile(dir, matcher, root, isDirectory: true, traversalMode: true))
                    stack.Push(dir);
            }
        }
    }

    private static EnumerationOptions CreateNonRecursiveEnumerationOptions(bool includeHiddenAndSystemEntries) => new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = includeHiddenAndSystemEntries ? 0 : FileAttributes.Hidden | FileAttributes.System,
    };

    

}
