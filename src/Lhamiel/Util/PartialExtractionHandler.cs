using Cube.FileSystem.SevenZip;
using System.Text;
namespace Lhamiel.Util;

/// <summary>
/// 部分的な展開失敗時の処理を管理するクラス。
/// 個別エントリの展開ロジックは <see cref="ArchiveExtractor.TryExtractEntryAsync"/> に委譲し、
/// 本クラスはエラー処理フローの調整と結果集約を担う薄アダプター。
/// </summary>
[Obsolete("主フローの ArchiveExtractor.TryExtractEntryAsync + skipRelativePaths に統合予定")]
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
        Func<FailedFileInfo, Task<ErrorHandlingOption>>? userChoiceCallback = null,
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
                for (var i = 0; i < items.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var item = items[i];
                    var fullName = item.FullName ?? string.Empty;
                    var progress = items.Count == 0 ? 100 : (int)((double)(i + 1) / items.Count * 100);
                    progressCallback?.Invoke(progress, App.Text("Extraction.Progress", fullName));

                    // ArchiveExtractor.TryExtractEntryAsync に委譲（リトライ 1 回で初回試行）
                    var copied = await ArchiveExtractor.TryExtractEntryAsync(
                        tempPath, outputPath, fullName, item.IsDirectory,
                        maxRetries: 1, cancellationToken: cancellationToken);

                    if (copied)
                    {
                        result.SuccessFiles.Add(fullName);
                        result.SuccessCount++;
                        Logger.Log($"ファイル展開成功: {fullName}", LogLevel.Debug);
                        continue;
                    }

                    // 初回失敗 — エラー情報を構築してユーザー判断を仰ぐ
                    var error = extractionException ?? new IOException(App.Text("Error.ExtractedFileNotFound"));
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

                    var handlingOption = await DetermineErrorHandlingAsync(failedFile, errorHandling, userChoiceCallback);

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
                            if (await ArchiveExtractor.TryExtractEntryAsync(
                                tempPath, outputPath, fullName, item.IsDirectory,
                                maxRetries: 3, cancellationToken: cancellationToken))
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
            catch (OperationCanceledException)
            {
                Logger.Log($"部分展開処理がキャンセルされました: {outputPath}（一時フォルダのみクリーンアップ、出力先は保持）");
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
            using var progress = new CancellableProgress<Report>(_ => { }, cancellationToken);
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
    private static async Task<ErrorHandlingOption> DetermineErrorHandlingAsync(
        FailedFileInfo failedFile,
        ErrorHandlingOption defaultOption,
        Func<FailedFileInfo, Task<ErrorHandlingOption>>? userChoiceCallback)
    {
        if (defaultOption == ErrorHandlingOption.AskUser && userChoiceCallback != null)
        {
            return await userChoiceCallback(failedFile);
        }

        return defaultOption;
    }

    [Obsolete("ArchiveExtractor.TryExtractEntryAsync に統合済み")]
    private static Task<bool> RetryExtraction(string tempPath, string outputPath, string fullName, bool isDirectory, int maxRetries)
        => ArchiveExtractor.TryExtractEntryAsync(tempPath, outputPath, fullName, isDirectory, maxRetries: maxRetries);

    /// <summary>
    /// 展開結果のサマリーを生成
    /// </summary>
    public static string GenerateResultSummary(ExtractionResult result)
    {
        var summary = new StringBuilder();

        summary.AppendLine("=== 展開結果サマリー ===");
        summary.AppendLine($"総ファイル数: {result.TotalFiles}");
        summary.AppendLine($"成功: {result.SuccessCount} ({result.SuccessRate:F1}%)");
        summary.AppendLine($"失敗: {result.FailureCount}");
        summary.AppendLine($"スキップ: {result.SkippedCount}");

        if (result.FailedFiles.Count > 0)
        {
            summary.AppendLine("\n=== 失敗したファイル ===");
            foreach (var failedFile in result.FailedFiles)
            {
                summary.AppendLine($"- {failedFile.FilePath}: {failedFile.ErrorMessage}");
            }
        }

        if (result.SkippedFiles.Count > 0)
        {
            summary.AppendLine("\n=== スキップされたファイル ===");
            foreach (var skippedFile in result.SkippedFiles)
            {
                summary.AppendLine($"- {skippedFile}");
            }
        }

        return summary.ToString();
    }

}
