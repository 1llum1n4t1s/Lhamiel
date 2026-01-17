using System.IO;
using Cube.FileSystem.SevenZip;

namespace Lhamiel.Util;

/// <summary>
/// アーカイブ展開機能
/// </summary>
public class ArchiveExtractor
{
    /// <summary>
    /// サポートされている展開形式の一覧
    /// </summary>
    private static readonly string[] SupportedExtensions = [".zip", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".tbz", ".lzma", ".tlz", ".xz", ".txz", ".rar", ".lzh", ".cab", ".arj", ".z", ".tZ", ".exe"];

    /// <summary>
    /// スマート解凍判定用：無視するシステムディレクトリ名
    /// </summary>
    private static readonly string[] IgnoredSystemDirectories = ["__MACOSX"];

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
    /// <returns>展開先ディレクトリのパス（アーカイブ名フォルダを含む）</returns>
    public static string GetOutputDirectory(string archivePath, string defaultOutputDir, bool outputToSameDirectory = false)
    {
        var baseDir = GetBaseOutputDirectory(archivePath, defaultOutputDir, outputToSameDirectory);
        var fileName = Path.GetFileNameWithoutExtension(archivePath);

        // 基本動作：アーカイブ名フォルダを作成
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
    /// アーカイブのルート要素が単一かどうかを判定し、その名前を取得する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <returns>単一のルート要素名（見つからないか複数の場合はnull）</returns>
    public static string? GetSingleRootItemName(string archivePath)
    {
        if (!File.Exists(archivePath)) return null;

        try
        {
            using var reader = new ArchiveReader(archivePath);
            var rootItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in reader.Items)
            {
                // パスを正規化（バックスラッシュをスラッシュに）
                var path = item.FullName.Replace('\\', '/');
                var parts = path.Split(['/'], StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length > 0)
                {
                    var rootItem = parts[0];

                    // システム管理用フォルダ（__MACOSXなど）は無視
                    if (IgnoredSystemDirectories.Contains(rootItem))
                    {
                        continue;
                    }

                    rootItems.Add(rootItem);

                    // 2つ以上見つかった時点でnull確定
                    if (rootItems.Count > 1)
                    {
                        return null;
                    }
                }
            }

            return rootItems.Count == 1 ? rootItems.First() : null;
        }
        catch (Exception ex)
        {
            Logger.Log($"アーカイブ構造解析エラー: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// アーカイブのルート要素が単一かどうかを判定する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <returns>ルート要素が単一の場合はtrue</returns>
    public static bool HasSingleRootItem(string archivePath)
    {
        return !string.IsNullOrEmpty(GetSingleRootItemName(archivePath));
    }


    /// <summary>
    /// アーカイブを展開する（非同期版）
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="outputPath">展開先ディレクトリのパス</param>
    /// <param name="progress">進捗コールバック</param>
    /// <param name="parentWindow">親ウィンドウ（上書き確認ダイアログ用）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <param name="rootItemNameForCleanup">キャンセル時に削除すべき単一ルートアイテム名（スマート解凍用）</param>
    /// <returns>展開処理の完了を表すTask</returns>
    public static async Task ExtractArchiveAsync(string archivePath, string outputPath, IProgress<ProgressInfo>? progress = null, System.Windows.Window? parentWindow = null, CancellationToken cancellationToken = default, string? rootItemNameForCleanup = null)
    {
        Logger.Log($"ExtractArchiveAsync開始: archivePath={archivePath}, outputPath={outputPath}, parentWindow={parentWindow?.GetType().Name ?? "null"}, rootItem={rootItemNameForCleanup ?? "null"}");

        cancellationToken.ThrowIfCancellationRequested();

        // 実際の展開先ターゲットを確認（スマート解凍時はベースパス＋ルートアイテム名）
        var actualTargetDir = rootItemNameForCleanup != null ? Path.Combine(outputPath, rootItemNameForCleanup) : outputPath;
        
        // 上書き確認が必要かどうかをチェック
        var targetExists = Directory.Exists(actualTargetDir) || File.Exists(actualTargetDir);
        Logger.Log($"展開先存在チェック: actualTargetDir={actualTargetDir}, exists={targetExists}");

        var overwriteConfirmed = false;

        if (targetExists && parentWindow != null)
        {
            // 保護されたディレクトリ（デスクトップ自体など）の場合は上書き確認（削除）をさせない
            if (PathValidator.IsProtectedDirectory(actualTargetDir))
            {
                Logger.Log($"上書き不可: 保護されたディレクトリです: {actualTargetDir}", LogLevel.Warning);
                throw new InvalidOperationException($"'{actualTargetDir}' はシステムによって保護されているため、上書き展開できません。別の場所を選択してください。");
            }

            Logger.Log($"上書き確認ダイアログを表示します: {actualTargetDir}");
            // UIスレッドで上書き確認を実行
            var canOverwrite = await parentWindow.Dispatcher.InvokeAsync(() =>
                FileOverwriteDialog.CanOverwriteFile(archivePath, actualTargetDir, parentWindow));

            Logger.Log($"上書き確認ダイアログ結果: canOverwrite={canOverwrite}");

            if (!canOverwrite)
            {
                throw new OperationCanceledException("ユーザーが展開処理をキャンセルしました。");
            }
            
            overwriteConfirmed = true;
        }
        else if (targetExists)
        {
            // parentWindow がない場合は自動的に上書き（または既存仕様に合わせる）
            Logger.Log($"上書き確認ダイアログをスキップ（parentWindowなし）: {actualTargetDir}");
            overwriteConfirmed = true;
        }

        // 非同期タスクで展開処理を実行
        await Task.Run(async () =>
        {
            var extractor = new ArchiveExtractor();
            var progressCallback = progress != null ? new Action<ProgressInfo>(p => progress.Report(p)) : null;
            await extractor.ExtractArchive(archivePath, outputPath, progressCallback, parentWindow, overwriteConfirmed, cancellationToken, rootItemNameForCleanup);
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
    /// <param name="rootItemNameForCleanup">キャンセル時に削除すべき単一ルートアイテム名</param>
    public async Task ExtractArchive(string archivePath, string outputPath, Action<ProgressInfo>? progressCallback = null, System.Windows.Window? parentWindow = null, bool overwriteConfirmed = false, CancellationToken cancellationToken = default, string? rootItemNameForCleanup = null)
    {
        Logger.Log($"ExtractArchive開始: archivePath={archivePath}, outputPath={outputPath}, overwriteConfirmed={overwriteConfirmed}, rootItem={rootItemNameForCleanup ?? "null"}");

        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"アーカイブファイルが見つかりません: {archivePath}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 実際の展開先
        var actualTargetDir = rootItemNameForCleanup != null ? Path.Combine(outputPath, rootItemNameForCleanup) : outputPath;

        // 展開先が既に存在する場合の処理
        if (Directory.Exists(actualTargetDir) || File.Exists(actualTargetDir))
        {
            if (!overwriteConfirmed)
            {
                // まだ確認されていない場合はここで確認
                Logger.Log($"ExtractArchive内で上書き確認ダイアログを表示します: {actualTargetDir}");
                var canOverwrite = FileOverwriteDialog.CanOverwriteFile(archivePath, actualTargetDir, parentWindow);
                if (!canOverwrite)
                {
                    throw new OperationCanceledException("ユーザーが展開処理をキャンセルしました。");
                }
            }

            // 上書きが許可された（または確認済み）の場合は既存の対象を削除
            try
            {
                Logger.Log($"既存の展開先を削除します: {actualTargetDir}");
                if (Directory.Exists(actualTargetDir))
                {
                    try
                    {
                        Directory.Delete(actualTargetDir, true);
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                    {
                        Logger.Log($"削除再試行（属性解除）: {actualTargetDir}");
                        RemoveReadOnlyAttributes(actualTargetDir);
                        await Task.Delay(200, cancellationToken);
                        Directory.Delete(actualTargetDir, true);
                    }
                }
                else if (File.Exists(actualTargetDir))
                {
                    File.Delete(actualTargetDir);
                }
                Logger.Log("既存の対象を正常に削除しました。");
            }
            catch (Exception ex)
            {
                Logger.Log($"既存対象の削除に失敗しました: {actualTargetDir}, {ex.Message}");
                throw new InvalidOperationException($"展開先 '{Path.GetFileName(actualTargetDir)}' が使用中か、削除権限がありません。", ex);
            }
        }

        try
        {
            // 出力ディレクトリを作成
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            cancellationToken.ThrowIfCancellationRequested();

            using (var reader = new ArchiveReader(archivePath))
            {
                Logger.Log($"展開処理開始: {archivePath}");

                if (progressCallback != null)
                {
                    var lastPercentage = -1;
                    var progress = new CancellableProgress<Report>(report =>
                    {
                        var percentage = report.TotalBytes > 0 ? (int)((report.Bytes * 100) / report.TotalBytes) : 0;
                        if (percentage == lastPercentage) return;
                        lastPercentage = percentage;
                        progressCallback(new ProgressInfo(percentage, "ファイルを展開中..."));
                    }, cancellationToken);

                    reader.Save(outputPath, progress);
                }
                else
                {
                    reader.Save(outputPath);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            Logger.Log($"アーカイブ展開完了: {archivePath} -> {outputPath}");
        }
        catch (OperationCanceledException)
        {
            // クリーンアップ対象の決定
            var cleanupPath = rootItemNameForCleanup != null ? Path.Combine(outputPath, rootItemNameForCleanup) : outputPath;
            
            Logger.Log($"展開処理がキャンセルされました。クリーンアップを試行: {cleanupPath}");
            
            // 保護されたディレクトリは絶対に削除しない
            if (PathValidator.IsProtectedDirectory(cleanupPath))
            {
                Logger.Log($"クリーンアップをスキップ: 保護されたディレクトリです: {cleanupPath}", LogLevel.Warning);
                throw;
            }

            try
            {
                if (Directory.Exists(cleanupPath))
                {
                    RemoveReadOnlyAttributes(cleanupPath);
                    Directory.Delete(cleanupPath, true);
                    Logger.Log($"キャンセルされた展開先を削除しました: {cleanupPath}");
                }
                else if (File.Exists(cleanupPath))
                {
                    File.Delete(cleanupPath);
                    Logger.Log($"キャンセルされた展開ファイルを削除しました: {cleanupPath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"キャンセル時のクリーンアップに失敗しました: {cleanupPath}, {ex.Message}", LogLevel.Warning);
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
    /// ファイルまたはディレクトリの読み取り専用属性を削除する
    /// </summary>
    /// <param name="path">対象のファイルまたはディレクトリパス</param>
    internal static void RemoveReadOnlyAttributes(string path)
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
                // GetFiles -> EnumerateFiles に変更してメモリ効率を向上
                // 大量ファイル処理時に配列を一括確保せず、遅延実行（イテレータ処理）で処理
                foreach (var filePath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
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
}
