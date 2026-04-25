using Avalonia.Controls;
using Avalonia.Threading;
using Cube.FileSystem.SevenZip;
using System.Security;
namespace Lhamiel.Util;

/// <summary>
/// アーカイブ展開機能
/// </summary>
public static class ArchiveExtractor
{
    /// <summary>
    /// 定数: サポートされている展開形式の一覧
    /// </summary>
    internal static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".tbz",
        ".lzma", ".tlz", ".xz", ".txz", ".rar", ".lzh", ".cab", ".arj", ".z", ".tz"
    };

    /// <summary>
    /// 無視するシステムディレクトリ名（展開時の構造解析・圧縮時の除外で共用）
    /// </summary>
    internal static readonly HashSet<string> IgnoredSystemDirectories = new(StringComparer.OrdinalIgnoreCase) { "__MACOSX" };

    /// <summary>
    /// 無視するシステムファイル名（展開時の構造解析・圧縮時の除外で共用）
    /// </summary>
    internal static readonly HashSet<string> IgnoredSystemFiles = new(StringComparer.OrdinalIgnoreCase) { "desktop.ini", "Thumbs.db", ".DS_Store" };

    /// <summary>展開フィルタ用の統合済みシステム名配列（static で1回だけ生成）</summary>
    private static readonly string[] FilterNames = [.. IgnoredSystemDirectories, .. IgnoredSystemFiles];

    /// <summary>
    /// 圧縮のみの拡張子（単体ではアーカイブでない）。.tar と組み合わせた複合拡張子のみ2段除去する。
    /// 定義は <see cref="ArchiveFormatConstants.CompressionOnlyExtensions"/> を参照する（DRY 統合）。
    /// </summary>
    private static HashSet<string> CompressionOnlyExtensions => ArchiveFormatConstants.CompressionOnlyExtensions;

    /// <summary>
    /// アーカイブファイル名から拡張子を除去したベース名を返す。
    /// 複合コンテナ（.tar.gz, .tar.xz 等）は両方除去し、それ以外は最外の拡張子のみ除去する。
    /// 例: "foo.tar.gz" → "foo", "project.zip" → "project", "foo.rar.zip" → "foo.rar"
    /// </summary>
    internal static string GetArchiveBaseName(string filePath)
    {
        var name = Path.GetFileName(filePath);
        var ext = Path.GetExtension(name);
        if (string.IsNullOrEmpty(ext) || !SupportedExtensions.Contains(ext)) return name;

        // 最外の拡張子を除去
        name = Path.GetFileNameWithoutExtension(name);

        // 圧縮のみの拡張子だった場合、内側が .tar なら追加除去（.tar.gz → foo）
        if (CompressionOnlyExtensions.Contains(ext))
        {
            var innerExt = Path.GetExtension(name);
            if (string.Equals(innerExt, ".tar", StringComparison.OrdinalIgnoreCase))
            {
                name = Path.GetFileNameWithoutExtension(name);
            }
        }

        return name;
    }

    /// <summary>
    /// 指定されたファイルがサポートされているアーカイブ形式かどうかを確認する
    /// </summary>
    /// <param name="filePath">確認するファイルのパス</param>
    /// <returns>サポートされている形式の場合はtrue、そうでなければfalse</returns>
    public static bool IsSupportedArchiveType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    /// <summary>
    /// 指定されたパス一覧がすべてサポート対象のアーカイブファイルかどうかを判定する。
    /// フォルダや非アーカイブファイルが1つでも含まれていればfalseを返す。
    /// </summary>
    public static bool AreAllSupportedArchives(IEnumerable<string> paths)
    {
        return paths.All(p => File.Exists(p) && IsSupportedArchiveType(p));
    }

    /// <summary>
    /// アーカイブファイルの展開先ディレクトリを取得する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="defaultOutputDir">デフォルトの出力ディレクトリ</param>
    /// <param name="outputToSameDirectory">同じディレクトリに出力するかどうか</param>
    /// <returns>展開先ディレクトリのパス（アーカイブ名フォルダを含む）</returns>
    public static string GetOutputDirectory(string archivePath, string defaultOutputDir, bool outputToSameDirectory = false)
    {
        var baseDir = GetBaseOutputDirectory(archivePath, defaultOutputDir, outputToSameDirectory);
        return Path.Combine(baseDir, GetArchiveBaseName(archivePath));
    }

    /// <summary>
    /// 基準となる出力ディレクトリを取得（アーカイブ名フォルダを含まない）
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="defaultOutputDir">デフォルトの出力ディレクトリ</param>
    /// <param name="outputToSameDirectory">同じディレクトリに出力するかどうか</param>
    /// <returns>基準となる出力ディレクトリのパス</returns>
    public static string GetBaseOutputDirectory(string archivePath, string defaultOutputDir, bool outputToSameDirectory = false)
    {
        var directory = Path.GetDirectoryName(archivePath) ?? "";
        var baseDirectory = outputToSameDirectory ? directory : defaultOutputDir;

        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            // outputToSameDirectory=true でもディレクトリ部が空なら defaultOutputDir にフォールバック
            baseDirectory = string.IsNullOrWhiteSpace(defaultOutputDir) ? directory : defaultOutputDir;
        }
        return baseDirectory;
    }

    /// <summary>
    /// アーカイブの先頭2階層の解析結果を保持するデータ構造。
    /// record にすることで <c>with</c> 式による部分コピーを可能にし、プロパティ追加時の
    /// コピー漏れバグ（各呼び出し側で全プロパティを列挙するパターン）を防ぐ。
    /// </summary>
    public record ArchiveStructureInfo
    {
        /// <summary>
        /// フォルダ作成をスキップすべきか。
        /// ルートフォルダがアーカイブ名と一致する場合、フォルダを作成すると二重ネストになるためスキップする。
        /// </summary>
        public bool ShouldSkipFolderCreation { get; init; }

        /// <summary>
        /// ルートレベルが単一アイテムの場合、その名前（上書き確認パス精密化用）
        /// </summary>
        public string? SingleRootItemName { get; init; }

        /// <summary>
        /// 展開時に使われた <c>CreateArchiveNameFolder</c> 設定のスナップショット値。
        /// 展開中にユーザーが設定を変更しても、完了後の「開くフォルダ」決定と矛盾しないよう
        /// <see cref="FolderOpener.OpenExtractionResult"/> にここの値を渡す。
        /// null の場合は現在の設定値が使われる（下位互換）。
        /// </summary>
        public bool? CapturedCreateArchiveNameFolder { get; init; }

        /// <summary>
        /// アーカイブ内の非圧縮サイズ合計（バイト）。<see cref="GetArchiveStructureInfo"/> が
        /// reader.Items を走査するついでに計算するため、別途 <c>DiskSpaceChecker.GetArchiveUncompressedSize</c>
        /// を呼ぶ必要はない。-1 の場合は未計算（取得失敗または旧経路）。
        /// </summary>
        public long TotalUncompressedSize { get; init; } = -1;
    }

    /// <summary>
    /// アーカイブの構造を一度の解析で取得する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <returns>解析結果を格納したArchiveStructureInfo</returns>
    public static ArchiveStructureInfo GetArchiveStructureInfo(string archivePath)
    {
        if (!File.Exists(archivePath))
        {
            return new ArchiveStructureInfo();
        }

        try
        {
            using var reader = new ArchiveReader(archivePath);
            var structure = ParseArchiveRootLevel(reader);

            var rootFolders = structure.RootFolders;
            var rootFiles = structure.RootFiles;

            var allRootItems = new HashSet<string>(rootFolders, StringComparer.OrdinalIgnoreCase);
            allRootItems.UnionWith(rootFiles);
            var hasSingleRootItem = allRootItems.Count == 1;
            var singleRootItemName = hasSingleRootItem ? allRootItems.FirstOrDefault() : null;

            var archiveName = GetArchiveBaseName(archivePath);

            // ルートフォルダのみ（ファイルなし）でアーカイブ名と一致 → フォルダ作成すると二重ネストになるのでスキップ
            var shouldSkipFolderCreation = rootFolders.Count == 1 && rootFiles.Count == 0 &&
                string.Equals(rootFolders.First(), archiveName, StringComparison.OrdinalIgnoreCase);

            if (shouldSkipFolderCreation)
                Logger.Log($"アーカイブ名と一致するルートフォルダを検出（フォルダ作成スキップ）: {rootFolders.First()}");

            return new ArchiveStructureInfo
            {
                ShouldSkipFolderCreation = shouldSkipFolderCreation,
                SingleRootItemName = singleRootItemName,
                TotalUncompressedSize = structure.TotalUncompressedSize
            };
        }
        catch (Exception ex)
        {
            Logger.Log($"アーカイブ構造解析エラー: {ex.Message}");
            return new ArchiveStructureInfo();
        }
    }

    /// <summary>
    /// アーカイブの先頭2階層の解析結果を保持する内部データ構造
    /// </summary>
    private class ArchiveStructure
    {
        /// <summary>
        /// プロパティ: ルートレベルのフォルダ名のセット
        /// </summary>
        public HashSet<string> RootFolders { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// プロパティ: ルートレベルのファイル名のセット
        /// </summary>
        public HashSet<string> RootFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 非圧縮サイズ合計（バイト）。reader.Items のループで同時集計する。
        /// </summary>
        public long TotalUncompressedSize { get; set; }
    }

    private const string TempDirPrefix = "Lhamiel_";

    /// <summary>
    /// 一時ディレクトリを作成する。suffixで用途を区別する。
    /// </summary>
    private static string CreateTempDirectory(string suffix, string? basePath = null)
    {
        var dir = Path.Combine(basePath ?? Path.GetTempPath(), $"{TempDirPrefix}{suffix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// ディレクトリの直下の全アイテム（サブディレクトリ・ファイル）を宛先に移動する
    /// </summary>
    private static void MoveDirectoryContents(string sourceDir, string destDir)
    {
        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
            Directory.Move(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        foreach (var file in Directory.EnumerateFiles(sourceDir))
            File.Move(file, Path.Combine(destDir, Path.GetFileName(file)));
    }

    /// <summary>
    /// パス内に無視対象のシステムディレクトリが含まれるかチェック（ゼロアロケーション）
    /// </summary>
    private static bool ContainsIgnoredDirectory(ReadOnlySpan<char> path)
    {
        while (path.Length > 0)
        {
            var sepIndex = path.IndexOfAny('/', '\\');
            var segment = sepIndex < 0 ? path : path[..sepIndex];
            if (segment.Length > 0)
            {
                foreach (var dir in IgnoredSystemDirectories)
                {
                    if (segment.Equals(dir.AsSpan(), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            path = sepIndex < 0 ? [] : path[(sepIndex + 1)..];
        }
        return false;
    }

    /// <summary>
    /// アーカイブのルートレベルのフォルダ・ファイルを解析する
    /// </summary>
    private static ArchiveStructure ParseArchiveRootLevel(ArchiveReader reader)
    {
        var structure = new ArchiveStructure();

        foreach (var item in reader.Items)
        {
            // 非圧縮サイズの集計はディレクトリエントリを除外して 1 度のループで完結させる。
            // (long) キャストで item.Length が int の場合のオーバーフローも防ぐ。
            if (!item.IsDirectory)
                structure.TotalUncompressedSize += (long)item.Length;

            var path = item.FullName.AsSpan();

            // 最初のセグメント（ルート名）を切り出す（配列アロケーション不要）
            var firstSep = path.IndexOfAny('/', '\\');
            var rootName = (firstSep < 0 ? path : path[..firstSep]).ToString();
            var hasSubPath = firstSep >= 0 && firstSep < path.Length - 1;

            if (rootName.Length == 0) continue;
            if (IgnoredSystemDirectories.Contains(rootName)) continue;

            // ファイル名（最後のセグメント）のシステムファイルチェック
            if (!item.IsDirectory)
            {
                var lastSep = path.LastIndexOfAny('/', '\\');
                var fileName = lastSep < 0 ? rootName : path[(lastSep + 1)..].ToString();
                if (IgnoredSystemFiles.Contains(fileName)) continue;
            }

            if (!hasSubPath && !item.IsDirectory)
                structure.RootFiles.Add(rootName);
            else
                structure.RootFolders.Add(rootName);
        }

        return structure;
    }

    /// <summary>
    /// パス境界チェックに使う OS 依存の文字列比較モード。
    /// Windows は case-insensitive（NTFS 既定）、Linux/macOS は case-sensitive。
    /// 全プラットフォームで <see cref="StringComparison.OrdinalIgnoreCase"/> を使うと
    /// case-sensitive FS で <c>../output/evil</c> 形の traversal を許してしまうため、
    /// 実ファイルシステムの挙動に合わせた比較を採用する。
    /// </summary>
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// パス境界チェック用に <paramref name="basePath"/> を絶対化 + 末尾セパレータ付与した形に正規化する。
    /// エントリ単位のループで繰り返し正規化するコストを避けるため、ループ外で 1 度だけ呼び出すのが想定用途。
    /// </summary>
    internal static string NormalizeBaseDirectory(string basePath) =>
        Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        + Path.DirectorySeparatorChar;

    /// <summary>
    /// アーカイブ内のエントリ名を展開先の相対パスとして安全に解決する（呼び出しごとに basePath を正規化する版）。
    /// 単発呼び出し用。ループ内からの呼び出しには <see cref="TryResolveSafeEntryPathFromNormalized"/> を使い、
    /// <see cref="NormalizeBaseDirectory"/> でループ外に正規化コストを追い出すこと。
    /// </summary>
    /// <param name="basePath">展開先のベースディレクトリ（未正規化可）</param>
    /// <param name="entryName">アーカイブ内のエントリ名（攻撃者制御）</param>
    /// <param name="safeFullPath">境界内に収まる結合済み絶対パス</param>
    /// <returns>境界内なら true、境界を超える場合は false</returns>
    internal static bool TryResolveSafeEntryPath(string basePath, string entryName, out string safeFullPath)
    {
        try
        {
            return TryResolveSafeEntryPathFromNormalized(NormalizeBaseDirectory(basePath), entryName, out safeFullPath);
        }
        catch
        {
            safeFullPath = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// <see cref="NormalizeBaseDirectory"/> で事前正規化したベースディレクトリを使って境界チェックを行う。
    /// アーカイブ内エントリをループ走査する場面では、1 回だけ正規化して本メソッドを繰り返し呼ぶことで
    /// <see cref="Path.GetFullPath(string)"/> の繰り返しコストを避けられる。
    /// </summary>
    /// <param name="normalizedBase">末尾セパレータ付きの絶対パス（<see cref="NormalizeBaseDirectory"/> の戻り値）</param>
    /// <param name="entryName">アーカイブ内のエントリ名（攻撃者制御）</param>
    /// <param name="safeFullPath">境界内に収まる結合済み絶対パス</param>
    internal static bool TryResolveSafeEntryPathFromNormalized(string normalizedBase, string entryName, out string safeFullPath)
    {
        safeFullPath = string.Empty;
        if (string.IsNullOrEmpty(entryName)) return false;

        // Zip Slip ガード: パス区切り・絶対パス・UNC・ドライブレターを拒否
        if (entryName.StartsWith('/') || entryName.StartsWith('\\')) return false;
        if (Path.IsPathRooted(entryName)) return false;

        // `/` と `\` の両方を Path.DirectorySeparatorChar に統一する。
        // Linux/macOS では `\` がファイル名として正当なため Path.GetFullPath が
        // 区切りと認識せず、アーカイブ内の `a\..\b` が traversal として解釈されない
        // リスクがある。両方を事前置換して Path.GetFullPath の境界判定に確実に乗せる。
        var normalized = entryName
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        try
        {
            // basePath が相対パスでも CWD に依存しないよう、Path.GetFullPath(path, basePath)
            // オーバーロード（.NET Core 2.1+）で事前正規化済みベースを使って結合する。
            var combined = Path.GetFullPath(normalized, normalizedBase);

            // 比較は OS 依存。case-sensitive FS で `../output/evil` 形のケース違い traversal が
            // 通らないよう、実際のファイルシステムの挙動に合わせる。
            if (!combined.StartsWith(normalizedBase, PathComparison) &&
                !string.Equals(combined, normalizedBase.TrimEnd(Path.DirectorySeparatorChar), PathComparison))
            {
                return false;
            }

            safeFullPath = combined;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// アーカイブ内のファイルと展開先の既存ファイルを突き合わせて衝突を検出する。
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="outputPath">展開先ディレクトリのパス</param>
    /// <returns>衝突するファイルの競合グループリスト。衝突がなければ空リスト</returns>
    public static List<Models.FileConflictGroup> DetectExtractionConflicts(string archivePath, string outputPath)
    {
        var conflicts = new List<Models.FileConflictGroup>();

        try
        {
            using var reader = new ArchiveReader(archivePath);
            // outputPath の絶対化はループ外で 1 回だけ行い、ループ内は正規化済み版を使う
            var normalizedOutputBase = NormalizeBaseDirectory(outputPath);

            foreach (var item in reader.Items)
            {
                if (item.IsDirectory) continue;

                var relativePath = item.FullName.Replace('\\', '/');

                // システムファイル・ディレクトリの除外
                var fileName = Path.GetFileName(relativePath);
                if (IgnoredSystemFiles.Contains(fileName)) continue;
                if (ContainsIgnoredDirectory(relativePath)) continue;

                // Zip Slip ガード: outputPath 境界外に出るエントリ（`..` / 絶対パス / UNC）はスキップ
                if (!TryResolveSafeEntryPathFromNormalized(normalizedOutputBase, relativePath, out var destFilePath))
                {
                    Logger.Log($"展開衝突検出で境界外パスを検出しスキップ: {relativePath}", LogLevel.Warning);
                    continue;
                }
                if (!File.Exists(destFilePath)) continue;

                // 衝突発見: 左=アーカイブ内ファイル（ソース）、右=既存ファイル（宛先）
                var destInfo = new FileInfo(destFilePath);
                var archiveEntry = new Models.FileConflictEntry(
                    archivePath, relativePath, item.Length, item.LastWriteTime);
                var existingEntry = new Models.FileConflictEntry(
                    destFilePath, relativePath, destInfo.Length, destInfo.LastWriteTime);

                conflicts.Add(new Models.FileConflictGroup
                {
                    ConflictingName = relativePath,
                    Entries = [archiveEntry, existingEntry]
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"展開衝突検出でエラー: {ex.Message}");
        }

        return conflicts;
    }

    /// <summary>
    /// 上書き確認ダイアログを表示すべきかどうかを判定する（親フォルダ直下展開時は実際に上書きされるパスのみで判定）
    /// </summary>
    /// <param name="outputPath">展開先ディレクトリのパス</param>
    /// <param name="overwriteCheckPaths">上書き確認を行う対象パス（nullの場合はoutputPathで判定。親フォルダ直下展開時は実際に上書きされるパスのみ渡す）</param>
    /// <returns>上書き対象が存在する場合true</returns>
    public static bool ShouldShowOverwriteDialog(string outputPath, IReadOnlyList<string>? overwriteCheckPaths)
    {
        return overwriteCheckPaths is { Count: > 0 }
            ? overwriteCheckPaths.Any(Path.Exists)
            : Path.Exists(outputPath);
    }

    /// <summary>
    /// アーカイブを展開する（非同期版）
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="outputPath">展開先ディレクトリのパス</param>
    /// <param name="progress">進捗コールバック</param>
    /// <param name="parentWindow">親ウィンドウ（上書き確認ダイアログ用）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <param name="overwriteCheckPaths">上書き確認を行う対象パス（nullの場合はoutputPathで判定）</param>
    /// <returns>展開処理の完了を表すTask</returns>
    public static async Task ExtractArchiveAsync(string archivePath, string outputPath, IProgress<ProgressInfo>? progress = null, Window? parentWindow = null, CancellationToken cancellationToken = default, IReadOnlyList<string>? overwriteCheckPaths = null, View.ProgressWindow? progressWindow = null, long precomputedUncompressedSize = -1)
    {
        Logger.Log($"ExtractArchiveAsync開始: archivePath={archivePath}, outputPath={outputPath}");
        cancellationToken.ThrowIfCancellationRequested();

        // 展開前のディスク容量チェック（アーカイブのメタデータ上の非圧縮サイズ）。
        // GetArchiveStructureInfo で既に reader.Items を 1 周しており、その時点で集計済みの
        // サイズを引き回すことで「同じアーカイブを 2 回開いて Items 列挙」を回避する（#10 軽量統合）。
        // 旧経路や呼び出し元が事前計算していない場合（precomputedUncompressedSize < 0）は
        // 従来通り DiskSpaceChecker 側で再計算する（後方互換）。
        var requiredSize = precomputedUncompressedSize >= 0
            ? precomputedUncompressedSize
            : DiskSpaceChecker.GetArchiveUncompressedSize(archivePath);
        if (requiredSize > 0)
        {
            var hasSpace = await DiskSpaceChecker.EnsureDiskSpaceAsync(
                outputPath, requiredSize, parentWindow, cancellationToken);
            if (!hasSpace)
                throw new OperationCanceledException(App.Text("Error.DiskSpaceCancelled"));
        }

        // 展開中のランタイム容量監視（Zip bomb 対策 / 悪意あるメタデータサイズへの保険）
        // operationCts と linkedCts で外側のキャンセルとも連携する。
        // requiredSize <= 0 の場合（メタデータが読めない / 空アーカイブ）は相対的な「必要量の
        // 10%」基準での判定が意味を持たないため 0 を渡し、DiskSpaceChecker 側で絶対閾値
        // （MinFreeSpaceThresholdBytes）のみの判定にフォールバックさせる。
        //
        // 戻り値の IDisposable は PeriodicCheckDisposable（DiskSpaceChecker 内部の型）で、
        // Dispose() 内で checkCts.Cancel() を呼ぶ実装になっているため、通常の
        // CancellationTokenSource のように「Dispose ではキャンセルされない」問題はない。
        // 内部の linkedCts は Task.Run の using スコープで別途破棄される。
        // using でスコープを抜けた時点で監視 Task.Run は確実に停止する。
        using var extractCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var periodicCheck = DiskSpaceChecker.StartPeriodicCheck(
            outputPath, requiredSize > 0 ? requiredSize : 0, parentWindow, extractCts);
        cancellationToken = extractCts.Token;

        // 展開先に既存ファイルがあるかチェック（一時展開方式の判定）
        var hasExistingFiles = ShouldShowOverwriteDialog(outputPath, overwriteCheckPaths);

        if (hasExistingFiles && parentWindow != null)
        {
            // 一時フォルダ方式: 一時展開 → 衝突検出 → ダイアログ → 移動
            await ExtractViaTempFolderAsync(archivePath, outputPath, progress, parentWindow, cancellationToken, progressWindow);
        }
        else
        {
            // 衝突なし: 直接展開
            await Task.Run(async () =>
            {
                var progressCallback = progress != null ? new Action<ProgressInfo>(p => progress.Report(p)) : null;
                try
                {
                    await ExtractArchive(archivePath, outputPath, progressCallback, parentWindow, false, cancellationToken, overwriteCheckPaths, null);
                }
                finally
                {
                    NativeInteropHelper.KeepAliveCallbacks(progressCallback, progress);
                }
            }, cancellationToken);
        }
    }

    /// <summary>
    /// 一時フォルダ方式で展開する。
    /// ①一時フォルダに全展開 → ②衝突検出 → ③ダイアログ表示 → ④選択結果に基づいて移動
    /// </summary>
    private static async Task ExtractViaTempFolderAsync(string archivePath, string outputPath, IProgress<ProgressInfo>? progress, Window parentWindow, CancellationToken cancellationToken, View.ProgressWindow? progressWindow)
    {
        // 一時フォルダを出力先ディレクトリ直下に作成（同一ドライブでFile.Moveが高速、かつ書き込み権限が確実）
        // outputPathがファイルの場合は親ディレクトリを使用
        var tempBaseDir = File.Exists(outputPath) ? (Path.GetDirectoryName(outputPath) ?? Path.GetTempPath()) : outputPath;
        var tempDir = CreateTempDirectory("Temp", tempBaseDir);
        Logger.Log($"一時フォルダ方式: tempDir={tempDir}");

        try
        {
            // ① 一時フォルダに展開（注意書き表示）
            progressWindow?.SetNotice(App.Text("Progress.ConflictNotice"));

            await Task.Run(async () =>
            {
                var progressCallback = progress != null ? new Action<ProgressInfo>(p => progress.Report(p)) : null;
                try
                {
                    await ExtractArchive(archivePath, tempDir, progressCallback, null, false, cancellationToken, null, null);
                }
                finally
                {
                    NativeInteropHelper.KeepAliveCallbacks(progressCallback, progress);
                }
            }, cancellationToken);

            progressWindow?.ClearNotice();
            cancellationToken.ThrowIfCancellationRequested();

            // ② 一時フォルダ vs 展開先を比較して衝突検出
            var conflicts = DetectFileSystemConflicts(tempDir, outputPath);
            HashSet<string>? skipArg = null;

            if (conflicts.Count > 0)
            {
                Logger.Log($"一時展開後のファイル衝突: {conflicts.Count}件");

                // ③ ダイアログ表示（両方実在するのでサムネイル完全対応）
                var (result, selectedFiles) = await View.FileConflictDialog.ShowFromBackgroundAsync(conflicts, parentWindow);
                if (result == Models.FileConflictResult.Cancel)
                {
                    Logger.Log("ユーザーが展開をキャンセル");
                    throw new OperationCanceledException(App.Text("Error.UserCancelledExtraction"));
                }

                // ④ 選択結果に基づいて移動
                // 左ペイン（一時フォルダ側=アーカイブから展開済み）が選択されたファイルのみ上書き移動する
                // fullPath が tempDir 内にあるものが左ペイン（アーカイブ側）の選択
                var archiveSideSelected = new HashSet<string>(
                    selectedFiles
                        .Where(f => f.fullPath.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase))
                        .Select(f => f.relativePath),
                    StringComparer.OrdinalIgnoreCase);

                // 衝突ファイルのうちアーカイブ側が選択されなかったもの = スキップ（既存を保持）
                var skipPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var conflict in conflicts)
                {
                    if (!archiveSideSelected.Contains(conflict.ConflictingName))
                        skipPaths.Add(conflict.ConflictingName);
                }

                if (skipPaths.Count > 0)
                    Logger.Log($"ユーザーが {skipPaths.Count} 件のファイルをスキップ指定");

                skipArg = skipPaths.Count > 0 ? skipPaths : null;
            }

            // ④ ファイル配置（衝突有無共通）
            progressWindow?.SetIndeterminate(App.Text("Progress.MovingFiles"));
            try
            {
                await MoveExtractedFilesAsync(tempDir, outputPath, skipArg, cancellationToken);
            }
            finally
            {
                progressWindow?.UpdateProgress(100);
            }
        }
        finally
        {
            // 一時フォルダを削除
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                    Logger.Log($"一時フォルダ削除完了: {tempDir}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"一時フォルダ削除失敗: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// ファイルシステム上の2つのディレクトリを比較して衝突を検出する。
    /// </summary>
    private static List<Models.FileConflictGroup> DetectFileSystemConflicts(string sourceDir, string destDir)
    {
        var conflicts = new List<Models.FileConflictGroup>();

        if (!Directory.Exists(sourceDir))
            return conflicts;

        // 宛先がファイルの場合（アーカイブ名フォルダと同名のファイルが存在）は
        // そのファイル自体を衝突として報告し、内部ファイルの個別チェックはスキップ
        if (File.Exists(destDir))
        {
            var fileInfo = new FileInfo(destDir);
            var destName = Path.GetFileName(destDir);
            conflicts.Add(new Models.FileConflictGroup
            {
                ConflictingName = destName,
                Entries =
                [
                    new Models.FileConflictEntry(sourceDir, destName, 0, Directory.GetLastWriteTime(sourceDir)),
                    new Models.FileConflictEntry(destDir, destName, fileInfo.Length, fileInfo.LastWriteTime)
                ]
            });
            return conflicts;
        }

        if (!Directory.Exists(destDir))
            return conflicts;

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, sourceFile).Replace('\\', '/');
            var destFile = Path.Combine(destDir, relativePath.Replace('/', Path.DirectorySeparatorChar));

            // ファイル同士の衝突、またはファイル↔ディレクトリのパス型衝突を検出。
            // File.Exists + Directory.Exists + FileInfo で 3〜4 回 stat が走るのを避けるため、
            // File.GetAttributes を 1 回だけ呼んで属性フラグで分岐する。
            FileAttributes destAttrs;
            try { destAttrs = File.GetAttributes(destFile); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            // システムファイル・ディレクトリをスキップ
            var fileName = Path.GetFileName(relativePath);
            if (IgnoredSystemFiles.Contains(fileName)) continue;
            if (ContainsIgnoredDirectory(relativePath)) continue;

            var sourceInfo = new FileInfo(sourceFile);

            // 宛先がファイルかディレクトリかを FileAttributes で判定
            long destSize;
            DateTime destLastWrite;
            if ((destAttrs & FileAttributes.Directory) != 0)
            {
                destSize = 0;
                // ディレクトリの最終更新日時は Directory.GetLastWriteTime を使うと FileInfo 経由より syscall が軽い
                destLastWrite = Directory.GetLastWriteTime(destFile);
            }
            else
            {
                var destInfo = new FileInfo(destFile);
                destSize = destInfo.Length;
                destLastWrite = destInfo.LastWriteTime;
            }

            // 左=アーカイブから展開されたファイル、右=既存ファイル/ディレクトリ
            var archiveEntry = new Models.FileConflictEntry(
                sourceFile, relativePath, sourceInfo.Length, sourceInfo.LastWriteTime);
            var existingEntry = new Models.FileConflictEntry(
                destFile, relativePath, destSize, destLastWrite);

            conflicts.Add(new Models.FileConflictGroup
            {
                ConflictingName = relativePath,
                Entries = [archiveEntry, existingEntry]
            });
        }

        return conflicts;
    }

    /// <summary>
    /// 一時フォルダから展開先にファイルを移動する。
    /// 同一ドライブならFile.Moveで瞬時、異なるドライブならコピー＋削除。
    /// </summary>
    private static Task MoveExtractedFilesAsync(string sourceDir, string destDir, HashSet<string>? skipPaths, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            // 宛先がファイルの場合（パス型衝突）
            if (File.Exists(destDir))
            {
                // ユーザーが既存ファイルの保持を選択した場合はスキップ（移動しない）
                var destFileName = Path.GetFileName(destDir);
                if (skipPaths != null && skipPaths.Contains(destFileName))
                {
                    Logger.Log($"宛先ファイルを保持（ユーザー選択）: {destDir}");
                    return;
                }

                File.Delete(destDir);
            }
            Directory.CreateDirectory(destDir);

            // 空ディレクトリも保持するため、先にディレクトリ構造を作成する。
            // （ファイル側ループ内の CreateDirectory は親ディレクトリにしか届かないため、
            //   子孫空ディレクトリを確実に保持するにはこの 1 回の走査が必要）
            foreach (var sourceSubDir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relDir = Path.GetRelativePath(sourceDir, sourceSubDir);
                var destSubDir = Path.Combine(destDir, relDir);
                if (!Directory.Exists(destSubDir))
                    Directory.CreateDirectory(destSubDir);
            }

            foreach (var sourceFile in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(sourceDir, sourceFile).Replace('\\', '/');

                // スキップ対象チェック
                if (skipPaths != null && skipPaths.Contains(relativePath))
                    continue;

                var destFile = Path.Combine(destDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                // 親ディレクトリは上のディレクトリ構造作成ループで既に作られているため
                // CreateDirectory の重複呼び出しは基本的に不要。ただし空ディレクトリ列挙で
                // 親が拾えないエッジケース（権限等）に備えて、未存在時のみ作成する。
                var destFileDir = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(destFileDir) && !Directory.Exists(destFileDir))
                    Directory.CreateDirectory(destFileDir);

                // パス型衝突: 宛先にディレクトリがあるがソースはファイルの場合は削除
                if (Directory.Exists(destFile))
                    Directory.Delete(destFile, recursive: true);

                // 上書き移動（ReadOnly属性がある場合は解除してから実行、失敗時はロールバック）
                FileAttributes originalAttrs = 0;
                var clearedReadOnly = false;
                if (File.Exists(destFile))
                {
                    originalAttrs = File.GetAttributes(destFile);
                    if ((originalAttrs & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(destFile, originalAttrs & ~FileAttributes.ReadOnly);
                        clearedReadOnly = true;
                    }
                }
                try
                {
                    File.Move(sourceFile, destFile, overwrite: true);
                }
                catch
                {
                    // 移動失敗時: ReadOnly属性を元に戻す
                    if (clearedReadOnly && File.Exists(destFile))
                        try { File.SetAttributes(destFile, originalAttrs); } catch { /* ベストエフォート */ }
                    throw;
                }
            }
        }, cancellationToken);
    }

    /// <summary>
    /// アーカイブを展開する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="outputPath">展開先ディレクトリのパス</param>
    /// <param name="progressCallback">進捗コールバック</param>
    /// <param name="parentWindow">親ウィンドウ（上書き確認ダイアログ用）</param>
    /// <param name="overwriteConfirmed">上書き確認が既に完了しているかどうか</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <param name="overwriteCheckPaths">上書き確認を行う対象パス（nullの場合はoutputPathで判定）</param>
    public static async Task ExtractArchive(string archivePath, string outputPath, Action<ProgressInfo>? progressCallback = null, Window? parentWindow = null, bool overwriteConfirmed = false, CancellationToken cancellationToken = default, IReadOnlyList<string>? overwriteCheckPaths = null, HashSet<string>? skipRelativePaths = null)
    {
        Logger.Log($"ExtractArchive開始: archivePath={archivePath}, outputPath={outputPath}, overwriteConfirmed={overwriteConfirmed}");

        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException(App.Text("Error.ArchiveNotFound", archivePath));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // ファイル単位の衝突検出（上位で未確認の場合）
        var outputOrOverwriteExists = ShouldShowOverwriteDialog(outputPath, overwriteCheckPaths);

        if (outputOrOverwriteExists)
        {
            if (!overwriteConfirmed)
            {
                var conflicts = DetectExtractionConflicts(archivePath, outputPath);
                if (conflicts.Count > 0)
                {
                    Logger.Log($"ExtractArchive内でファイル衝突を検出: {conflicts.Count}件");
                    var (result, _) = await View.FileConflictDialog.ShowFromBackgroundAsync(conflicts, parentWindow);
                    if (result == Models.FileConflictResult.Cancel)
                        throw new OperationCanceledException(App.Text("Error.UserCancelledExtraction"));
                }
            }

            // 保護されたディレクトリ（デスクトップ自体など）の場合は上書き確認（削除）をさせない
            // overwriteCheckPaths がある場合は実際に退避・削除される各パスをチェック、
            // ない場合は outputPath 自体が退避・削除対象となるのでそちらをチェック
            var pathsToProtect = overwriteCheckPaths is { Count: > 0 }
                ? (IEnumerable<string>)overwriteCheckPaths
                : [outputPath];
            foreach (var protectPath in pathsToProtect)
            {
                if (PathValidator.IsProtectedDirectory(protectPath))
                {
                    Logger.Log($"上書き不可: 保護されたディレクトリです: {protectPath}", LogLevel.Warning);
                    throw new InvalidOperationException($"'{protectPath}' はシステムによって保護されているため、上書き展開できません。別の場所を選択してください。");
                }
            }
        }

        var tempOutputPath = CreateTempDirectory("Extract");

        // tempOutputPath は %TEMP% 配下に作られるため、outputPath と別ドライブの場合に
        // TEMP 側の空き容量枯渇を外側の periodicCheck（outputPath 監視）では検出できない。
        // 両者が別ドライブのときのみ TEMP 側も並行監視する（同一ドライブなら冗長なので省略）。
        // requiredBytes は上位で確保済みなので 0 を渡し、絶対閾値のみで判定。
        using var innerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var tempDriveRoot = Path.GetPathRoot(tempOutputPath);
        var outputDriveRoot = Path.GetPathRoot(outputPath);
        IDisposable? tempPeriodicCheck = null;
        if (!string.IsNullOrEmpty(tempDriveRoot) &&
            !string.IsNullOrEmpty(outputDriveRoot) &&
            !string.Equals(tempDriveRoot, outputDriveRoot, StringComparison.OrdinalIgnoreCase))
        {
            tempPeriodicCheck = DiskSpaceChecker.StartPeriodicCheck(
                tempOutputPath, 0, parentWindow, innerCts);
        }
        cancellationToken = innerCts.Token;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extractOption = new ArchiveOption { Filter = Filter.From(FilterNames) };

            // パスワード保護アーカイブ対応:
            // 7z.dll は暗号化エントリに遭遇した時点で ICryptoGetTextPassword コールバックを呼ぶ。
            // AsyncPasswordQuery 経由で UI ダイアログに委譲する。
            // 誤入力時は 7z.dll が同じハンドラを再度呼ぶため、attemptCount で UI に再試行表示を出す。
            // ExtractArchive は既に Task.Run 配下で呼ばれている（ExtractArchiveAsync 参照）ので
            // AsyncPasswordQuery の「UI スレッドから呼ぶな」制約に抵触しない。
            //
            // キャンセル追跡の必要性:
            // ユーザーがダイアログで Cancel を押すと ShowFromBackgroundAsync は null を返す。
            // 空文字で返しても 7z.dll は WrongPassword とみなし EncryptionException を投げるため、
            // ここでユーザー意図によるキャンセルを記録し、Save() 後に OperationCanceledException に
            // 変換する（「パスワードが違います」という誤解を招く通知を回避）。
            var archiveName = Path.GetFileName(archivePath);
            var attemptCount = 0;
            // userCancelledPassword は PasswordDialog のコールバックスレッド（7z.dll 由来）から書き込まれ、
            // reader.Save() 例外ハンドラ側のスレッドから読み取られるため、
            // volatile 相当のメモリ可視性保証が必要。attemptCount と対称に Interlocked で扱う。
            //
            // CS1628 について（レビュー bot の誤検知対策コメント）:
            // この変数は直下のラムダ（passwordQuery）にキャプチャされるため、コンパイラによって
            // closure クラスのフィールドに hoist される。`Interlocked.Increment(ref ...)` /
            // `Volatile.Read(ref ...)` は closure フィールドへの ref を取るが、これは合法。
            // CS1628 は async メソッドの ref/out パラメータを await を跨いで使う場合の制限であり、
            // 「captured local への ref」とは別。Volatile.Read は同期 catch when フィルタ内で
            // 使われており、await を跨ぐ可能性もない。実ビルドも 0 errors / 0 warnings。
            var userCancelledPassword = 0;
            // 再試行上限: 悪意あるアーカイブや構造的に誤判定されるアーカイブでの無限ダイアログループを防ぐ
            const int MaxPasswordAttempts = 3;
            var passwordQuery = new AsyncPasswordQuery(async _ =>
            {
                var currentAttempt = System.Threading.Interlocked.Increment(ref attemptCount);
                var isRetry = currentAttempt > 1;

                // 上限を超えたら自動キャンセル扱い（null 返しと同じ経路）
                if (currentAttempt > MaxPasswordAttempts)
                {
                    Logger.Log($"パスワード入力上限（{MaxPasswordAttempts}回）を超えたため展開を中止します", LogLevel.Warning);
                    System.Threading.Interlocked.Exchange(ref userCancelledPassword, 1);
                    return string.Empty;
                }

                var pw = await View.PasswordDialog.ShowFromBackgroundAsync(archiveName, isRetry, parentWindow);
                if (pw is null)
                {
                    System.Threading.Interlocked.Exchange(ref userCancelledPassword, 1);
                    // 空文字を返すと AsyncPasswordQuery 側で Cancel=true にマップされる
                    return string.Empty;
                }
                return pw;
            }, cancellationToken);

            // ネイティブ側（7z.dll）との連携を確実に保護するため
            // using スコープ内で reader と progress を管理する
            using (var reader = new ArchiveReader(archivePath, passwordQuery, extractOption))
            {
                Logger.Log($"一時ディレクトリへの展開処理開始: {archivePath} -> {tempOutputPath}");

                // Zip Slip プリチェック: reader.Save() が全エントリを展開する前に、
                // アーカイブ内の全エントリ名が tempOutputPath 境界内に収まるかを検証する。
                // （DetectExtractionConflicts は衝突検出専用で、境界外エントリをスキップするだけなので
                //   ここで改めて全エントリを検証して安全性を担保する）
                // tempOutputPath の絶対化はループ外で 1 回だけ行う。
                var normalizedTempBase = NormalizeBaseDirectory(tempOutputPath);
                foreach (var item in reader.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entryName = item.FullName ?? string.Empty;
                    if (string.IsNullOrEmpty(entryName)) continue;

                    // Zip Slip ガードを最優先で実行。攻撃者が `__MACOSX/../../evil.txt` のような
                    // エントリ名を仕込んだ場合、`ContainsIgnoredDirectory` が先頭の `__MACOSX` を
                    // 検出して無視判定を下すと、本来境界外へ書き込まれるエントリの検証が
                    // スキップされてしまう。フィルタリングより前にセキュリティ境界を確定させる。
                    if (!TryResolveSafeEntryPathFromNormalized(normalizedTempBase, entryName, out _))
                    {
                        Logger.Log($"危険なエントリ名を検出しアーカイブ展開を中止: {entryName}", LogLevel.Warning);
                        throw new InvalidOperationException(
                            App.Text("Error.ZipSlipDetected", entryName));
                    }

                    // Zip Slip チェック通過後にライブラリ側フィルタ（__MACOSX / .DS_Store 等）と
                    // 歩調を合わせた無視判定。ここで continue しても上記セキュリティ境界は既に
                    // 通過済みなので、攻撃者が親ディレクトリ名を偽装してもバイパスにはならない。
                    var fileName = Path.GetFileName(entryName);
                    if (IgnoredSystemFiles.Contains(fileName)) continue;
                    if (ContainsIgnoredDirectory(entryName.Replace('\\', '/'))) continue;
                }

                // 進捗コールバックの有無に関わらず CancellableProgress<Report> を介して
                // reader.Save() に cancellationToken を接続する。旧実装は progressCallback が
                // null のとき `reader.Save(tempOutputPath)` を直接呼んでいたため、
                // DiskSpaceChecker の定期チェック等で extractCts.Cancel() が発火しても
                // ネイティブ展開処理がキャンセルを受け取らず走り続ける問題があった。
                var throttler = progressCallback != null ? new ProgressThrottler() : null;
                using var progress = new CancellableProgress<Report>(report =>
                {
                    if (progressCallback is null || throttler is null) return;
                    var percentage = (int)(report.GetRatio() * 100);
                    if (throttler.ShouldReport(percentage))
                        progressCallback(new ProgressInfo(percentage, ""));
                }, cancellationToken);

                try
                {
                    reader.Save(tempOutputPath, progress);
                }
                catch (EncryptionException) when (System.Threading.Volatile.Read(ref userCancelledPassword) == 1)
                {
                    // ユーザーがパスワードダイアログで Cancel を押した結果、または再試行上限超過で
                    // 自動キャンセルされた結果としての EncryptionException。
                    // 「パスワードが違います」ではなく通常のキャンセル扱いにする。
                    // Volatile.Read で別スレッドの書き込みを確実に可視化する。
                    Logger.Log("パスワード入力がキャンセルされたため展開を中止します");
                    throw new OperationCanceledException(App.Text("Error.UserCancelledExtraction"));
                }

                // キャンセルされていたらここで一度だけスロー（コールバック内ではスローしない）
                cancellationToken.ThrowIfCancellationRequested();

                // Terminate で 100% を保証（Ice アプリケーションの実装パターンに準拠）
                progressCallback?.Invoke(new ProgressInfo(100, ""));

                // ネイティブ側のコールバック完了を確実に保証
                NativeInteropHelper.KeepAliveCallbacks(progress, progressCallback);

                // reader自体の生存も保証
                NativeInteropHelper.KeepAliveCallbacks(reader);
            }

            // スキップ対象ファイルを一時ディレクトリから削除（移動前に除外）。
            // 攻撃者制御のエントリ名 `../foo` などで tempOutputPath 外のファイルを削除されないよう
            // TryResolveSafeEntryPath で境界内に収まっているかを必ず検証する。
            if (skipRelativePaths is { Count: > 0 })
            {
                Logger.Log($"スキップ対象の {skipRelativePaths.Count} ファイルを一時ディレクトリから除外");
                // ループ外で 1 回だけ正規化してから繰り返し境界チェックする
                var normalizedSkipBase = NormalizeBaseDirectory(tempOutputPath);
                foreach (var relativePath in skipRelativePaths)
                {
                    if (!TryResolveSafeEntryPathFromNormalized(normalizedSkipBase, relativePath, out var tempFilePath))
                    {
                        Logger.Log($"スキップ対象の境界外パスを拒否: {relativePath}", LogLevel.Warning);
                        continue;
                    }
                    if (File.Exists(tempFilePath))
                    {
                        try
                        {
                            File.Delete(tempFilePath);
                            Logger.Log($"スキップ: {relativePath}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"スキップファイルの削除に失敗: {relativePath}, {ex.Message}");
                        }
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 最終的な展開先への移動処理（原子性のため既存は削除せず退避し、移動成功後にバックアップを削除）
            Logger.Log($"一時ディレクトリから最終展開先へ移動します: {tempOutputPath} -> {outputPath}");
            var backupPaths = new List<string>();

            // 上書きが許可された（または確認済み）の場合は既存の対象を退避（削除せず移動で原子性を確保）
            try
            {
                if (overwriteCheckPaths is { Count: > 0 })
                {
                    // 親フォルダ直下展開時: 実際に上書きされるパスのみ退避（outputPathは退避しない）
                    foreach (var path in overwriteCheckPaths)
                    {
                        var moved = MoveExistingToBackup(path, backupPaths);
                        if (moved)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }
                }
                else
                {
                    // 複数ルート等でoutputPathを新規作成する場合: outputPathを退避してから作成
                    MoveExistingToBackup(outputPath, backupPaths);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Logger.Log($"既存対象の退避に失敗しました: {ex.Message}");
                throw new InvalidOperationException(App.Text("Error.PreparationFailed"), ex);
            }

            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            Logger.Log("一時ディレクトリの内容を最終展開先に移動します");

            try
            {
                MoveDirectoryContents(tempOutputPath, outputPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Logger.Log($"一時ディレクトリの内容移動に失敗しました: {ex.Message}");
                foreach (var backup in backupPaths)
                {
                    Logger.Log($"退避先（復元可能）: {backup}");
                }
                throw new InvalidOperationException(App.Text("Error.MoveFailed"), ex);
            }

            // 移動成功後のみバックアップを削除（原子性の完了）
            foreach (var backupPath in backupPaths)
            {
                try
                {
                    if (Directory.Exists(backupPath))
                    {
                        RemoveReadOnlyAttributes(backupPath);
                        Directory.Delete(backupPath, true);
                    }
                    else if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                {
                    Logger.Log($"バックアップの削除に失敗しました（手動削除可能）: {backupPath}, {ex.Message}", LogLevel.Warning);
                }
            }

            Logger.Log($"アーカイブ展開完了: {archivePath} -> {outputPath}");

        }
        catch (OperationCanceledException)
        {
            Logger.Log($"展開処理がキャンセルされました。一時ディレクトリを削除: {tempOutputPath}");

            try
            {
                if (Directory.Exists(tempOutputPath))
                {
                    RemoveReadOnlyAttributes(tempOutputPath);
                    Directory.Delete(tempOutputPath, true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Logger.Log($"キャンセル時の一時ディレクトリ削除に失敗しました: {tempOutputPath}, {ex.Message}", LogLevel.Warning);
            }
            throw;
        }
        catch (Exception ex)
        {
            // 一時ディレクトリのクリーンアップ
            try
            {
                if (Directory.Exists(tempOutputPath))
                {
                    RemoveReadOnlyAttributes(tempOutputPath);
                    Directory.Delete(tempOutputPath, true);
                }
            }
            catch (Exception cleanupEx)
            {
                Logger.Log($"エラー発生時の一時ディレクトリ削除に失敗しました: {tempOutputPath}, {cleanupEx.Message}", LogLevel.Warning);
            }

            var errorInfo = ArchiveErrorHandler.AnalyzeError(ex, archivePath, outputPath);
            Logger.Log($"アーカイブ展開でエラーが発生しました: {errorInfo.Message}");
            Logger.Log($"エラー詳細: {errorInfo.Details}");

            throw;
        }
        finally
        {
            // TEMP ドライブ監視を停止（開始していない場合は no-op）
            tempPeriodicCheck?.Dispose();
        }
    }


    /// <summary>
    /// 既存のファイルまたはディレクトリを退避用バックアップパスへ移動する（原子性のため削除せず移動）
    /// </summary>
    /// <param name="path">退避対象のパス（ファイルまたはディレクトリ）</param>
    /// <param name="backupPaths">退避先パスを追加するリスト</param>
    /// <returns>退避を行った場合はtrue、対象が存在しなかった場合はfalse</returns>
    private static bool MoveExistingToBackup(string path, List<string> backupPaths)
    {
        var isDirectory = Directory.Exists(path);
        if (!isDirectory && !File.Exists(path))
            return false;

        var backupPath = path + ".Lhamiel_backup_" + Guid.NewGuid().ToString("N");
        if (isDirectory)
        {
            RemoveReadOnlyAttributes(path);
            Directory.Move(path, backupPath);
        }
        else
        {
            File.Move(path, backupPath);
        }
        backupPaths.Add(backupPath);
        return true;
    }

    /// <summary>
    /// ファイルまたはディレクトリの読み取り専用属性を削除する
    /// </summary>
    /// <param name="path">対象のファイルまたはディレクトリパス</param>
    internal static void RemoveReadOnlyAttributes(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.Attributes.HasFlag(FileAttributes.ReadOnly))
                    fileInfo.Attributes &= ~FileAttributes.ReadOnly;
            }
            else if (Directory.Exists(path))
            {
                RemoveReadOnlyAttributesIterative(new DirectoryInfo(path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Logger.Log($"読み取り専用属性の削除処理でエラーが発生しました: {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// 指定されたディレクトリとその内容に対して、反復的に読み取り専用属性を解除します
    /// </summary>
    /// <param name="dirInfo">対象ディレクトリの DirectoryInfo インスタンス</param>
    private static void RemoveReadOnlyAttributesIterative(DirectoryInfo dirInfo)
    {
        if (!dirInfo.Exists) return;

        // スタックベースの反復処理（深い階層でのスタックオーバーフロー防止）
        var stack = new Stack<DirectoryInfo>();
        stack.Push(dirInfo);

        while (stack.Count > 0)
        {
            var currentDir = stack.Pop();

            try
            {
                if (currentDir.Attributes.HasFlag(FileAttributes.ReadOnly))
                    currentDir.Attributes &= ~FileAttributes.ReadOnly;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Logger.Log($"ディレクトリ属性変更失敗: {currentDir.FullName}: {ex.Message}");
            }

            try
            {
                foreach (var file in currentDir.GetFiles())
                {
                    try
                    {
                        if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
                            file.Attributes &= ~FileAttributes.ReadOnly;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                    {
                        Logger.Log($"ファイル属性変更エラー（無視）: {file.FullName}: {ex.Message}", LogLevel.Warning);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Logger.Log($"ディレクトリアクセスエラー（ファイル属性変更中）: {currentDir.FullName}: {ex.Message}", LogLevel.Warning);
            }

            try
            {
                foreach (var subDir in currentDir.GetDirectories())
                    stack.Push(subDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Logger.Log($"サブディレクトリアクセスエラー: {currentDir.FullName}: {ex.Message}", LogLevel.Warning);
            }
        }
    }
}
