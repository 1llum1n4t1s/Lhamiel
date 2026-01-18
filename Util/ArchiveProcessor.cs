using System.IO;
using System.Windows;

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
    /// <returns>処理が成功した場合はtrue、そうでなければfalse</returns>
    public static async Task<bool> ExtractArchiveAsync(string filePath, string outputDir, bool outputToSameDirectory, View.ProgressWindow progressWindow, CancellationToken cancellationToken = default, bool enablePartialExtraction = false, IProgress<ProgressInfo>? individualProgress = null)
    {
        Logger.Log($"ArchiveProcessor.ExtractArchiveAsync開始: filePath={filePath}, outputDir={outputDir}, outputToSameDirectory={outputToSameDirectory}");
        
        // ファイル存在確認などの軽量なチェックはUIスレッドで実施
        if (!File.Exists(filePath))
        {
            Logger.Log($"指定されたファイルが存在しません: {filePath}");
            MessageService.ShowError($"指定されたファイルが見つかりません。\n{filePath}");
            return false;
        }

        // I/Oを含む重い処理全体を Task.Run でバックグラウンドに移動
        return await Task.Run(async () => 
        {
            var outputPath = "";
            try
            {
                // UIスレッドからアクセスが必要なプログレス表示用のラッパー
                IProgress<ProgressInfo>? progress = individualProgress;
                if (progress == null && progressWindow != null)
                {
                    progress = new Progress<ProgressInfo>(info =>
                    {
                        // Task.Run 内で作る場合はコンテキストがないため、Dispatcher経由にする
                        progressWindow.Dispatcher.BeginInvoke(() => progressWindow.UpdateProgress(info.Percentage));
                    });
                }

                // ファイル拡張子の確認
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                var supportedExtensions = new[] { ".zip", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".tbz", ".lzma", ".tlz", ".xz", ".txz", ".rar", ".lzh", ".cab", ".arj", ".z", ".tz", ".exe" };
                
                if (!supportedExtensions.Contains(extension))
                {
                    Logger.Log($"サポートされていないファイル形式です: {extension}");
                    Application.Current.Dispatcher.Invoke(() => 
                        MessageService.ShowError($"サポートされていないファイル形式です。\n{extension}"));
                    return false;
                }

                // .exeファイルの場合は自己展開圧縮ファイルかどうかを確認
                if (extension == ".exe")
                {
                    if (!ArchiveFormatDetector.IsSelfExtractingArchive(filePath))
                    {
                        Logger.Log($"実行可能ファイルですが、自己展開圧縮ファイルではありません: {filePath}", LogLevel.Warning);
                        Application.Current.Dispatcher.Invoke(() => 
                            MessageService.ShowError($"実行可能ファイルですが、自己展開圧縮ファイルではありません。\n{filePath}"));
                        return false;
                    }
                }

                // --- ここから重いI/O処理 ---
                
                // 1. スマート解凍の判定 (バックグラウンドで実行)
                var baseDirectory = ArchiveExtractor.GetBaseOutputDirectory(filePath, outputDir, outputToSameDirectory);
                
                // ここで重い処理が走ってもUIは止まらない
                var isSingleRoot = ArchiveExtractor.HasSingleRootItem(filePath);
                
                string? rootItemName = null;

                if (isSingleRoot)
                {
                    outputPath = baseDirectory;
                    rootItemName = ArchiveExtractor.GetSingleRootItemName(filePath);
                    Logger.Log($"スマート解凍適用: 単一ルート要素のため直下に展開 -> {outputPath}");
                }
                else
                {
                    var fileName = Path.GetFileNameWithoutExtension(filePath);
                    outputPath = Path.Combine(baseDirectory, fileName);
                    Logger.Log($"通常解凍: アーカイブ名フォルダを作成 -> {outputPath}");
                }

                cancellationToken.ThrowIfCancellationRequested();

                // 2. 展開実行
                if (enablePartialExtraction)
                {
                    Logger.Log($"部分展開モードで展開処理を実行: {filePath}");
                    
                    var result = await PartialExtractionHandler.ExtractWithPartialFailureHandling(
                        filePath,
                        outputPath,
                        PartialExtractionHandler.ErrorHandlingOption.AskUser,
                        (percentage, _) => progressWindow?.Dispatcher.BeginInvoke(() => progressWindow.UpdateProgress(percentage)),
                        (failedFile) => ShowErrorRecoveryDialog(failedFile, progressWindow),
                        cancellationToken);
                    
                    if (result.SuccessCount > 0)
                    {
                        var summary = PartialExtractionHandler.GenerateResultSummary(result);
                        Logger.Log($"部分展開完了:\n{summary}");
                        
                        progressWindow?.Dispatcher.BeginInvoke(() => 
                            progressWindow.SetCompleted($"展開完了: {result.SuccessCount}/{result.TotalFiles}個のファイルが成功"));
                        return true;
                    }
                    return false;
                }
                else
                {
                    var extractor = new ArchiveExtractor();
                    await extractor.ExtractArchive(filePath, outputPath, 
                        p => progress?.Report(p), 
                        progressWindow, 
                        false, 
                        cancellationToken, 
                        rootItemName);
                    
                    return true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogException($"展開処理でエラーが発生: {filePath}", ex);
                
                // エラーダイアログはUIスレッドで表示
                Application.Current.Dispatcher.Invoke(() => 
                {
                    var errorInfo = ArchiveErrorHandler.AnalyzeError(ex, filePath, outputPath);
                    MessageService.ShowError($"{errorInfo.Message}\n\n詳細: {errorInfo.Details}", "展開エラー");
                });
                
                return false;
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
    /// <returns>すべての処理が成功した場合はtrue、そうでなければfalse</returns>
    public static async Task<bool> ExtractArchivesAsync(string[] filePaths, string outputDir, bool outputToSameDirectory, View.ProgressWindow progressWindow, CancellationToken cancellationToken = default)
    {
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
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 個別進捗を全体進捗にマッピングする Progress
                    var mappedProgress = new Progress<ProgressInfo>(info =>
                    {
                        // BeginInvoke を使用して、UIスレッドの負荷を軽減しデッドロックを回避
                        progressWindow?.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            var overallProgress = (int)((double)index / totalCount * 100 + (double)info.Percentage / totalCount);
                            progressWindow.UpdateProgress(overallProgress);
                        }));
                    });

                    var success = await ExtractArchiveAsync(filePath, outputDir, outputToSameDirectory, progressWindow, cancellationToken, enablePartialExtraction: false, individualProgress: mappedProgress);

                    int progressToReport;
                    lock (lockObject)
                    {
                        if (success) successCount++;
                        else failedFiles.Add(Path.GetFileName(filePath));

                        // 件数ベースの進捗を計算
                        progressToReport = (int)((double)(successCount + failedFiles.Count) / totalCount * 100);
                    }

                    // 修正点: lockの外で、かつ BeginInvoke を使用して更新
                    progressWindow?.Dispatcher.BeginInvoke(new Action(() =>
                        progressWindow.UpdateProgress(progressToReport)
                    ));
                }
                catch (OperationCanceledException)
                {
                    Logger.Log($"ファイル展開がキャンセルされました: {filePath}");
                    throw;
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

            // 完了処理
            progressWindow?.Dispatcher.BeginInvoke(new Action(() => progressWindow.Close()));
            return successCount > 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogException("複数ファイル展開処理でエラーが発生", ex);
            MessageService.ShowError($"展開中にエラーが発生しました。\n{ex.Message}");
            return false;
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
    /// <returns>処理が成功した場合はtrue、そうでなければfalse</returns>
    public static async Task<bool> CompressItemAsync(string sourcePath, string outputDir, bool outputToSameDirectory, string format, View.ProgressWindow? progressWindow, IProgress<ProgressInfo>? progressReporter = null, CancellationToken cancellationToken = default)
    {
        Logger.Log($"ArchiveProcessor.CompressItemAsync開始: sourcePath={sourcePath}, outputDir={outputDir}, outputToSameDirectory={outputToSameDirectory}, format={format}");

        // 対象の存在確認（軽量なチェックはUIスレッドで実施）
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
        {
            Logger.Log($"指定された対象が存在しません: {sourcePath}");
            MessageService.ShowError($"指定されたファイルまたはフォルダが見つかりません。\n{sourcePath}");
            return false;
        }

        // 圧縮形式の確認
        var supportedFormats = new[] { "zip", "7z", "tar", "gz", "bz2", "xz", "cab", "wim" };
        if (!supportedFormats.Contains(format.ToLowerInvariant()))
        {
            Logger.Log($"サポートされていない圧縮形式です: {format}");
            MessageService.ShowError($"サポートされていない圧縮形式です。\n{format}");
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
                    var dispatcher = progressWindow?.Dispatcher ?? Application.Current.Dispatcher;
                    
                    var canOverwrite = await dispatcher.InvokeAsync(() =>
                        FileOverwriteDialog.CanOverwriteFile(sourcePath, outputPath, progressWindow)).Task;

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
                    // 1. レガシーなProgressWindow更新 (単体実行時用)
                    if (progressReporter == null)
                    {
                        progressWindow?.UpdateProgress(info.Percentage);
                    }

                    // 2. 外部から渡された進捗レポーターへの報告 (並列実行時用)
                    progressReporter?.Report(info);
                });

                await ArchiveCompressor.CompressAsync(sourcePath, outputPath, format, progress, actualCancellationToken);

                Logger.Log($"圧縮処理が完了: {sourcePath} -> {outputPath}");

                if (progressReporter == null)
                {
                    // UIスレッド上で安全にクローズ
                    progressWindow?.Dispatcher.BeginInvoke(new Action(() => progressWindow.Close()));
                }

                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogException($"圧縮処理でエラーが発生: {sourcePath}", ex);
                
                // エラーダイアログはUIスレッドで表示
                Application.Current.Dispatcher.Invoke(() => 
                    MessageService.ShowError($"圧縮中にエラーが発生しました。\n{ex.Message}"));
                
                return false;
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
    /// <returns>すべての処理が成功した場合はtrue、そうでなければfalse</returns>
    public static async Task<bool> CompressItemsAsync(string[] sourcePaths, string outputDir, bool outputToSameDirectory, string format, View.ProgressWindow progressWindow, CancellationToken cancellationToken = default)
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
                await semaphore.WaitAsync(actualCancellationToken);
                try
                {
                    actualCancellationToken.ThrowIfCancellationRequested();

                    // 単一対象の場合は詳細な進捗を表示するためのReporterを作成
                    IProgress<ProgressInfo>? innerProgress = null;
                    if (totalCount == 1)
                    {
                        innerProgress = new Progress<ProgressInfo>(info =>
                        {
                            progressWindow?.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                progressWindow.UpdateProgress(info.Percentage);
                            }));
                        });
                    }
                    else
                    {
                        // 複数対象圧縮時は、個別進捗を全体進捗にマッピングして表示
                        innerProgress = new Progress<ProgressInfo>(info =>
                        {
                            progressWindow?.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                var overallProgress = (int)((double)index / totalCount * 100 + (double)info.Percentage / totalCount);
                                progressWindow.UpdateProgress(overallProgress);
                            }));
                        });
                    }

                    // 共通化された圧縮処理を実行
                    var success = await CompressItemAsync(sourcePath, outputDir, outputToSameDirectory, format, progressWindow, innerProgress, actualCancellationToken);

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
                        var completedProgress = (int)((double)(index + 1) / totalCount * 100);
                        progressWindow?.Dispatcher.BeginInvoke(new Action(() =>
                            progressWindow.UpdateProgress(completedProgress)
                        ));
                    }
                }
                catch (OperationCanceledException)
                {
                    Logger.Log($"圧縮がキャンセルされました: {sourcePath}");
                    throw;
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

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                Logger.Log("複数対象圧縮処理が全体でキャンセルされました");
                throw;
            }

            // 完了メッセージを表示
            if (successCount == totalCount)
            {
                Logger.Log($"複数対象圧縮完了: {successCount}/{totalCount}個の圧縮に成功");
                
                // UIスレッド上で安全にクローズ
                progressWindow?.Dispatcher.BeginInvoke(new Action(() => progressWindow.Close()));
                return true;
            }
            else
            {
                Logger.Log($"複数対象圧縮完了: {successCount}成功, {totalCount - successCount}失敗");
                
                progressWindow?.Dispatcher.BeginInvoke(new Action(() => progressWindow.Close()));
                return successCount > 0;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogException("複数対象圧縮処理でエラーが発生", ex);
            MessageService.ShowError($"圧縮中にエラーが発生しました。\n{ex.Message}");
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
            // エラー情報を生成
            var errorInfo = new ArchiveErrorInfo
            {
                ErrorType = failedFile.ErrorType,
                Message = failedFile.ErrorMessage,
                Details = $"ファイル: {failedFile.FilePath}\nエラー: {failedFile.ErrorMessage}",
                ProblematicFilePath = failedFile.FilePath,
                RecommendedAction = failedFile.IsRecoverable ? "再試行またはスキップを選択できます" : "このファイルは回復できません",
                IsRecoverable = failedFile.IsRecoverable
            };

            // UIスレッドでダイアログを表示
            if (parentWindow != null)
            {
                return parentWindow.Dispatcher.Invoke(() =>
                {
                    var dialog = new View.ErrorRecoveryDialog(errorInfo)
                    {
                        Owner = parentWindow
                    };
                    
                    if (dialog.ShowDialog() == true)
                    {
                        return dialog.SelectedOption;
                    }
                    else
                    {
                        return PartialExtractionHandler.ErrorHandlingOption.StopOnError;
                    }
                });
            }
            else
            {
                // 親ウィンドウがない場合は自動的にスキップ
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
