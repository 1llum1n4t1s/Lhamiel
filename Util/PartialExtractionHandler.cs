using System.IO;
using Cube.FileSystem.SevenZip;

namespace Lhamiel.Util;

/// <summary>
/// 部分的な展開失敗時の処理を管理するクラス
/// </summary>
public class PartialExtractionHandler
{
    /// <summary>
    /// 展開結果の詳細情報
    /// </summary>
    public class ExtractionResult
    {
        /// <summary>
        /// 展開が成功したかどうか
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 成功したファイル数
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// 失敗したファイル数
        /// </summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// スキップされたファイル数
               /// </summary>
        public int SkippedCount { get; set; }

        /// <summary>
        /// 成功したファイル一覧
        /// </summary>
        public List<string> SuccessFiles { get; set; } = [];

        /// <summary>
        /// 失敗したファイル一覧
        /// </summary>
        public List<FailedFileInfo> FailedFiles { get; set; } = [];

        /// <summary>
        /// スキップされたファイル一覧
        /// </summary>
        public List<string> SkippedFiles { get; set; } = [];

        /// <summary>
        /// 総ファイル数
        /// </summary>
        public int TotalFiles { get; set; }

        /// <summary>
        /// 成功率（パーセント）
        /// </summary>
        public double SuccessRate => TotalFiles > 0 ? (double)SuccessCount / TotalFiles * 100 : 0;
    }

    /// <summary>
    /// 失敗したファイルの情報
    /// </summary>
    public class FailedFileInfo
    {
        /// <summary>
        /// ファイルパス
        /// </summary>
        public string FilePath { get; set; } = "";

        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public string ErrorMessage { get; set; } = "";

        /// <summary>
        /// エラーの種類
        /// </summary>
        public ArchiveErrorType ErrorType { get; set; }

        /// <summary>
        /// 回復可能かどうか
        /// </summary>
        public bool IsRecoverable { get; set; }
    }

    /// <summary>
    /// エラー処理オプション
    /// </summary>
    public enum ErrorHandlingOption
    {
        /// <summary>
        /// エラーで停止
        /// </summary>
        StopOnError,

        /// <summary>
        /// エラーファイルをスキップして続行
        /// </summary>
        SkipOnError,

        /// <summary>
        /// ユーザーに選択を求める
        /// </summary>
        AskUser,

        /// <summary>
        /// 自動的にリトライ
        /// </summary>
        AutoRetry
    }

    /// <summary>
    /// 部分的な展開を実行
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="outputPath">出力先ディレクトリ</param>
    /// <param name="errorHandling">エラー処理オプション</param>
    /// <param name="progressCallback">進捗コールバック</param>
    /// <param name="userChoiceCallback">ユーザー選択コールバック（AskUserの場合）</param>
    /// <param name="cancellationToken"></param>
    /// <returns>展開結果</returns>
    public static async Task<ExtractionResult> ExtractWithPartialFailureHandling(
        string archivePath,
        string outputPath,
        ErrorHandlingOption errorHandling = ErrorHandlingOption.AskUser,
        Action<int, string>? progressCallback = null,
        Func<FailedFileInfo, ErrorHandlingOption>? userChoiceCallback = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ExtractionResult();
        
        Logger.Log($"部分展開処理開始: {archivePath} -> {outputPath}, エラー処理: {errorHandling}");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var reader = new ArchiveReader(archivePath);
            var items = reader.Items.ToList();
            result.TotalFiles = items.Count;

            // 出力ディレクトリを作成
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var (tempPath, extractionException) = ExtractArchiveToTemporaryPath(reader, cancellationToken);

            try
            {
                // 事前展開済みの内容から個別ファイルをコピー
                for (int i = 0; i < items.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var item = items[i];
                    var fullName = item.FullName ?? string.Empty;
                    var progress = items.Count == 0 ? 100 : (int)((double)(i + 1) / items.Count * 100);
                    progressCallback?.Invoke(progress, $"展開中: {fullName}");

                    try
                    {
                        await Task.Run(() => FileOperations.CopyExtractedItem(tempPath, outputPath, fullName, item.IsDirectory), cancellationToken);
                        result.SuccessFiles.Add(fullName);
                        result.SuccessCount++;

                        Logger.Log($"ファイル展開成功: {fullName}", LogLevel.Debug);
                    }
                    catch (Exception ex)
                    {
                        var error = ex is FileNotFoundException && extractionException != null ? extractionException : ex;
                        var analyzed = ArchiveErrorHandler.AnalyzeError(error, archivePath, outputPath);
                        var failedFile = new FailedFileInfo
                        {
                            FilePath = fullName,
                            ErrorMessage = error.Message,
                            ErrorType = analyzed.ErrorType,
                            IsRecoverable = analyzed.IsRecoverable
                        };

                        result.FailedFiles.Add(failedFile);
                        result.FailureCount++;

                        Logger.Log($"ファイル展開失敗: {fullName}, エラー: {error.Message}", LogLevel.Error);

                        // エラー処理オプションに基づいて処理を決定
                        var handlingOption = DetermineErrorHandling(failedFile, errorHandling, userChoiceCallback);

                        switch (handlingOption)
                        {
                            case ErrorHandlingOption.StopOnError:
                                Logger.Log("エラーで停止", LogLevel.Error);
                                result.IsSuccess = false;
                                return result;

                            case ErrorHandlingOption.SkipOnError:
                                result.SkippedFiles.Add(fullName);
                                result.SkippedCount++;
                                Logger.Log($"ファイルをスキップ: {fullName}", LogLevel.Warning);
                                break;

                            case ErrorHandlingOption.AutoRetry:
                                // リトライを試行
                                if (await RetryExtraction(tempPath, outputPath, fullName, item.IsDirectory, 3))
                                {
                                    result.SuccessFiles.Add(fullName);
                                    result.SuccessCount++;
                                    result.FailedFiles.RemoveAt(result.FailedFiles.Count - 1);
                                    result.FailureCount--;
                                    Logger.Log($"リトライ成功: {fullName}");
                                }
                                else
                                {
                                    result.SkippedFiles.Add(fullName);
                                    result.SkippedCount++;
                                    Logger.Log($"リトライ失敗、スキップ: {fullName}", LogLevel.Warning);
                                }
                                break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Log($"部分展開処理がキャンセルされました。出力先をクリーンアップします: {outputPath}");
                try
                {
                    // 保護されたディレクトリ（デスクトップなど）は絶対に削除しない
                    if (PathValidator.IsProtectedDirectory(outputPath))
                    {
                        Logger.Log($"クリーンアップをスキップ: 保護されたディレクトリです: {outputPath}", LogLevel.Warning);
                        throw;
                    }

                    if (Directory.Exists(outputPath))
                    {
                        // 読み取り専用属性などを解除してから削除
                        ArchiveExtractor.RemoveReadOnlyAttributes(outputPath);
                        Directory.Delete(outputPath, true);
                        Logger.Log($"キャンセルされた展開先を削除しました: {outputPath}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"キャンセル時のクリーンアップに失敗しました: {outputPath}, {ex.Message}", LogLevel.Warning);
                }
                throw;
            }
            finally
            {
                FileOperations.CleanupTemporaryPath(tempPath, message => Logger.Log(message, LogLevel.Warning));
            }

            // 最終結果を判定
            result.IsSuccess = result.SuccessCount > 0;
            
            Logger.Log($"部分展開完了: 成功={result.SuccessCount}, 失敗={result.FailureCount}, スキップ={result.SkippedCount}");
        }
        catch (Exception ex)
        {
            Logger.LogException("部分展開処理でエラーが発生", ex);
            result.IsSuccess = false;
        }

        return result;
    }

    /// <summary>
    /// アーカイブを一時ディレクトリに展開する
    /// </summary>
    private static (string TempPath, Exception? ExtractionException) ExtractArchiveToTemporaryPath(ArchiveReader reader, CancellationToken cancellationToken = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"lhamiel-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);
        Exception? extractionException = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // 進行状況の監視は不要だが、キャンセルは監視したい
            var progress = new CancellableProgress<Report>(_ => { }, cancellationToken);
            reader.Save(tempPath, progress);
        }
        catch (Exception ex)
        {
            extractionException = ex;
            Logger.LogException("一時展開中にエラーが発生しました", ex);
        }

        return (tempPath, extractionException);
    }

    /// <summary>
    /// エラー処理方法を決定
    /// </summary>
    private static ErrorHandlingOption DetermineErrorHandling(
        FailedFileInfo failedFile,
        ErrorHandlingOption defaultOption,
        Func<FailedFileInfo, ErrorHandlingOption>? userChoiceCallback)
    {
        if (defaultOption == ErrorHandlingOption.AskUser && userChoiceCallback != null)
        {
            return userChoiceCallback(failedFile);
        }
        
        return defaultOption;
    }

    /// <summary>
    /// 展開のリトライを実行
    /// </summary>
    private static async Task<bool> RetryExtraction(string tempPath, string outputPath, string fullName, bool isDirectory, int maxRetries)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                Logger.Log($"リトライ試行 {attempt}/{maxRetries}: {fullName}");

                // 少し待機してからリトライ
                await Task.Delay(1000 * attempt);

                await Task.Run(() => FileOperations.CopyExtractedItem(tempPath, outputPath, fullName, isDirectory));
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"リトライ {attempt} 失敗: {ex.Message}");
                if (attempt == maxRetries)
                {
                    return false;
                }
            }
        }
        
        return false;
    }

    /// <summary>
    /// 展開結果のサマリーを生成
    /// </summary>
    public static string GenerateResultSummary(ExtractionResult result)
    {
        var summary = new System.Text.StringBuilder();
        
        summary.AppendLine("=== 展開結果サマリー ===");
        summary.AppendLine($"総ファイル数: {result.TotalFiles}");
        summary.AppendLine($"成功: {result.SuccessCount} ({result.SuccessRate:F1}%)");
        summary.AppendLine($"失敗: {result.FailureCount}");
        summary.AppendLine($"スキップ: {result.SkippedCount}");
        
        if (result.FailedFiles.Any())
        {
            summary.AppendLine("\n=== 失敗したファイル ===");
            foreach (var failedFile in result.FailedFiles)
            {
                summary.AppendLine($"- {failedFile.FilePath}: {failedFile.ErrorMessage}");
            }
        }
        
        if (result.SkippedFiles.Any())
        {
            summary.AppendLine("\n=== スキップされたファイル ===");
            foreach (var skippedFile in result.SkippedFiles)
            {
                summary.AppendLine($"- {skippedFile}");
            }
        }
        
        return summary.ToString();
    }

    /// <summary>
    /// 回復可能なファイルのみを再展開
    /// </summary>
    public static async Task<ExtractionResult> RetryRecoverableFiles(
        string archivePath,
        string outputPath,
        ExtractionResult previousResult,
        Action<int, string>? progressCallback = null)
    {
        var retryResult = new ExtractionResult
        {
            TotalFiles = previousResult.FailedFiles.Count(f => f.IsRecoverable)
        };

        if (retryResult.TotalFiles == 0)
        {
            Logger.Log("回復可能なファイルがありません");
            return retryResult;
        }

        Logger.Log($"回復可能なファイルの再展開開始: {retryResult.TotalFiles}個");

        try
        {
            using var reader = new ArchiveReader(archivePath);
            var recoverableFiles = previousResult.FailedFiles.Where(f => f.IsRecoverable).ToList();
            var (tempPath, extractionException) = ExtractArchiveToTemporaryPath(reader);

            try
            {
                for (int i = 0; i < recoverableFiles.Count; i++)
                {
                    var failedFile = recoverableFiles[i];
                    var progress = recoverableFiles.Count == 0 ? 100 : (int)((double)(i + 1) / recoverableFiles.Count * 100);
                    progressCallback?.Invoke(progress, $"再展開中: {failedFile.FilePath}");

                    try
                    {
                        var item = reader.Items.FirstOrDefault(x => x.FullName == failedFile.FilePath);
                        if (item != null)
                        {
                            await Task.Run(() => FileOperations.CopyExtractedItem(tempPath, outputPath, item.FullName, item.IsDirectory));
                            retryResult.SuccessFiles.Add(failedFile.FilePath);
                            retryResult.SuccessCount++;
                            Logger.Log($"再展開成功: {failedFile.FilePath}");
                        }
                        else
                        {
                            var errorMessage = "アーカイブ内に対象ファイルが見つかりません。";
                            retryResult.FailedFiles.Add(new FailedFileInfo
                            {
                                FilePath = failedFile.FilePath,
                                ErrorMessage = errorMessage,
                                ErrorType = failedFile.ErrorType,
                                IsRecoverable = false
                            });
                            retryResult.FailureCount++;
                            Logger.Log($"再展開失敗: {failedFile.FilePath}, エラー: {errorMessage}");
                        }
                    }
                    catch (Exception ex)
                    {
                        var error = ex is FileNotFoundException && extractionException != null ? extractionException : ex;
                        retryResult.FailedFiles.Add(new FailedFileInfo
                        {
                            FilePath = failedFile.FilePath,
                            ErrorMessage = error.Message,
                            ErrorType = failedFile.ErrorType,
                            IsRecoverable = false
                        });
                        retryResult.FailureCount++;
                        Logger.Log($"再展開失敗: {failedFile.FilePath}, エラー: {error.Message}");
                    }
                }
            }
            finally
            {
                FileOperations.CleanupTemporaryPath(tempPath, message => Logger.Log(message, LogLevel.Warning));
            }

            retryResult.IsSuccess = retryResult.SuccessCount > 0;
        }
        catch (Exception ex)
        {
            Logger.LogException("回復可能ファイルの再展開でエラーが発生", ex);
            retryResult.IsSuccess = false;
        }

        return retryResult;
    }

    /// <summary>
    /// キャンセル可能な進捗報告クラス
    /// </summary>
    private class CancellableProgress<T>(Action<T> handler, CancellationToken token) : IProgress<T>
    {
        public void Report(T value)
        {
            token.ThrowIfCancellationRequested();
            handler(value);
        }
    }
}
