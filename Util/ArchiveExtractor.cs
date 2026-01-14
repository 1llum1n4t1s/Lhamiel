using System.IO;
using System.IO.Compression;
using Cube.FileSystem.SevenZip;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;
using System.Threading;

namespace Lhamiel.Util;

/// <summary>
/// アーカイブ展開機能
/// </summary>
public class ArchiveExtractor
{
    /// <summary>
    /// サポートされている展開形式の一覧
    /// </summary>
    private static readonly string[] SupportedExtensions = { ".zip", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".tbz", ".lzma", ".tlz", ".xz", ".txz", ".rar", ".lzh", ".cab", ".arj", ".z", ".tZ", ".exe" };

    /// <summary>
    /// 指定されたファイルがサポートされているアーカイブ形式かどうかを確認する
    /// </summary>
    /// <param name="filePath">確認するファイルのパス</param>
    /// <returns>サポートされている形式の場合はtrue、そうでなければfalse</returns>
    public static bool IsSupportedArchiveType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        if (!SupportedExtensions.Contains(extension))
        {
            return false;
        }
        
        // .exeファイルの場合は自己展開圧縮ファイルかどうかを確認
        if (extension == ".exe")
        {
            return ArchiveFormatDetector.IsSelfExtractingArchive(filePath);
        }
        
        return true;
    }

    /// <summary>
    /// アーカイブファイルの展開先ディレクトリを取得する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="defaultOutputDir">デフォルトの出力ディレクトリ</param>
    /// <param name="outputToSameDirectory">同じディレクトリに出力するかどうか</param>
    /// <returns>展開先ディレクトリのパス</returns>
    public static string GetOutputDirectory(string archivePath, string defaultOutputDir, bool outputToSameDirectory = false)
    {
        var directory = Path.GetDirectoryName(archivePath) ?? "";
        var fileName = Path.GetFileNameWithoutExtension(archivePath);
        var baseDirectory = outputToSameDirectory ? directory : defaultOutputDir;

        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = directory;
        }
        
        // アーカイブの内容をチェックして、二重フォルダを避ける
        var adjustedFileName = GetAdjustedFileName(archivePath, fileName);
        
        // 空文字列が返された場合は、アーカイブファイルと同じディレクトリに展開
        if (string.IsNullOrEmpty(adjustedFileName))
        {
            return baseDirectory;
        }
        
        return Path.Combine(baseDirectory, adjustedFileName);
    }

    /// <summary>
    /// アーカイブの内容をチェックして、適切なファイル名を取得する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="defaultFileName">デフォルトのファイル名</param>
    /// <returns>調整されたファイル名（通常は defaultFileName、二重フォルダ防止の場合は空文字列）</returns>
    private static string GetAdjustedFileName(string archivePath, string defaultFileName)
    {
        try
        {
            using var reader = new ArchiveReader(archivePath);

            // アーカイブの内容を取得
            var archiveContents = reader.Items.Select(item => item.FullName).ToList();

            if (!archiveContents.Any())
                return defaultFileName;

            // 二重フォルダ防止が必要かどうかをチェック（再帰的）
            if (ShouldPreventDoubleFolders(archiveContents, defaultFileName))
            {
                Logger.Log("二重フォルダ防止が必要: 空文字列を返します");
                return "";
            }

            return defaultFileName;
        }
        catch (Exception ex)
        {
            Logger.Log($"アーカイブ内容のチェックでエラーが発生しました: {archivePath}, {ex.Message}");
            return defaultFileName;
        }
    }

    /// <summary>
    /// 二重フォルダ防止が必要かどうかをチェック（再帰的に同名フォルダをチェック）
    /// </summary>
    /// <param name="archiveContents">アーカイブの全アイテムパス</param>
    /// <param name="expectedFolderName">期待されるフォルダ名（アーカイブ名）</param>
    /// <returns>二重フォルダ防止が必要な場合はtrue</returns>
    private static bool ShouldPreventDoubleFolders(List<string> archiveContents, string expectedFolderName)
    {
        return ShouldPreventDoubleFoldersRecursive(archiveContents, expectedFolderName, "");
    }

    /// <summary>
    /// 二重フォルダ防止が必要かどうかを再帰的にチェック
    /// </summary>
    /// <param name="archiveContents">アーカイブの全アイテムパス</param>
    /// <param name="expectedFolderName">期待されるフォルダ名（アーカイブ名）</param>
    /// <param name="currentPath">現在のパス（再帰的に深くなる）</param>
    /// <returns>二重フォルダ防止が必要な場合はtrue</returns>
    private static bool ShouldPreventDoubleFoldersRecursive(List<string> archiveContents, string expectedFolderName, string currentPath)
    {
        Logger.Log($"ShouldPreventDoubleFoldersRecursive: currentPath='{currentPath}'");

        // 現在のパスでのアイテムを取得
        string? folderName = null;
        bool isDirectory = false;
        int itemCount = 0;

        if (string.IsNullOrEmpty(currentPath))
        {
            // ルートレベルのアイテムを取得
            var rootItems = GetRootLevelItems(archiveContents);
            itemCount = rootItems.Count;

            if (rootItems.Count == 1)
            {
                folderName = rootItems[0].Name;
                isDirectory = rootItems[0].IsDirectory;
            }
        }
        else
        {
            // 指定されたフォルダ内のアイテムを取得
            var items = GetItemsInFolder(archiveContents, currentPath);
            itemCount = items.Count;

            if (items.Count == 1)
            {
                folderName = items[0].Path;
                isDirectory = items[0].IsDirectory;
            }
        }

        Logger.Log($"  itemCount={itemCount}, folderName={folderName}, isDirectory={isDirectory}");

        // アイテムが1つもない場合は防止不要
        if (itemCount == 0)
            return false;

        // アイテムが1つだけで、それがディレクトリの場合
        if (itemCount == 1 && isDirectory && !string.IsNullOrEmpty(folderName))
        {
            Logger.Log($"  単一フォルダ: {folderName}");

            // フォルダ名が期待される名前と一致する場合（大文字小文字を区別しない）
            if (string.Equals(folderName, expectedFolderName, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log($"  フォルダ名が一致: {folderName} == {expectedFolderName}");

                // 次のパスを構築
                var nextPath = string.IsNullOrEmpty(currentPath)
                    ? folderName
                    : currentPath + "/" + folderName;

                // 再帰的にチェック
                return ShouldPreventDoubleFoldersRecursive(archiveContents, expectedFolderName, nextPath);
            }
            else
            {
                // フォルダ名が異なる場合、ここで終了
                // 親階層で同名フォルダがあったなら、二重フォルダ防止が必要
                Logger.Log($"  フォルダ名が不一致: {folderName} != {expectedFolderName}");
                return !string.IsNullOrEmpty(currentPath);
            }
        }

        // 複数のアイテムがある場合、ここで終了
        // 親階層で同名フォルダがあったなら、二重フォルダ防止が必要
        Logger.Log($"  複数アイテムまたはファイル: itemCount={itemCount}");
        return !string.IsNullOrEmpty(currentPath);
    }

    /// <summary>
    /// ルートレベルのアイテムを取得する
    /// </summary>
    /// <param name="archiveContents">アーカイブの全アイテムパス</param>
    /// <returns>ルートレベルのアイテムリスト</returns>
    private static List<(string Name, bool IsDirectory)> GetRootLevelItems(List<string> archiveContents)
    {
        var rootItems = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in archiveContents)
        {
            // パスをノーマライズ（バックスラッシュをスラッシュに統一）
            var normalizedPath = path.Replace('\\', '/');

            // 末尾のスラッシュを削除
            normalizedPath = normalizedPath.TrimEnd('/');

            // ルートレベルの要素を抽出
            var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length > 0)
            {
                var rootName = parts[0];
                var isDirectory = path.EndsWith("/") || path.EndsWith("\\") || parts.Length > 1;

                if (!rootItems.ContainsKey(rootName))
                {
                    rootItems[rootName] = isDirectory;
                }
                else
                {
                    // isDirectory が true の場合は true に更新（ファイルだと思っていたものがディレクトリだった場合）
                    rootItems[rootName] = rootItems[rootName] || isDirectory;
                }
            }
        }

        return rootItems.Select(kvp => (kvp.Key, kvp.Value)).ToList();
    }

    /// <summary>
    /// 指定されたフォルダ内のアイテムを取得する
    /// </summary>
    /// <param name="archiveContents">アーカイブの全アイテムパス</param>
    /// <param name="folderName">対象フォルダ名</param>
    /// <returns>フォルダ内のアイテムリスト</returns>
    private static List<(string Path, bool IsDirectory)> GetItemsInFolder(List<string> archiveContents, string folderName)
    {
        var items = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        var folderPrefix1 = folderName + "/";
        var folderPrefix2 = folderName + "\\";

        foreach (var path in archiveContents)
        {
            // フォルダプレフィックスをチェック
            if (!path.StartsWith(folderPrefix1, StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith(folderPrefix2, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // パスをノーマライズ
            var normalizedPath = path.Replace('\\', '/');

            // フォルダプレフィックスを削除
            var relativePath = normalizedPath.Substring(folderName.Length).TrimStart('/');

            // 空またはフォルダ自体の場合はスキップ
            if (string.IsNullOrEmpty(relativePath))
                continue;

            // 末尾のスラッシュを削除
            relativePath = relativePath.TrimEnd('/');

            // ルートレベルの要素を抽出
            var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length > 0)
            {
                var itemName = parts[0];
                var isDirectory = path.EndsWith("/") || path.EndsWith("\\") || parts.Length > 1;

                if (!items.ContainsKey(itemName))
                {
                    items[itemName] = isDirectory;
                }
                else
                {
                    // isDirectory が true の場合は true に更新（ファイルだと思っていたものがディレクトリだった場合）
                    items[itemName] = items[itemName] || isDirectory;
                }
            }
        }

        return items.Select(kvp => (kvp.Key, kvp.Value)).ToList();
    }

    /// <summary>
    /// アーカイブを展開する（非同期版）
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="outputPath">展開先ディレクトリのパス</param>
    /// <param name="progress">進捗コールバック</param>
    /// <param name="parentWindow">親ウィンドウ（上書き確認ダイアログ用）</param>
    /// <returns>展開処理の完了を表すTask</returns>
    public static async Task ExtractArchiveAsync(string archivePath, string outputPath, IProgress<int>? progress = null, System.Windows.Window? parentWindow = null, CancellationToken cancellationToken = default)
    {
        Logger.Log($"ExtractArchiveAsync開始: archivePath={archivePath}, outputPath={outputPath}, parentWindow={parentWindow?.GetType().Name ?? "null"}");

        cancellationToken.ThrowIfCancellationRequested();

        // 上書き確認が必要かどうかを事前にチェック
        var needsOverwriteConfirmation = Directory.Exists(outputPath);
        Logger.Log($"展開先ディレクトリ存在チェック: outputPath={outputPath}, exists={needsOverwriteConfirmation}");

        if (needsOverwriteConfirmation && parentWindow != null)
        {
            Logger.Log("上書き確認ダイアログを表示します");
            // UIスレッドで上書き確認を実行
            var canOverwrite = await parentWindow.Dispatcher.InvokeAsync(() =>
                FileOverwriteDialog.CanOverwriteFile(archivePath, outputPath, parentWindow));

            Logger.Log($"上書き確認ダイアログ結果: canOverwrite={canOverwrite}");

            if (!canOverwrite)
            {
                throw new OperationCanceledException("ユーザーが展開処理をキャンセルしました。");
            }
        }
        else
        {
            Logger.Log($"上書き確認ダイアログをスキップ: needsOverwriteConfirmation={needsOverwriteConfirmation}, parentWindow={parentWindow != null}");
        }

        // 非同期タスクで展開処理を実行
        await Task.Run(() =>
        {
            var extractor = new ArchiveExtractor();
            var progressCallback = progress != null ? new Action<int>(p => progress.Report(p)) : null;
            extractor.ExtractArchive(archivePath, outputPath, progressCallback, parentWindow, needsOverwriteConfirmation, cancellationToken);
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
    public void ExtractArchive(string archivePath, string outputPath, Action<int>? progressCallback = null, System.Windows.Window? parentWindow = null, bool overwriteConfirmed = false, CancellationToken cancellationToken = default)
    {
        Logger.Log($"ExtractArchive開始: archivePath={archivePath}, outputPath={outputPath}, overwriteConfirmed={overwriteConfirmed}");

        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"アーカイブファイルが見つかりません: {archivePath}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 展開先ディレクトリが既に存在する場合は上書き確認
        if (Directory.Exists(outputPath) && !overwriteConfirmed)
        {
            Logger.Log("ExtractArchive内で上書き確認ダイアログを表示します");
            var canOverwrite = FileOverwriteDialog.CanOverwriteFile(archivePath, outputPath, parentWindow);
            Logger.Log($"ExtractArchive内の上書き確認結果: canOverwrite={canOverwrite}");

            if (!canOverwrite)
            {
                throw new OperationCanceledException("ユーザーが展開処理をキャンセルしました。");
            }

            // 上書きが許可された場合は既存ディレクトリを削除
            try
            {
                RemoveReadOnlyAttributes(outputPath);
                Directory.Delete(outputPath, true);
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.Log($"ディレクトリの削除に失敗しました（アクセス拒否）: {outputPath}, {ex.Message}");
                throw new InvalidOperationException($"展開先ディレクトリ '{Path.GetFileName(outputPath)}' が他のアプリケーションで使用されているため削除できません。\nディレクトリを閉じてから再度お試しください。", ex);
            }
            catch (IOException ex)
            {
                Logger.Log($"ディレクトリの削除に失敗しました（I/Oエラー）: {outputPath}, {ex.Message}");
                throw new InvalidOperationException($"展開先ディレクトリ '{Path.GetFileName(outputPath)}' の削除に失敗しました。\nディレクトリ内のファイルが使用中である可能性があります。", ex);
            }
        }

        var outputPrepared = false;
        var tempExtractionPath = "";

        try
        {
            // 出力ディレクトリを作成
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
                outputPrepared = true;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 二重フォルダ防止のための一時展開パスを確認
            tempExtractionPath = GetTemporaryExtractionPath(archivePath, outputPath);
            var useTempPath = !string.IsNullOrEmpty(tempExtractionPath) && tempExtractionPath != outputPath;

            Logger.Log($"一時展開パス使用: {useTempPath}, パス: {tempExtractionPath}");

            var extractPath = useTempPath ? tempExtractionPath : outputPath;

            using (var reader = new ArchiveReader(archivePath))
            {
                // 進捗報告を設定
                if (progressCallback != null)
                {
                    var progress = new Progress<Report>(report =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var percentage = report.TotalBytes > 0 ? (int)((report.Bytes * 100) / report.TotalBytes) : 0;
                        progressCallback(percentage);
                    });

                    reader.Save(extractPath, progress);
                }
                else
                {
                    reader.Save(extractPath);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 一時パスから本来のパスにファイルをリフトアップ
            if (useTempPath && Directory.Exists(tempExtractionPath))
            {
                LiftUpFilesFromTemporaryPath(tempExtractionPath, outputPath);

                // 一時ディレクトリを削除
                try
                {
                    RemoveReadOnlyAttributes(tempExtractionPath);
                    Directory.Delete(tempExtractionPath, true);
                    Logger.Log($"一時ディレクトリを削除しました: {tempExtractionPath}");
                }
                catch (Exception ex)
                {
                    Logger.Log($"一時ディレクトリの削除に失敗しました: {tempExtractionPath}, {ex.Message}");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            Logger.Log($"アーカイブ展開完了: {archivePath} -> {outputPath}");
        }
        catch (OperationCanceledException)
        {
            if (outputPrepared && Directory.Exists(outputPath))
            {
                try
                {
                    RemoveReadOnlyAttributes(outputPath);
                    Directory.Delete(outputPath, true);
                }
                catch (Exception ex)
                {
                    Logger.Log($"キャンセル時の出力ディレクトリ削除に失敗しました: {outputPath}, {ex.Message}");
                }
            }

            if (!string.IsNullOrEmpty(tempExtractionPath) && Directory.Exists(tempExtractionPath))
            {
                try
                {
                    RemoveReadOnlyAttributes(tempExtractionPath);
                    Directory.Delete(tempExtractionPath, true);
                }
                catch (Exception ex)
                {
                    Logger.Log($"キャンセル時の一時ディレクトリ削除に失敗しました: {tempExtractionPath}, {ex.Message}");
                }
            }

            throw;
        }
        catch (Exception ex)
        {
            // 詳細なエラー分析を実行
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
    /// 二重フォルダ防止のための一時展開パスを取得する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="outputPath">展開先ディレクトリのパス</param>
    /// <returns>一時展開パス、または二重フォルダ防止が不要な場合は outputPath と同じ値</returns>
    private static string GetTemporaryExtractionPath(string archivePath, string outputPath)
    {
        try
        {
            using var reader = new ArchiveReader(archivePath);

            // アーカイブの内容を取得
            var archiveContents = reader.Items.Select(item => item.FullName).ToList();

            if (!archiveContents.Any())
                return outputPath;

            // アーカイブファイル名（拡張子なし）を取得
            var expectedFolderName = Path.GetFileNameWithoutExtension(archivePath);

            // 二重フォルダ防止が必要かどうかをチェック（再帰的）
            if (ShouldPreventDoubleFolders(archiveContents, expectedFolderName))
            {
                // 一時ディレクトリを作成（outputPath の親ディレクトリに）
                var parentDir = Path.GetDirectoryName(outputPath) ?? Path.GetTempPath();
                var tempDirName = Path.GetFileName(outputPath) + "_temp_" + Guid.NewGuid().ToString().Substring(0, 8);
                var tempPath = Path.Combine(parentDir, tempDirName);

                Logger.Log($"一時展開パスを返す: {tempPath}");
                return tempPath;
            }

            return outputPath;
        }
        catch (Exception ex)
        {
            Logger.Log($"一時展開パス取得でエラーが発生しました: {archivePath}, {ex.Message}");
            return outputPath;
        }
    }

    /// <summary>
    /// 一時展開パスからファイルをリフトアップして本来のパスに配置する（再帰的に同名フォルダを辿る）
    /// </summary>
    /// <param name="tempPath">一時展開パス</param>
    /// <param name="outputPath">本来の展開先パス</param>
    private static void LiftUpFilesFromTemporaryPath(string tempPath, string outputPath)
    {
        try
        {
            Logger.Log($"ファイルをリフトアップ開始: {tempPath} -> {outputPath}");

            // 再帰的に最深の有効なフォルダを見つける
            var sourcePath = FindDeepestValidFolder(tempPath, Path.GetFileName(outputPath));

            Logger.Log($"リフトアップ元パス: {sourcePath}");

            // 本来のパスがまだ存在しない場合は作成
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            // 見つかったフォルダのすべてのファイルとフォルダを本来のパスに移動
            foreach (var file in Directory.GetFiles(sourcePath))
            {
                var destFile = Path.Combine(outputPath, Path.GetFileName(file));
                RemoveReadOnlyAttributes(file);

                if (File.Exists(destFile))
                {
                    File.Delete(destFile);
                }

                File.Move(file, destFile);
                Logger.Log($"ファイルを移動: {file} -> {destFile}");
            }

            foreach (var dir in Directory.GetDirectories(sourcePath))
            {
                var destDir = Path.Combine(outputPath, Path.GetFileName(dir));

                if (Directory.Exists(destDir))
                {
                    RemoveReadOnlyAttributes(destDir);
                    Directory.Delete(destDir, true);
                }

                Directory.Move(dir, destDir);
                Logger.Log($"ディレクトリを移動: {dir} -> {destDir}");
            }

            Logger.Log("ファイルのリフトアップが完了しました");
        }
        catch (Exception ex)
        {
            Logger.Log($"ファイルのリフトアップでエラーが発生しました: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 再帰的に最深の有効なフォルダを見つける（同名フォルダのネストを辿る）
    /// </summary>
    /// <param name="currentPath">現在のパス</param>
    /// <param name="expectedFolderName">期待されるフォルダ名</param>
    /// <returns>最深の有効なフォルダのパス</returns>
    private static string FindDeepestValidFolder(string currentPath, string expectedFolderName)
    {
        Logger.Log($"FindDeepestValidFolder: currentPath={currentPath}, expectedFolderName={expectedFolderName}");

        var directories = Directory.GetDirectories(currentPath);

        // サブディレクトリが1つだけの場合
        if (directories.Length == 1)
        {
            var innerDirPath = directories[0];
            var innerDirName = Path.GetFileName(innerDirPath);

            Logger.Log($"  サブディレクトリ: {innerDirName}");

            // フォルダ名が期待される名前と一致する場合（大文字小文字を区別しない）
            if (string.Equals(innerDirName, expectedFolderName, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log($"  フォルダ名が一致: {innerDirName} == {expectedFolderName}、さらに深く探索");
                // 再帰的にさらに深く探索
                return FindDeepestValidFolder(innerDirPath, expectedFolderName);
            }
            else
            {
                // フォルダ名が異なる場合、または複数のアイテムがある場合、このフォルダの中身をリフトアップ
                Logger.Log($"  フォルダ名が不一致: {innerDirName} != {expectedFolderName}、このフォルダの中身をリフトアップ");
                return innerDirPath;
            }
        }

        // サブディレクトリが0個、または複数ある場合は現在のパスを返す
        Logger.Log($"  サブディレクトリ数={directories.Length}、現在のパスを返す");
        return currentPath;
    }

    /// <summary>
    /// ファイルまたはディレクトリの読み取り専用属性を削除する
    /// </summary>
    /// <param name="path">対象のファイルまたはディレクトリパス</param>
    private static void RemoveReadOnlyAttributes(string path)
    {
        try
        {
            // ファイルかディレクトリかを判定
            if (File.Exists(path))
            {
                try
                {
                    var fileInfo = new FileInfo(path);
                    if ((fileInfo.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        fileInfo.Attributes &= ~FileAttributes.ReadOnly;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"ファイル属性の変更に失敗しました: {path}, {ex.Message}");
                }
            }
            else if (Directory.Exists(path))
            {
                // ディレクトリ内のすべてのファイルとサブディレクトリを再帰的に処理
                foreach (var filePath in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        if ((fileInfo.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                        {
                            fileInfo.Attributes &= ~FileAttributes.ReadOnly;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"ファイル属性の変更に失敗しました: {filePath}, {ex.Message}");
                    }
                }

                // ディレクトリ自体の読み取り専用属性も削除
                try
                {
                    var dirInfo = new DirectoryInfo(path);
                    if ((dirInfo.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        dirInfo.Attributes &= ~FileAttributes.ReadOnly;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"ディレクトリ属性の変更に失敗しました: {path}, {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"読み取り専用属性の削除処理でエラーが発生しました: {path}, {ex.Message}");
        }
    }

    /// <summary>
    /// 特定のファイルを展開する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="outputPath">展開先ディレクトリのパス</param>
    /// <param name="fileNames">展開するファイル名のリスト</param>
    /// <param name="progressCallback">進捗コールバック</param>
    public void ExtractSpecificFiles(string archivePath, string outputPath, IEnumerable<string> fileNames, Action<int>? progressCallback = null)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"アーカイブファイルが見つかりません: {archivePath}");
            
        var fileNameList = fileNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!fileNameList.Any())
            throw new ArgumentException("展開するファイルが指定されていません。");
            
        if (!Directory.Exists(outputPath))
            Directory.CreateDirectory(outputPath);

        try
        {
        using var reader = new ArchiveReader(archivePath);
            
            // 現在のライブラリでは特定ファイル展開は制限されているため、
            // 全体を展開する方法を使用
        reader.Save(outputPath);
            
        progressCallback?.Invoke(100);
            Logger.Log($"特定ファイル展開完了（全体展開）: {string.Join(", ", fileNames)}");
        }
        catch (Exception ex)
        {
            Logger.Log($"特定ファイル展開でエラーが発生しました: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// アーカイブの内容を一覧表示
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <returns>アーカイブ内のファイル一覧</returns>
    public List<string> ListArchiveContents(string archivePath)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"アーカイブファイルが見つかりません: {archivePath}");
            
        var contents = new List<string>();
        
        try
        {
        using var reader = new ArchiveReader(archivePath);
            
            // Items プロパティを使用してアーカイブ内容を取得
            foreach (var item in reader.Items)
            {
                contents.Add(item.FullName);
            }
            
            Logger.Log($"アーカイブ内容確認完了: {contents.Count}個のファイル");
        }
        catch (Exception ex)
        {
            Logger.Log($"アーカイブ内容確認でエラーが発生しました: {ex.Message}");
            throw;
        }
        
        return contents;
    }

    /// <summary>
    /// 既存ファイルとの競合をチェックする
    /// </summary>
    /// <param name="reader">アーカイブリーダー</param>
    /// <param name="outputPath">出力先ディレクトリ</param>
    /// <returns>競合するファイルのパス一覧</returns>
    private static List<string> CheckForConflictingFiles(ArchiveReader reader, string outputPath)
    {
        Logger.Log($"CheckForConflictingFiles開始: outputPath={outputPath}");
        var conflictingFiles = new List<string>();
        
        try
        {
            var itemCount = 0;
            foreach (var item in reader.Items)
            {
                itemCount++;
                if (!item.IsDirectory)
                {
                    // アーカイブ内のファイルパスから、実際の展開先パスを計算
                    var relativePath = item.FullName;
                    var targetPath = Path.Combine(outputPath, relativePath);
                    
                    Logger.Log($"チェック中: アーカイブ内パス={relativePath}, 展開先パス={targetPath}");
                    
                    if (File.Exists(targetPath))
                    {
                        conflictingFiles.Add(targetPath);
                        Logger.Log($"競合ファイルを発見: {targetPath}");
                    }
                }
            }
            Logger.Log($"CheckForConflictingFiles完了: 総アイテム数={itemCount}, 競合ファイル数={conflictingFiles.Count}");
        }
        catch (Exception ex)
        {
            Logger.Log($"競合ファイルチェックでエラーが発生しました: {ex.Message}");
        }
        
        return conflictingFiles;
    }

    /// <summary>
    /// アーカイブの詳細情報を取得
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <returns>アーカイブ内のファイル詳細情報一覧</returns>
    public List<ArchiveFileInfo> GetArchiveFileInfos(string archivePath)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"アーカイブファイルが見つかりません: {archivePath}");
            
        var fileInfos = new List<ArchiveFileInfo>();
        
        try
        {
            using var reader = new ArchiveReader(archivePath);
            
            // Items プロパティを使用してアーカイブ内容の詳細情報を取得
            foreach (var item in reader.Items)
            {
                var fileInfo = new ArchiveFileInfo
                {
                    Index = (uint)item.Index,
                    Path = item.FullName,
                    Name = Path.GetFileName(item.FullName),
                    IsDirectory = item.IsDirectory,
                    Size = 0, // 現在のライブラリではサイズ情報が取得できないため0を設定
                    PackedSize = 0, // 現在のライブラリでは圧縮サイズ情報が取得できないため0を設定
                    LastWriteTime = item.LastWriteTime
                };
                
                fileInfos.Add(fileInfo);
            }
            
            Logger.Log($"アーカイブ詳細情報取得完了: {fileInfos.Count}個のエントリ");
        }
        catch (Exception ex)
        {
            Logger.Log($"アーカイブ詳細情報取得でエラーが発生しました: {ex.Message}");
            throw;
        }
        
        return fileInfos;
    }
}

/// <summary>
/// アーカイブ内のファイル情報
/// </summary>
public class ArchiveFileInfo
{
    /// <summary>
    /// アイテムインデックス
    /// </summary>
    public uint Index { get; set; }

    /// <summary>
    /// ファイルパス
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// ファイル名
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// ディレクトリかどうか
    /// </summary>
    public bool IsDirectory { get; set; }

    /// <summary>
    /// ファイルサイズ
    /// </summary>
    public ulong Size { get; set; }

    /// <summary>
    /// 圧縮サイズ
    /// </summary>
    public ulong PackedSize { get; set; }

    /// <summary>
    /// 最終更新日時
    /// </summary>
    public DateTime LastWriteTime { get; set; }
}
