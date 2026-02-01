using System.IO;
using Avalonia;
using Avalonia.Threading;

namespace Lhamiel.Util;

/// <summary>
/// アーカイブ処理を共通化するクラス
/// </summary>
public static class ArchiveProcessor
{
    /// <summary>
    /// アーカイブファイルの展開処理を実行
    /// </summary>
    /// <param name="filePath">展開するファイルのパス</param>
    /// <param name="outputDir">出力ディレクトリ</param>
    /// <param name="outputToSameDirectory">同じディレクトリに出力するかどうか</param>
    /// <param name="progressWindow">進行状況ウィンドウ</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <param name="enablePartialExtraction">部分展開を有効にするかどうか</param>
    /// <param name="individualProgress">個別ファイルの進捗報告（並列処理時は空のProgressで無効化）</param>
    /// <param name="closeWindowOnCompletion">完了時に進捗ウィンドウを閉じるかどうか</param>
    public static async Task<(string? outputPath, ArchiveExtractor.ArchiveStructureInfo? structureInfo)> ExtractArchiveAsync(string filePath, string outputDir, bool outputToSameDirectory, View.ProgressWindow progressWindow, CancellationToken cancellationToken = default, bool enablePartialExtraction = false, IProgress<ProgressInfo>? individualProgress = null, bool closeWindowOnCompletion = true)
    {
        Logger.Log($"ArchiveProcessor.ExtractArchiveAsync開始: filePath={filePath}, outputDir={outputDir}, outputToSameDirectory={outputToSameDirectory}");

        // ファイル存在確認などの軽量なチェックはUIスレッドで実施
        if (!File.Exists(filePath))
        {
            Logger.Log($"指定されたファイルが存在しません: {filePath}");
            _ = MessageService.ShowError($"指定されたファイルが見つかりません。\n{filePath}");
            return (null!, null!);
        }

        // I/Oを含む重い処理全体を Task.Run でバックグラウンドに移動
        return await Task.Run(async () =>
        {
            string? outputPath = null;
            ArchiveExtractor.ArchiveStructureInfo? structureInfo = null;
            try
            {
                // UIスレッドからアクセスが必要なプログレス表示用のラッパー
                var progress = individualProgress;
                if (progress == null && progressWindow != null)
                {
                    progress = new Progress<ProgressInfo>(info =>
                    {
                        // Task.Run 内で作る場合はコンテキストがないため、Dispatcher経由にする
                        Dispatcher.UIThread.Post(() => progressWindow.UpdateProgress(info.Percentage));
                    });
                }

                // ファイル拡張子の確認
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                var supportedExtensions = new[] { ".zip", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".tbz", ".lzma", ".tlz", ".xz", ".txz", ".rar", ".lzh", ".cab", ".arj", ".z", ".tz" };

                if (!supportedExtensions.Contains(extension))
                {
                    Logger.Log($"サポートされていないファイル形式です: {extension}");
                    Dispatcher.UIThread.Post(() => _ = MessageService.ShowError($"サポートされていないファイル形式です。\n{extension}"));
                    return (null!, null!);
                }

                // --- ここから重いI/O処理 ---

                // 1. スマート解凍の判定 (バックグラウンドで実行)
                var baseDirectory = ArchiveExtractor.GetBaseOutputDirectory(filePath, outputDir, outputToSameDirectory);

                // アーカイブの構造を一度だけ解析
                structureInfo = ArchiveExtractor.GetArchiveStructureInfo(filePath);
                var rootItemName = structureInfo.DuplicateFolderName;
                var hasSingleRootItem = structureInfo.HasSingleRootItem;

                // 出力先を決定
                if (rootItemName != null)
                {
                    // 二重フォルダが検出された場合：baseDirectoryに直接展開してリフトアップ
                    outputPath = baseDirectory;
                    Logger.Log($"スマート解凍適用: 二重フォルダ構造を検出 '{rootItemName}' -> {outputPath}");
                }
                else if (hasSingleRootItem)
                {
                    // ルート要素が1つだけ：baseDirectoryに直接展開
                    outputPath = baseDirectory;
                    Logger.Log($"単一ルート展開: 内容をそのまま展開 -> {outputPath}");
                }
                else
                {
                    // ルート要素が複数：アーカイブ名フォルダを作成して展開
                    var fileName = Path.GetFileNameWithoutExtension(filePath);
                    outputPath = Path.Combine(baseDirectory, fileName);
                    Logger.Log($"複数ルート展開: アーカイブ名フォルダを作成 -> {outputPath}");
                }

                cancellationToken.ThrowIfCancellationRequested();

                // 2. 展開実行
                if (enablePartialExtraction)
                {
                    Logger.Log($"部分展開モードで展開処理を実行: {filePath}");

                    var result = await PartialExtractionHandler.ExtractWithPartialFailureHandling(
                        filePath,
                        outputPath!,
                        PartialExtractionHandler.ErrorHandlingOption.AskUser,
                        (percentage, _) => Dispatcher.UIThread.Post(() => progressWindow?.UpdateProgress(percentage)),
                        (failedFile) => ShowErrorRecoveryDialog(failedFile, progressWindow),
                        cancellationToken);

                    if (result.SuccessCount > 0)
                    {
                        var summary = PartialExtractionHandler.GenerateResultSummary(result);
                        Logger.Log($"部分展開完了:\n{summary}");

                        Dispatcher.UIThread.Post(() =>
                            progressWindow?.SetCompleted($"展開完了: {result.SuccessCount}/{result.TotalFiles}個のファイルが成功"));

                        if (closeWindowOnCompletion)
                        {
                            progressWindow?.CloseSafe();
                        }
                        return (outputPath, structureInfo);
                    }
                    return (null!, null!);
                }
                else
                {
                    // メソッド呼び出し: 静的メソッドとしてのExtractArchiveを呼び出し
                    await ArchiveExtractor.ExtractArchive(filePath, outputPath!,
                        p => progress?.Report(p),
                        progressWindow,
                        false,
                        cancellationToken,
                        rootItemName);

                    if (closeWindowOnCompletion)
                    {
                        progressWindow?.CloseSafe();
                    }
                    return (outputPath, structureInfo);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogException($"展開処理でエラーが発生: {filePath}", ex);
                var errorInfo = ArchiveErrorHandler.AnalyzeError(ex, filePath, outputPath ?? string.Empty);
                Dispatcher.UIThread.Post(() =>
                    _ = MessageService.ShowError($"{errorInfo.Message}\n\n詳細: {errorInfo.Details}", "展開エラー"));
                return (null!, null!);
            }
            finally
            {
                // 例外発生時にも確実にクリーンアップ
                if (closeWindowOnCompletion)
                {
                    progressWindow?.CloseSafe();
                }
            }
        }, cancellationToken);
    }

    /// <summary>
    /// 複数のアーカイブファイルの展開処理を実行（並列処理対応）
    /// </summary>
    /// <param name="filePaths">展開するファイルのパスの配列</param>
    /// <param name="outputDir">出力ディレクトリ</param>
    /// <param name="outputToSameDirectory">同じディレクトリに出力するかどうか</param>
    /// <param name="progressWindow">進行状況ウィンドウ</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <param name="closeWindowOnCompletion">完了時に進捗ウィンドウを閉じるかどうか</param>
    /// <returns>成功したアーカイブのソースパス、展開先パス、構造情報のリスト。すべて失敗した場合は空のリスト</returns>
    public static async Task<List<(string SourcePath, string OutputPath, ArchiveExtractor.ArchiveStructureInfo StructureInfo)>> ExtractArchivesAsync(string[] filePaths, string outputDir, bool outputToSameDirectory, View.ProgressWindow progressWindow, CancellationToken cancellationToken = default, bool closeWindowOnCompletion = true)
    {
        var results = new List<(string SourcePath, string OutputPath, ArchiveExtractor.ArchiveStructureInfo StructureInfo)>();
        try
        {
            var totalCount = filePaths.Length;
            var successCount = 0;
            var failedFiles = new List<string>();
            var lockObject = new object();

            // ディスクI/O負荷を考慮し、並列数をCPUコア数ではなく制限
            var maxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
            using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);

            Logger.Log($"複数ファイル展開開始: {totalCount}個のファイル、最大並列度={maxDegreeOfParallelism}");

            var tasks = filePaths.Select(async (filePath, index) =>
            {
                try
                {
                    await semaphore.WaitAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();

                    // 個別進捗を全体進捗にマッピングする Progress
                    var mappedProgress = new Progress<ProgressInfo>(info =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            var overallProgress = (int)((double)index / totalCount * 100 + (double)info.Percentage / totalCount);
                            progressWindow?.UpdateProgress(overallProgress);
                        });
                    });

                    var extractResult = await ExtractArchiveAsync(filePath, outputDir, outputToSameDirectory, progressWindow, cancellationToken, enablePartialExtraction: false, individualProgress: mappedProgress, closeWindowOnCompletion: false);
                    var finalOutputPath = extractResult.outputPath;
                    var structureInfo = extractResult.structureInfo;

                    // lock 内で状態のみ更新し、Dispatcher への通知は lock 外で実行
                    int progressToReport = 0;
                    bool shouldReportProgress = false;

                    lock (lockObject)
                    {
                        if (finalOutputPath != null && structureInfo != null)
                        {
                            successCount++;
                            results.Add((filePath, finalOutputPath, structureInfo));
                        }
                        else
                        {
                            failedFiles.Add(Path.GetFileName(filePath));
                        }

                        // 件数ベースの進捗を計算
                        progressToReport = (int)((double)(successCount + failedFiles.Count) / totalCount * 100);
                        shouldReportProgress = true;
                    }

                    // lock の外で、一度だけ BeginInvoke を実行してDispatcherキューの競合を削減
                    if (shouldReportProgress)
                    {
                        Dispatcher.UIThread.Post(() =>
                            progressWindow?.UpdateProgress(progressToReport));
                    }
                }
                catch (OperationCanceledException)
                {
                    Logger.Log($"ファイル展開がキャンセルされました: {filePath}");
                    // Ice と同様にタスク内では再スローせず、WhenAll 後に一度だけスローする
                }
                catch (Exception ex)
                {
                    Logger.LogException($"ファイル展開でエラーが発生: {filePath}", ex);
                    lock (lockObject)
                    {
                        failedFiles.Add(Path.GetFileName(filePath));
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);

            // Ice と同様: キャンセル時はここで一度だけスロー（複数タスクがそれぞれスローしない）
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            // 完了処理
            if (closeWindowOnCompletion)
            {
                progressWindow?.CloseSafe();
            }
            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogException("複数ファイル展開処理でエラーが発生", ex);
            _ = MessageService.ShowError($"展開中にエラーが発生しました。\n{ex.Message}");

            // 例外発生時にも確実にクリーンアップ
            if (closeWindowOnCompletion)
            {
                progressWindow?.CloseSafe();
            }

            return results;
        }
    }

    /// <summary>
    /// ファイルまたはフォルダの圧縮処理を実行
    /// </summary>
    /// <param name="sourcePath">圧縮する対象（ファイルまたはフォルダ）のパス</param>
    /// <param name="outputDir">出力ディレクトリ</param>
    /// <param name="outputToSameDirectory">同じディレクトリに出力するかどうか</param>
    /// <param name="format">圧縮形式</param>
    /// <param name="progressWindow">進行状況ウィンドウ（nullの場合はUI更新を行わない）</param>
    /// <param name="progressReporter">外部からの進捗報告用（並列処理時などに使用）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <param name="closeWindowOnCompletion">完了時に進捗ウィンドウを閉じるかどうか</param>
    /// <returns>処理が成功した場合はtrue、そうでなければfalse</returns>
    public static async Task<bool> CompressItemAsync(string sourcePath, string outputDir, bool outputToSameDirectory, string format, View.ProgressWindow? progressWindow, IProgress<ProgressInfo>? progressReporter = null, CancellationToken cancellationToken = default, bool closeWindowOnCompletion = true)
    {
        Logger.Log($"ArchiveProcessor.CompressItemAsync開始: sourcePath={sourcePath}, outputDir={outputDir}, outputToSameDirectory={outputToSameDirectory}, format={format}");

        // 対象の存在確認（軽量なチェックはUIスレッドで実施）
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
        {
            Logger.Log($"指定された対象が存在しません: {sourcePath}");
            _ = MessageService.ShowError($"指定されたファイルまたはフォルダが見つかりません。\n{sourcePath}");
            return false;
        }

        // 圧縮形式の確認
        var supportedFormats = new[] { "zip", "7z", "tar", "gz", "bz2", "xz", "cab", "wim" };
        if (!supportedFormats.Contains(format.ToLowerInvariant()))
        {
            Logger.Log($"サポートされていない圧縮形式です: {format}");
            _ = MessageService.ShowError($"サポートされていない圧縮形式です。\n{format}");
            return false;
        }

        // ProgressWindow からキャンセルトークンを取得（UIスレッドで事前に取得）
        var actualCancellationToken = progressWindow != null ? progressWindow.GetCancellationToken() : cancellationToken;

        // 重い処理全体を Task.Run でバックグラウンドへ移動
        return await Task.Run(async () =>
        {
            try
            {
                Logger.Log($"圧縮処理を開始: {sourcePath}");

                // 出力ファイル名の取得
                var outputPath = ArchiveCompressor.GetCompressedFileName(sourcePath, format, outputDir, outputToSameDirectory);

                // 出力先が既に存在する場合は上書き確認
                var targetExists = File.Exists(outputPath) || Directory.Exists(outputPath);
                if (targetExists)
                {
                    Logger.Log($"出力先が既に存在します: {outputPath}");

                    // UIスレッドで上書き確認を実行
                    bool canOverwrite = await Dispatcher.UIThread.InvokeAsync(() =>
                        FileOverwriteDialog.CanOverwriteFile(sourcePath, outputPath, progressWindow));

                    Logger.Log($"上書き確認ダイアログ結果: canOverwrite={canOverwrite}");

                    if (!canOverwrite)
                    {
                        Logger.Log("ユーザーが圧縮処理をキャンセルしました");
                        return false;
                    }

                    // 上書きが許可された場合は既存の対象を削除
                    try
                    {
                        if (Directory.Exists(outputPath))
                        {
                            Directory.Delete(outputPath, true);
                        }
                        else
                        {
                            File.Delete(outputPath);
                        }
                        Logger.Log($"既存の対象を削除しました: {outputPath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"既存対象の削除に失敗しました: {outputPath}, {ex.Message}");
                        throw new InvalidOperationException($"出力先 '{Path.GetFileName(outputPath)}' が使用中か、アクセス権限がありません。", ex);
                    }
                }

                // 圧縮処理を実行
                Logger.Log($"ArchiveCompressor.CompressAsyncを呼び出し: sourcePath={sourcePath}, outputPath={outputPath}, format={format}");

                var progress = new Progress<ProgressInfo>(info =>
                {
                    if (progressReporter == null)
                    {
                        Dispatcher.UIThread.Post(() => progressWindow?.UpdateProgress(info.Percentage));
                    }

                    progressReporter?.Report(info);
                });

                await ArchiveCompressor.CompressAsync(sourcePath, outputPath, format, progress, actualCancellationToken);

                Logger.Log($"圧縮処理が完了: {sourcePath} -> {outputPath}");

                if (progressReporter == null && closeWindowOnCompletion)
                {
                    // UIスレッド上で安全にクローズ
                    progressWindow?.CloseSafe();
                }

                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogException($"圧縮処理でエラーが発生: {sourcePath}", ex);
                Dispatcher.UIThread.Post(() =>
                    _ = MessageService.ShowError($"圧縮中にエラーが発生しました。\n{ex.Message}"));
                return false;
            }
            finally
            {
                // 例外発生時にも確実にクリーンアップ
                if (progressReporter == null && closeWindowOnCompletion)
                {
                    progressWindow?.CloseSafe();
                }
            }
        }, actualCancellationToken);
    }

    /// <summary>
    /// 複数のファイルまたはフォルダの圧縮処理を実行（並列処理対応）
    /// </summary>
    /// <param name="sourcePaths">圧縮する対象（ファイルまたはフォルダ）のパスの配列</param>
    /// <param name="outputDir">出力ディレクトリ</param>
    /// <param name="outputToSameDirectory">同じディレクトリに出力するかどうか</param>
    /// <param name="format">圧縮形式</param>
    /// <param name="progressWindow">進行状況ウィンドウ</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <param name="closeWindowOnCompletion">完了時に進捗ウィンドウを閉じるかどうか</param>
    /// <returns>すべての処理が成功した場合はtrue、そうでなければfalse</returns>
    public static async Task<bool> CompressItemsAsync(string[] sourcePaths, string outputDir, bool outputToSameDirectory, string format, View.ProgressWindow progressWindow, CancellationToken cancellationToken = default, bool closeWindowOnCompletion = true)
    {
        try
        {
            var totalCount = sourcePaths.Length;
            var successCount = 0;
            var failedPaths = new List<string>();
            var lockObject = new object();

            // ProgressWindow からキャンセルトークンを取得（nullの場合は渡されたトークンを使用）
            var actualCancellationToken = progressWindow != null ? progressWindow.GetCancellationToken() : cancellationToken;

            var maxDegreeOfParallelism = 2;
            using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);

            Logger.Log($"複数対象圧縮開始: {totalCount}個の対象、並列制限={maxDegreeOfParallelism}、形式={format}");

            var tasks = sourcePaths.Select(async (sourcePath, index) =>
            {
                try
                {
                    await semaphore.WaitAsync(actualCancellationToken);
                    actualCancellationToken.ThrowIfCancellationRequested();

                    // 単一対象の場合は詳細な進捗を表示するためのReporterを作成
                    IProgress<ProgressInfo>? innerProgress = null;
                    if (totalCount == 1)
                    {
                        innerProgress = new Progress<ProgressInfo>(info =>
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                progressWindow?.UpdateProgress(info.Percentage);
                            });
                        });
                    }
                    else
                    {
                        // 複数対象圧縮時は、個別進捗を全体進捗にマッピングして表示
                        innerProgress = new Progress<ProgressInfo>(info =>
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                var overallProgress = (int)((double)index / totalCount * 100 + (double)info.Percentage / totalCount);
                                progressWindow?.UpdateProgress(overallProgress);
                            });
                        });
                    }

                    // 共通化された圧縮処理を実行
                    var success = await CompressItemAsync(sourcePath, outputDir, outputToSameDirectory, format, progressWindow, innerProgress, actualCancellationToken, closeWindowOnCompletion: false);

                    // lock 内で状態のみ更新し、Dispatcher への通知は lock 外で実行
                    int completedProgress = 0;
                    bool shouldReportProgress = false;

                    lock (lockObject)
                    {
                        if (success)
                        {
                            successCount++;
                        }
                        else
                        {
                            failedPaths.Add(Path.GetFileName(sourcePath));
                        }

                        // 各対象完了時に確実に進捗を更新
                        completedProgress = (int)((double)(index + 1) / totalCount * 100);
                        shouldReportProgress = true;
                    }

                    // lock の外で、一度だけ BeginInvoke を実行してDispatcherキューの競合を削減
                    if (shouldReportProgress)
                    {
                        Dispatcher.UIThread.Post(() =>
                            progressWindow?.UpdateProgress(completedProgress));
                    }
                }
                catch (OperationCanceledException)
                {
                    Logger.Log($"圧縮がキャンセルされました: {sourcePath}");
                    // Ice と同様にタスク内では再スローせず、WhenAll 後に一度だけスローする
                }
                catch (Exception ex)
                {
                    Logger.LogException($"圧縮でエラーが発生: {sourcePath}", ex);
                    lock (lockObject)
                    {
                        failedPaths.Add(Path.GetFileName(sourcePath));
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);

            // Ice と同様: キャンセル時はここで一度だけスロー（複数タスクがそれぞれスローしない）
            if (actualCancellationToken.IsCancellationRequested)
            {
                Logger.Log("複数対象圧縮処理が全体でキャンセルされました");
                throw new OperationCanceledException(actualCancellationToken);
            }

            // 完了メッセージを表示
            if (successCount == totalCount)
            {
                Logger.Log($"複数対象圧縮完了: {successCount}/{totalCount}個の圧縮に成功");

                // UIスレッド上で安全にクローズ
                if (closeWindowOnCompletion)
                {
                    progressWindow?.CloseSafe();
                }
                return true;
            }
            else
            {
                Logger.Log($"複数対象圧縮完了: {successCount}成功, {totalCount - successCount}失敗");

                if (closeWindowOnCompletion)
                {
                    progressWindow?.CloseSafe();
                }
                return successCount > 0;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogException("複数対象圧縮処理でエラーが発生", ex);
            _ = MessageService.ShowError($"圧縮中にエラーが発生しました。\n{ex.Message}");

            // 例外発生時にも確実にクリーンアップ
            if (closeWindowOnCompletion)
            {
                progressWindow?.CloseSafe();
            }

            return false;
        }
    }

    /// <summary>
    /// エラー回復ダイアログを表示
    /// </summary>
    /// <param name="failedFile">失敗したファイル情報</param>
    /// <param name="parentWindow">親ウィンドウ</param>
    /// <returns>選択されたエラー処理オプション</returns>
    private static PartialExtractionHandler.ErrorHandlingOption ShowErrorRecoveryDialog(
        PartialExtractionHandler.FailedFileInfo failedFile,
        View.ProgressWindow? parentWindow)
    {
        try
        {
            var errorInfo = new ArchiveErrorInfo
            {
                ErrorType = failedFile.ErrorType,
                Message = failedFile.ErrorMessage,
                Details = $"ファイル: {failedFile.FilePath}\nエラー: {failedFile.ErrorMessage}",
                ProblematicFilePath = failedFile.FilePath,
                RecommendedAction = failedFile.IsRecoverable ? "再試行またはスキップを選択できます" : "このファイルは回復できません",
                IsRecoverable = failedFile.IsRecoverable
            };

            if (parentWindow != null)
            {
                var result = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var dialog = new View.ErrorRecoveryDialog(errorInfo);
                    var option = await dialog.ShowDialog<PartialExtractionHandler.ErrorHandlingOption?>(parentWindow);
                    return option ?? PartialExtractionHandler.ErrorHandlingOption.StopOnError;
                }).GetAwaiter().GetResult();
                return result;
            }
            else
            {
                return PartialExtractionHandler.ErrorHandlingOption.SkipOnError;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"エラー回復ダイアログの表示でエラーが発生: {ex.Message}");
            return PartialExtractionHandler.ErrorHandlingOption.SkipOnError;
        }
    }
}
