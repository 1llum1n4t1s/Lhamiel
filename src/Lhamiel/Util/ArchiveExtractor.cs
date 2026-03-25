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
        var fileName = Path.GetFileNameWithoutExtension(archivePath);
        return Path.Combine(baseDir, fileName);
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
            baseDirectory = directory;
        }
        return baseDirectory;
    }

    /// <summary>
    /// アーカイブの先頭2階層の解析結果を保持するデータ構造
    /// </summary>
    public class ArchiveStructureInfo
    {
        /// <summary>
        /// プロパティ: 二重フォルダ構造が検出された場合の内側のフォルダ名
        /// </summary>
        public string? DuplicateFolderName { get; init; }

        /// <summary>
        /// プロパティ: ルートレベルに単一のアイテムのみが存在するかどうか
        /// </summary>
        public bool HasSingleRootItem { get; init; }

        /// <summary>
        /// プロパティ: ルートレベルが単一アイテムの場合、その名前
        /// </summary>
        public string? SingleRootItemName { get; init; }
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
            return new ArchiveStructureInfo { HasSingleRootItem = false };
        }

        try
        {
            using var reader = new ArchiveReader(archivePath);
            var structure = ParseArchiveFirstTwoLevels(reader);

            var rootFolders = structure.RootFolders;
            var rootFiles = structure.RootFiles;

            var allRootItems = new HashSet<string>(rootFolders, StringComparer.OrdinalIgnoreCase);
            allRootItems.UnionWith(rootFiles);
            var rootItemsCount = allRootItems.Count;
            var hasSingleRootItem = rootItemsCount == 1;
            var singleRootItemName = hasSingleRootItem ? allRootItems.FirstOrDefault() : null;

            string? duplicateFolderName = null;

            // 二重フォルダ構造の判定
            if (rootFolders.Count == 1 && rootFiles.Count == 0)
            {
                var rootFolderName = rootFolders.First();

                // 第2階層にフォルダが1つのみで、ファイルがないことを確認
                if (structure.SecondLevelFolders.TryGetValue(rootFolderName, out var slFolders) &&
                    slFolders.Count == 1 &&
                    !(structure.SecondLevelFiles.TryGetValue(rootFolderName, out var slFiles) && slFiles.Count > 0))
                {
                    var secondLevelFolderName = slFolders.First();

                    // ルートフォルダ名と第2階層フォルダ名が同一か確認
                    if (string.Equals(rootFolderName, secondLevelFolderName, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicateFolderName = secondLevelFolderName;
                        Logger.Log($"二重フォルダ構造を検出: {rootFolderName}/{secondLevelFolderName}");
                    }
                }
            }

            return new ArchiveStructureInfo
            {
                DuplicateFolderName = duplicateFolderName,
                HasSingleRootItem = hasSingleRootItem,
                SingleRootItemName = singleRootItemName
            };
        }
        catch (Exception ex)
        {
            Logger.Log($"アーカイブ構造解析エラー: {ex.Message}");
            return new ArchiveStructureInfo { HasSingleRootItem = false };
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
        /// プロパティ: 第2階層のフォルダ名の辞書（キー: ルート名）
        /// </summary>
        public Dictionary<string, HashSet<string>> SecondLevelFolders { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// プロパティ: 第2階層のファイル名の辞書（キー: ルート名）
        /// </summary>
        public Dictionary<string, HashSet<string>> SecondLevelFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// アーカイブの先頭2階層を解析し、フォルダとファイルの情報を格納した構造を返す
    /// </summary>
    /// <param name="reader">アーカイブリーダー</param>
    /// <returns>解析結果を格納したArchiveStructure</returns>
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
            if (segment.Length > 0 && IgnoredSystemDirectories.Contains(segment.ToString()))
                return true;
            path = sepIndex < 0 ? [] : path[(sepIndex + 1)..];
        }
        return false;
    }

    private static ArchiveStructure ParseArchiveFirstTwoLevels(ArchiveReader reader)
    {
        var structure = new ArchiveStructure();

        // ローカル関数: 辞書のキーに対応する HashSet に値を追加（なければ作成）
        void AddToHierarchy(Dictionary<string, HashSet<string>> dict, string key, string value)
        {
            if (!dict.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                dict[key] = set;
            }
            set.Add(value);
        }

        foreach (var item in reader.Items)
        {
            // パスを正規化（バックスラッシュをスラッシュに）
            var path = item.FullName.Replace('\\', '/');
            var parts = path.Split(['/'], StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0) continue;

            var rootName = parts[0];
            if (IgnoredSystemDirectories.Contains(rootName)) continue;
            if (!item.IsDirectory && parts.Length > 0 && IgnoredSystemFiles.Contains(parts[^1])) continue;

            if (parts.Length == 1)
            {
                // ルートレベルのアイテム
                if (item.IsDirectory)
                {
                    structure.RootFolders.Add(rootName);
                }
                else
                {
                    structure.RootFiles.Add(rootName);
                }
            }
            else
            {
                // 子要素を持つため、ルートはフォルダ
                structure.RootFolders.Add(rootName);

                var secondLevelName = parts[1];

                // parts.Length == 2 かつ item がファイルの場合のみ SecondLevelFiles に追加
                if (parts.Length == 2 && !item.IsDirectory)
                {
                    AddToHierarchy(structure.SecondLevelFiles, rootName, secondLevelName);
                }
                else
                {
                    // item がディレクトリであるか、より深い階層を持つ場合は、第2階層はフォルダとして扱う
                    AddToHierarchy(structure.SecondLevelFolders, rootName, secondLevelName);
                }
            }
        }

        return structure;
    }

    /// <summary>
    /// アーカイブ内のファイルと展開先の既存ファイルを突き合わせて衝突を検出する。
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="outputPath">展開先ディレクトリのパス</param>
    /// <param name="duplicateFolderName">二重フォルダ構造の内側フォルダ名（スマート解凍用）</param>
    /// <returns>衝突するファイルの競合グループリスト。衝突がなければ空リスト</returns>
    public static List<Models.FileConflictGroup> DetectExtractionConflicts(string archivePath, string outputPath, string? duplicateFolderName = null)
    {
        var conflicts = new List<Models.FileConflictGroup>();

        try
        {
            using var reader = new ArchiveReader(archivePath);

            foreach (var item in reader.Items)
            {
                if (item.IsDirectory) continue;

                var relativePath = item.FullName.Replace('\\', '/');

                // 二重フォルダ構造のスキップ
                if (!string.IsNullOrEmpty(duplicateFolderName))
                {
                    var prefix = duplicateFolderName + "/";
                    if (relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        relativePath = relativePath[prefix.Length..];
                }

                // システムファイル・ディレクトリの除外
                var fileName = Path.GetFileName(relativePath);
                if (IgnoredSystemFiles.Contains(fileName)) continue;
                if (ContainsIgnoredDirectory(relativePath)) continue;

                // 展開先に同名ファイルが存在するかチェック
                var destFilePath = Path.Combine(outputPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
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
    /// <param name="duplicateFolderName">二重フォルダ構造が検出された場合の内側のフォルダ名（スマート解凍用）</param>
    /// <param name="overwriteCheckPaths">上書き確認を行う対象パス（nullの場合はoutputPathで判定。親フォルダ直下展開時は実際に上書きされるパスのみ渡す）</param>
    /// <returns>展開処理の完了を表すTask</returns>
    public static async Task ExtractArchiveAsync(string archivePath, string outputPath, IProgress<ProgressInfo>? progress = null, Window? parentWindow = null, CancellationToken cancellationToken = default, string? duplicateFolderName = null, IReadOnlyList<string>? overwriteCheckPaths = null, View.ProgressWindow? progressWindow = null)
    {
        Logger.Log($"ExtractArchiveAsync開始: archivePath={archivePath}, outputPath={outputPath}, duplicateFolderName={duplicateFolderName}");
        cancellationToken.ThrowIfCancellationRequested();

        // 展開前のディスク容量チェック
        var requiredSize = DiskSpaceChecker.GetArchiveUncompressedSize(archivePath);
        if (requiredSize > 0)
        {
            var hasSpace = await DiskSpaceChecker.EnsureDiskSpaceAsync(
                outputPath, requiredSize, parentWindow, cancellationToken);
            if (!hasSpace)
                throw new OperationCanceledException("ディスク容量不足でキャンセルされました。");
        }

        // 展開先に既存ファイルがあるかチェック（一時展開方式の判定）
        var hasExistingFiles = ShouldShowOverwriteDialog(outputPath, overwriteCheckPaths);

        if (hasExistingFiles && parentWindow != null)
        {
            // 一時フォルダ方式: 一時展開 → 衝突検出 → ダイアログ → 移動
            await ExtractViaTempFolderAsync(archivePath, outputPath, progress, parentWindow, cancellationToken, duplicateFolderName, progressWindow);
        }
        else
        {
            // 衝突なし: 直接展開
            await Task.Run(async () =>
            {
                var progressCallback = progress != null ? new Action<ProgressInfo>(p => progress.Report(p)) : null;
                try
                {
                    await ExtractArchive(archivePath, outputPath, progressCallback, parentWindow, false, cancellationToken, duplicateFolderName, overwriteCheckPaths, null);
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
    private static async Task ExtractViaTempFolderAsync(string archivePath, string outputPath, IProgress<ProgressInfo>? progress, Window parentWindow, CancellationToken cancellationToken, string? duplicateFolderName, View.ProgressWindow? progressWindow)
    {
        // 一時フォルダを出力先ディレクトリ直下に作成（同一ドライブでFile.Moveが高速、かつ書き込み権限が確実）
        var tempDir = CreateTempDirectory("Temp", outputPath);
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
                    await ExtractArchive(archivePath, tempDir, progressCallback, null, false, cancellationToken, duplicateFolderName, null, null);
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

            if (conflicts.Count > 0)
            {
                Logger.Log($"一時展開後のファイル衝突: {conflicts.Count}件");

                // ③ ダイアログ表示（両方実在するのでサムネイル完全対応）
                var (result, selectedFiles) = await View.FileConflictDialog.ShowFromBackgroundAsync(conflicts, parentWindow);
                if (result == Models.FileConflictResult.Cancel)
                {
                    Logger.Log("ユーザーが展開をキャンセル");
                    throw new OperationCanceledException("ユーザーが展開処理をキャンセルしました。");
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

                await MoveExtractedFilesAsync(tempDir, outputPath, skipPaths, cancellationToken);
            }
            else
            {
                // 衝突なし: 全ファイルを移動
                await MoveExtractedFilesAsync(tempDir, outputPath, null, cancellationToken);
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

        if (!Directory.Exists(sourceDir) || !Directory.Exists(destDir))
            return conflicts;

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, sourceFile).Replace('\\', '/');
            var destFile = Path.Combine(destDir, relativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(destFile)) continue;

            // システムファイル・ディレクトリをスキップ
            var fileName = Path.GetFileName(relativePath);
            if (IgnoredSystemFiles.Contains(fileName)) continue;
            if (ContainsIgnoredDirectory(relativePath)) continue;

            var sourceInfo = new FileInfo(sourceFile);
            var destInfo = new FileInfo(destFile);

            // 左=アーカイブから展開されたファイル、右=既存ファイル
            var archiveEntry = new Models.FileConflictEntry(
                sourceFile, relativePath, sourceInfo.Length, sourceInfo.LastWriteTime);
            var existingEntry = new Models.FileConflictEntry(
                destFile, relativePath, destInfo.Length, destInfo.LastWriteTime);

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
            foreach (var sourceFile in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(sourceDir, sourceFile).Replace('\\', '/');

                // スキップ対象チェック
                if (skipPaths != null && skipPaths.Contains(relativePath))
                    continue;

                var destFile = Path.Combine(destDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
                var destFileDir = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(destFileDir))
                    Directory.CreateDirectory(destFileDir);

                // 既存ファイルがあれば削除してから移動
                if (File.Exists(destFile))
                    File.Delete(destFile);

                File.Move(sourceFile, destFile);
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
    /// <param name="duplicateFolderName">二重フォルダ構造が検出された場合の内側のフォルダ名（スマート解凍用）</param>
    /// <param name="overwriteCheckPaths">上書き確認を行う対象パス（nullの場合はoutputPathで判定）</param>
    public static async Task ExtractArchive(string archivePath, string outputPath, Action<ProgressInfo>? progressCallback = null, Window? parentWindow = null, bool overwriteConfirmed = false, CancellationToken cancellationToken = default, string? duplicateFolderName = null, IReadOnlyList<string>? overwriteCheckPaths = null, HashSet<string>? skipRelativePaths = null)
    {
        Logger.Log($"ExtractArchive開始: archivePath={archivePath}, outputPath={outputPath}, overwriteConfirmed={overwriteConfirmed}, duplicateFolderName={duplicateFolderName}");

        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"アーカイブファイルが見つかりません: {archivePath}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // ファイル単位の衝突検出（上位で未確認の場合）
        var outputOrOverwriteExists = ShouldShowOverwriteDialog(outputPath, overwriteCheckPaths);

        if (outputOrOverwriteExists)
        {
            if (!overwriteConfirmed)
            {
                var conflicts = DetectExtractionConflicts(archivePath, outputPath, duplicateFolderName);
                if (conflicts.Count > 0)
                {
                    Logger.Log($"ExtractArchive内でファイル衝突を検出: {conflicts.Count}件");
                    var (result, _) = await View.FileConflictDialog.ShowFromBackgroundAsync(conflicts, parentWindow);
                    if (result == Models.FileConflictResult.Cancel)
                        throw new OperationCanceledException("ユーザーが展開処理をキャンセルしました。");
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

        try
        {
            Directory.CreateDirectory(tempOutputPath);
            cancellationToken.ThrowIfCancellationRequested();

            // Filter で無視しディスクに書き込まないシステム名
            var filterNames = IgnoredSystemDirectories.Concat(IgnoredSystemFiles).ToArray();
            var extractOption = new ArchiveOption { Filter = Filter.From(filterNames) };

            // ネイティブ側（7z.dll）との連携を確実に保護するため
            // using スコープ内で reader と progress を管理する
            using (var reader = new ArchiveReader(archivePath, (string?)null, extractOption))
            {
                Logger.Log($"一時ディレクトリへの展開処理開始: {archivePath} -> {tempOutputPath}");

                if (progressCallback != null)
                {
                    // 進捗スロットリング（UIスレッド負荷軽減用）
                    var throttler = new ProgressThrottler();

                    // キャンセル可能な進捗報告オブジェクト（using でスコープを維持）
                    using var progress = new CancellableProgress<Report>(report =>
                    {
                        var percentage = (int)(report.GetRatio() * 100);
                        if (throttler.ShouldReport(percentage))
                            progressCallback(new ProgressInfo(percentage, "ファイルを展開中..."));
                    }, cancellationToken);

                    reader.Save(tempOutputPath, progress);

                    // キャンセルされていたらここで一度だけスロー（コールバック内ではスローしない）
                    cancellationToken.ThrowIfCancellationRequested();

                    // Terminate で 100% を保証（Ice アプリケーションの実装パターンに準拠）
                    progressCallback(new ProgressInfo(100, "ファイルを展開中..."));

                    // ネイティブ側のコールバック完了を確実に保証
                    NativeInteropHelper.KeepAliveCallbacks(progress, progressCallback);
                }
                else
                {
                    reader.Save(tempOutputPath);
                }

                // reader自体の生存も保証
                NativeInteropHelper.KeepAliveCallbacks(reader);
            }

            // スキップ対象ファイルを一時ディレクトリから削除（移動前に除外）
            if (skipRelativePaths is { Count: > 0 })
            {
                Logger.Log($"スキップ対象の {skipRelativePaths.Count} ファイルを一時ディレクトリから除外");
                foreach (var relativePath in skipRelativePaths)
                {
                    var tempFilePath = Path.Combine(tempOutputPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
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

            // スマート解凍：二重フォルダの場合はリフトアップを行う
            if (duplicateFolderName != null)
            {
                var rootItemName = duplicateFolderName;

                var rootPath = Path.Combine(tempOutputPath, rootItemName);
                var innerFolderPath = Path.Combine(rootPath, rootItemName);

                if (Directory.Exists(innerFolderPath))
                {
                    Logger.Log($"スマート解凍：二重フォルダ '{rootItemName}' をリフトアップします");

                    // 一時ディレクトリを作成して、内側フォルダの中身を移動
                    var tempLiftUpPath = CreateTempDirectory("LiftUp");
                    try
                    {
                        // 内側フォルダの中身を一時ディレクトリに移動
                        MoveDirectoryContents(innerFolderPath, tempLiftUpPath);

                        // 空になった内側フォルダを削除
                        RemoveReadOnlyAttributes(innerFolderPath);
                        Directory.Delete(innerFolderPath, true);

                        // 一時ディレクトリの中身を外側のフォルダ(rootPath)に移動
                        MoveDirectoryContents(tempLiftUpPath, rootPath);

                        Logger.Log("リフトアップが完了しました");
                    }
                    finally
                    {
                        // 一時ディレクトリをクリーンアップ
                        try
                        {
                            if (Directory.Exists(tempLiftUpPath))
                            {
                                RemoveReadOnlyAttributes(tempLiftUpPath);
                                Directory.Delete(tempLiftUpPath, true);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"リフトアップ処理の一時ディレクトリ削除に失敗しました: {tempLiftUpPath}, {ex.Message}", LogLevel.Warning);
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
                throw new InvalidOperationException("展開先の準備中にエラーが発生しました。ファイルが使用中か、削除権限がない可能性があります。", ex);
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
                throw new InvalidOperationException("展開先への内容移動に失敗しました。元の内容は退避先に残っています。", ex);
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

            // 破損ファイルの場合は詳細分析を実行
            if (errorInfo.ErrorType == ArchiveErrorType.CorruptedFile)
            {
                Logger.Log("破損ファイルの詳細分析を実行します");
                var corruptionAnalysis = ArchiveErrorHandler.AnalyzeCorruption(archivePath);
                Logger.Log($"破損分析結果: 破損={corruptionAnalysis.IsCorrupted}, 種類={corruptionAnalysis.CorruptionType}, 回復率={corruptionAnalysis.RecoveryRate:F1}%");
            }

            throw;
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
