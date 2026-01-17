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
        Logger.Log($"ArchiveProcessor.ExtractArchiveAsync開始: filePath={filePath}, outputDir={outputDir}, outputToSameDirectory={outputToSameDirectory}, progressWindow={progressWindow?.GetType().Name ?? "null"}");
        
        // 出力先ディレクトリの取得（エラーハンドリングで使用するため、tryブロックの外側で宣言）
        var outputPath = "";
        
        try
        {
            // ファイルの存在確認
            if (!File.Exists(filePath))
            {
                Logger.Log($"指定されたファイルが存在しません: {filePath}");
                MessageBox.Show($"指定されたファイルが見つかりません。\n{filePath}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // ファイル拡張子の確認
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var supportedExtensions = new[] { ".zip", ".7z", ".tar", ".gz", ".bz2", ".xz", ".rar", ".lzh", ".cab", ".arj", ".z", ".exe" };
            
            if (!supportedExtensions.Contains(extension))
            {
                Logger.Log($"サポートされていないファイル形式です: {extension}");
                MessageBox.Show($"サポートされていないファイル形式です。\n{extension}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // .exeファイルの場合は自己展開圧縮ファイルかどうかを確認
            if (extension == ".exe")
            {
                Logger.Log($"自己展開圧縮ファイルの可能性を確認: {filePath}", LogLevel.Debug);
                if (!ArchiveFormatDetector.IsSelfExtractingArchive(filePath))
                {
                    Logger.Log($"実行可能ファイルですが、自己展開圧縮ファイルではありません: {filePath}", LogLevel.Warning);
                    MessageBox.Show($"実行可能ファイルですが、自己展開圧縮ファイルではありません。\n{filePath}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                Logger.Log($"自己展開圧縮ファイルを確認: {filePath}", LogLevel.Info);
            }

            Logger.Log($"展開処理を開始: {filePath}");

            // 展開先パスの決定ロジック（二重フォルダ防止）
            // 基準となる出力ディレクトリ（ユーザー指定の場所 または アーカイブと同じ場所）
            var baseDirectory = ArchiveExtractor.GetBaseOutputDirectory(filePath, outputDir, outputToSameDirectory);

            // アーカイブの中身をチェックして、展開先を決定
            var isSingleRoot = ArchiveExtractor.HasSingleRootItem(filePath);
            
            if (isSingleRoot)
            {
                // 単一要素の場合：アーカイブ名フォルダを作らず、基準ディレクトリに直接展開
                // 結果として、中身の単一要素名でフォルダ/ファイルが作成される
                outputPath = baseDirectory;
                Logger.Log($"スマート解凍適用: 単一ルート要素のため直下に展開 -> {outputPath}");
            }
            else
            {
                // 複数要素の場合：アーカイブ名フォルダを作成して、その中に展開
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                outputPath = Path.Combine(baseDirectory, fileName);
                Logger.Log($"通常解凍: アーカイブ名フォルダを作成 -> {outputPath}");
            }

            // ファイル名を設定
            progressWindow?.SetFileName($"{Path.GetFileName(filePath)} (展開中)");

            cancellationToken.ThrowIfCancellationRequested();

            // 展開処理を実行
            // 個別進捗が指定されていない場合は、ProgressWindow用の進捗を作成
            // 並列処理時はindividualProgressに空のProgressを渡して、個別進捗のUI更新を無効化する
            IProgress<ProgressInfo>? progress = individualProgress;
            if (progress == null && progressWindow != null)
            {
                progress = new Progress<ProgressInfo>(info =>
                {
                    progressWindow.UpdateProgress(info.Percentage, info.Status);
                    // 展開中のファイル名を表示
                    if (!string.IsNullOrEmpty(info.CurrentFileName))
                    {
                        Logger.Log($"展開中のファイル: {info.CurrentFileName}");
                        progressWindow.SetFileName(Path.GetFileName(info.CurrentFileName));
                    }
                });
            }

            if (enablePartialExtraction)
            {
                Logger.Log($"部分展開モードで展開処理を実行: {filePath}");
                
                // 部分展開処理を実行
                cancellationToken.ThrowIfCancellationRequested();

                var result = await PartialExtractionHandler.ExtractWithPartialFailureHandling(
                    filePath,
                    outputPath,
                    PartialExtractionHandler.ErrorHandlingOption.AskUser,
                    (percentage, message) => progressWindow?.UpdateProgress(percentage, message),
                    (failedFile) => ShowErrorRecoveryDialog(failedFile, progressWindow));
                
                // 結果を表示
                if (result.SuccessCount > 0)
                {
                    var summary = PartialExtractionHandler.GenerateResultSummary(result);
                    Logger.Log($"部分展開完了:\n{summary}");
                    
                    // 成功したファイルがある場合は成功として扱う
                    if (result.SuccessCount > 0)
                    {
                        progressWindow?.SetCompleted($"展開完了: {result.SuccessCount}/{result.TotalFiles}個のファイルが成功");
                        return true;
                    }
                }
                
                return false;
            }
            else
            {
                Logger.Log($"ArchiveExtractor.ExtractArchiveAsyncを呼び出し: filePath={filePath}, outputPath={outputPath}, progressWindow={progressWindow?.GetType().Name ?? "null"}");
                await ArchiveExtractor.ExtractArchiveAsync(filePath, outputPath, progress, progressWindow, cancellationToken);

                Logger.Log($"展開処理が完了: {filePath}");
                return true;
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Log($"展開処理がキャンセルされました: {filePath}");
            progressWindow?.SetCompleted("キャンセルしました。");
            MessageBox.Show("展開処理をキャンセルしました。", "キャンセル", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        catch (Exception ex)
        {
            // 詳細なエラー分析を実行
            var errorInfo = ArchiveErrorHandler.AnalyzeError(ex, filePath, outputPath);
            Logger.LogException($"展開処理でエラーが発生: {filePath}", ex);
            
            // エラーダイアログを表示
            var errorMessage = $"{errorInfo.Message}\n\n詳細: {errorInfo.Details}\n\n推奨対処法: {errorInfo.RecommendedAction}";
            MessageBox.Show(errorMessage, "展開エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            
            return false;
        }
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

            // ★ 【最適化】 ディスクI/O負荷を考慮し、並列数をCPUコア数ではなく制限
            // HDDの場合は並列数が多いとシーク多発で逆に遅くなるため、保守的な値に設定
            // SSDを前提とする場合でも、アーカイブ展開のメモリ使用量を考慮して上限を設定
            var maxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
            var semaphore = new System.Threading.SemaphoreSlim(maxDegreeOfParallelism);

            Logger.Log($"複数ファイル展開開始: {totalCount}個のファイル、最大並列度={maxDegreeOfParallelism}");

            var tasks = filePaths.Select(async (filePath, index) =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // ★ 修正: null を渡すと ExtractArchiveAsync 内部で progressWindow を使ってしまうため、
                    // 「何もしない Progress」を渡して、内部でのUI更新を無効化する。
                    // 並列処理中は「全体の何件目が終わったか」を外側のループで管理しているため、これで十分
                    var silentProgress = new Progress<ProgressInfo>(_ => { });
                    var success = await ExtractArchiveAsync(filePath, outputDir, outputToSameDirectory, progressWindow, cancellationToken, enablePartialExtraction: false, individualProgress: silentProgress);

                    lock (lockObject)
                    {
                        if (success)
                        {
                            successCount++;
                        }
                        else
                        {
                            failedFiles.Add(Path.GetFileName(filePath));
                        }

                        // 件数ベースの進捗のみを更新
                        var currentCount = successCount + failedFiles.Count;
                        var progress = (int)((double)currentCount / totalCount * 100);
                        
                        // ★ 【バグ修正】 UIスレッド上で更新を実行（クロススレッド操作違反を防ぐ）
                        progressWindow?.Dispatcher.Invoke(() =>
                            progressWindow.UpdateProgress(progress, $"展開中 ({currentCount}/{totalCount}): {Path.GetFileName(filePath)}")
                        );
                    }
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

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                Logger.Log("複数ファイル展開処理が全体でキャンセルされました");
                throw;
            }

            // 完了メッセージを表示
            if (successCount == totalCount)
            {
                Logger.Log($"複数ファイル展開完了: {successCount}/{totalCount}個の展開に成功");
                await Task.Delay(500);
                progressWindow?.Close();
                return true;
            }
            else
            {
                var failureMessage = failedFiles.Any() ? $"\n失敗: {string.Join(", ", failedFiles)}" : "";
                Logger.Log($"複数ファイル展開完了: {successCount}成功, {totalCount - successCount}失敗");
                await Task.Delay(500);
                progressWindow?.Close();
                return successCount > 0;
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Log("複数ファイル展開処理がキャンセルされました");
            progressWindow?.SetCompleted("キャンセルしました。");
            MessageBox.Show("展開処理をキャンセルしました。", "キャンセル", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogException("複数ファイル展開処理でエラーが発生", ex);
            MessageBox.Show($"展開中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>
    /// フォルダの圧縮処理を実行
    /// </summary>
    /// <param name="folderPath">圧縮するフォルダのパス</param>
    /// <param name="outputDir">出力ディレクトリ</param>
    /// <param name="outputToSameDirectory">同じディレクトリに出力するかどうか</param>
    /// <param name="format">圧縮形式</param>
    /// <param name="progressWindow">進行状況ウィンドウ（nullの場合はUI更新を行わない）</param>
    /// <param name="progressReporter">外部からの進捗報告用（並列処理時などに使用）</param>
    /// <returns>処理が成功した場合はtrue、そうでなければfalse</returns>
    public static async Task<bool> CompressFolderAsync(string folderPath, string outputDir, bool outputToSameDirectory, string format, View.ProgressWindow? progressWindow, IProgress<ProgressInfo>? progressReporter = null, CancellationToken cancellationToken = default)
    {
        Logger.Log($"ArchiveProcessor.CompressFolderAsync開始: folderPath={folderPath}, outputDir={outputDir}, outputToSameDirectory={outputToSameDirectory}, format={format}, progressWindow={progressWindow?.GetType().Name ?? "null"}");

        // ProgressWindow からキャンセルトークンを取得（nullの場合は渡されたトークンを使用）
        var actualCancellationToken = progressWindow != null ? progressWindow.GetCancellationToken() : cancellationToken;

        try
        {
            // フォルダの存在確認
            if (!Directory.Exists(folderPath))
            {
                Logger.Log($"指定されたフォルダが存在しません: {folderPath}");
                MessageBox.Show($"指定されたフォルダが見つかりません。\n{folderPath}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            // 圧縮形式の確認
            var supportedFormats = new[] { "zip", "7z", "tar", "gz", "bz2", "xz", "cab", "wim", "lzh" };
            if (!supportedFormats.Contains(format.ToLowerInvariant()))
            {
                Logger.Log($"サポートされていない圧縮形式です: {format}");
                MessageBox.Show($"サポートされていない圧縮形式です。\n{format}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            Logger.Log($"圧縮処理を開始: {folderPath}");

            // 出力ファイル名の取得
            var outputPath = ArchiveCompressor.GetCompressedFileName(folderPath, format, outputDir, outputToSameDirectory);

            // 出力ファイルが既に存在する場合は上書き確認
            if (File.Exists(outputPath))
            {
                Logger.Log($"出力ファイルが既に存在します: {outputPath}");

                if (progressWindow != null)
                {
                    // UIスレッドで上書き確認を実行
                    var canOverwrite = await progressWindow.Dispatcher.InvokeAsync(() =>
                        FileOverwriteDialog.ShowOverwriteDialog(folderPath, outputPath, progressWindow) == OverwriteResult.Yes);

                    Logger.Log($"上書き確認ダイアログ結果: canOverwrite={canOverwrite}");

                    if (!canOverwrite)
                    {
                        Logger.Log("ユーザーが圧縮処理をキャンセルしました");
                        return false;
                    }

                    // 上書きが許可された場合は既存ファイルを削除
                    File.Delete(outputPath);
                    Logger.Log($"既存ファイルを削除しました: {outputPath}");
                }
                else
                {
                    // progressWindowがnullの場合は自動的に上書き
                    Logger.Log("progressWindowがnullのため、既存ファイルを自動的に上書きします");
                    File.Delete(outputPath);
                }
            }

            // ファイル名を設定
            progressWindow?.SetFileName(outputPath);

            actualCancellationToken.ThrowIfCancellationRequested();

            // 圧縮処理を実行（最適化: LHA形式の場合、無駄なコピーを避けるためCompressDirectoryを使用）
            Logger.Log($"ArchiveCompressor.CompressDirectoryを呼び出し: folderPath={folderPath}, outputPath={outputPath}, format={format}, progressWindow={progressWindow?.GetType().Name ?? "null"}");

            await Task.Run(() =>
            {
                var progressCallback = new Action<ProgressInfo>(info =>
                {
                    // 1. レガシーなProgressWindow更新 (単体実行時用)
                    progressWindow?.UpdateProgress(info.Percentage, info.Status);

                    // 2. 外部から渡された進捗レポーターへの報告 (並列実行時用)
                    progressReporter?.Report(info);
                });

                ArchiveCompressor.CompressDirectory(folderPath, outputPath, progressCallback);
            }, actualCancellationToken);

            Logger.Log($"圧縮処理が完了: {folderPath} -> {outputPath}");

            await Task.Delay(500);
            progressWindow?.Close();

            return true;
        }
        catch (OperationCanceledException)
        {
            Logger.Log($"圧縮処理がキャンセルされました: {folderPath}");
            progressWindow?.SetCompleted("キャンセルしました。");
            MessageBox.Show("圧縮処理をキャンセルしました。", "キャンセル", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogException($"圧縮処理でエラーが発生: {folderPath}", ex);
            MessageBox.Show($"圧縮中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>
    /// 複数のフォルダの圧縮処理を実行（並列処理対応）
    /// </summary>
    /// <param name="folderPaths">圧縮するフォルダのパスの配列</param>
    /// <param name="outputDir">出力ディレクトリ</param>
    /// <param name="outputToSameDirectory">同じディレクトリに出力するかどうか</param>
    /// <param name="format">圧縮形式</param>
    /// <param name="progressWindow">進行状況ウィンドウ</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>すべての処理が成功した場合はtrue、そうでなければfalse</returns>
    public static async Task<bool> CompressFoldersAsync(string[] folderPaths, string outputDir, bool outputToSameDirectory, string format, View.ProgressWindow progressWindow, CancellationToken cancellationToken = default)
    {
        try
        {
            var totalCount = folderPaths.Length;
            var successCount = 0;
            var failedFolders = new List<string>();
            var lockObject = new object();

            // ProgressWindow からキャンセルトークンを取得（nullの場合は渡されたトークンを使用）
            var actualCancellationToken = progressWindow != null ? progressWindow.GetCancellationToken() : cancellationToken;

            // ★最適化: 並列処理時のCPUコア数を制限（圧縮処理自体がマルチスレッドなため）
            // 7-Zip等は内部で全コアを使うため、タスク並列数を抑えめにする
            var maxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
            var semaphore = new System.Threading.SemaphoreSlim(maxDegreeOfParallelism);

            Logger.Log($"複数フォルダ圧縮開始: {totalCount}個のフォルダ、最大並列度={maxDegreeOfParallelism}、形式={format}");

            var tasks = folderPaths.Select(async (folderPath, index) =>
            {
                await semaphore.WaitAsync(actualCancellationToken);
                try
                {
                    actualCancellationToken.ThrowIfCancellationRequested();

                    // ★修正ここから: 単一フォルダの場合は詳細な進捗を表示するためのReporterを作成
                    IProgress<ProgressInfo>? innerProgress = null;
                    if (totalCount == 1)
                    {
                        innerProgress = new Progress<ProgressInfo>(info =>
                        {
                            progressWindow?.Dispatcher.Invoke(() =>
                            {
                                progressWindow.UpdateProgress(info.Percentage, info.Status);
                                if (!string.IsNullOrEmpty(info.CurrentFileName))
                                {
                                    progressWindow.SetFileName(Path.GetFileName(info.CurrentFileName));
                                }
                            });
                        });
                    }

                    // progressWindowはnullのまま(二重更新防止)、innerProgressを渡す
                    var success = await CompressFolderAsync(folderPath, outputDir, outputToSameDirectory, format, null, innerProgress, actualCancellationToken);
                    // ★修正ここまで

                    lock (lockObject)
                    {
                        if (success)
                        {
                            successCount++;
                        }
                        else
                        {
                            failedFolders.Add(Path.GetFileName(folderPath));
                        }

                        // 件数ベースで進捗バーを更新（Dispatcher経由で安全に更新）
                        var progress = (int)((double)(index + 1) / totalCount * 100);
                        progressWindow?.Dispatcher.Invoke(() =>
                            progressWindow.UpdateProgress(progress, $"圧縮中 ({index + 1}/{totalCount}): {Path.GetFileName(folderPath)}")
                        );
                    }
                }
                catch (OperationCanceledException)
                {
                    Logger.Log($"フォルダ圧縮がキャンセルされました: {folderPath}");
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogException($"フォルダ圧縮でエラーが発生: {folderPath}", ex);
                    lock (lockObject)
                    {
                        failedFolders.Add(Path.GetFileName(folderPath));
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
                Logger.Log("複数フォルダ圧縮処理が全体でキャンセルされました");
                throw;
            }

            // 完了メッセージを表示
            if (successCount == totalCount)
            {
                Logger.Log($"複数フォルダ圧縮完了: {successCount}/{totalCount}個の圧縮に成功");
                await Task.Delay(500);
                progressWindow?.Close();
                return true;
            }
            else
            {
                var failureMessage = failedFolders.Any() ? $"\n失敗: {string.Join(", ", failedFolders)}" : "";
                Logger.Log($"複数フォルダ圧縮完了: {successCount}成功, {totalCount - successCount}失敗");
                await Task.Delay(500);
                progressWindow?.Close();
                return successCount > 0;
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Log("複数フォルダ圧縮処理がキャンセルされました");
            progressWindow?.SetCompleted("キャンセルしました。");
            MessageBox.Show("圧縮処理をキャンセルしました。", "キャンセル", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogException("複数フォルダ圧縮処理でエラーが発生", ex);
            MessageBox.Show($"圧縮中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
