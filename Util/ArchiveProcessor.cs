using System;
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Linq;
using System.Windows;
using System.Collections.Generic;

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
    /// <param name="enablePartialExtraction">部分展開を有効にするかどうか</param>
    /// <returns>処理が成功した場合はtrue、そうでなければfalse</returns>
    public static async Task<bool> ExtractArchiveAsync(string filePath, string outputDir, bool outputToSameDirectory, View.ProgressWindow progressWindow, CancellationToken cancellationToken = default, bool enablePartialExtraction = false)
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

            // 出力先ディレクトリの取得
            outputPath = ArchiveExtractor.GetOutputDirectory(filePath, outputDir, outputToSameDirectory);

            // ファイル名を設定
            progressWindow?.SetFileName(filePath);

            cancellationToken.ThrowIfCancellationRequested();

            // 展開処理を実行
            var progress = new Progress<int>(percentage =>
            {
                progressWindow?.UpdateProgress(percentage, "ファイルを展開中...");
            });

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

            // 同時実行数を CPU コア数に制限（メモリ保護）
            var maxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4);
            var semaphore = new System.Threading.SemaphoreSlim(maxDegreeOfParallelism);

            Logger.Log($"複数ファイル展開開始: {totalCount}個のファイル、最大並列度={maxDegreeOfParallelism}");

            var tasks = filePaths.Select(async (filePath, index) =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var success = await ExtractArchiveAsync(filePath, outputDir, outputToSameDirectory, progressWindow, cancellationToken);

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

                        var progress = (int)((double)(index + 1) / totalCount * 100);
                        progressWindow?.UpdateProgress(progress, $"展開中: {Path.GetFileName(filePath)}");
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
                progressWindow?.SetCompleted("展開が完了しました。");
                Logger.Log($"複数ファイル展開完了: {successCount}/{totalCount}個の展開に成功");
                await Task.Delay(1000);
                progressWindow?.Close();
                return true;
            }
            else
            {
                var failureMessage = failedFiles.Any() ? $"\n失敗: {string.Join(", ", failedFiles)}" : "";
                progressWindow?.SetCompleted($"{successCount}/{totalCount}個のファイルの展開が完了しました。{failureMessage}");
                Logger.Log($"複数ファイル展開完了: {successCount}成功, {totalCount - successCount}失敗");
                await Task.Delay(1000);
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
    /// <param name="progressWindow">進行状況ウィンドウ</param>
    /// <returns>処理が成功した場合はtrue、そうでなければfalse</returns>
    public static async Task<bool> CompressFolderAsync(string folderPath, string outputDir, bool outputToSameDirectory, string format, View.ProgressWindow progressWindow, CancellationToken cancellationToken = default)
    {
        Logger.Log($"ArchiveProcessor.CompressFolderAsync開始: folderPath={folderPath}, outputDir={outputDir}, outputToSameDirectory={outputToSameDirectory}, format={format}, progressWindow={progressWindow?.GetType().Name ?? "null"}");
        
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
            var supportedFormats = new[] { "zip", "7z", "tar", "gz", "bz2", "xz", "cab", "wim" };
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
                        FileOverwriteDialog.CanOverwriteFile(folderPath, outputPath, progressWindow));
                    
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

            cancellationToken.ThrowIfCancellationRequested();

            // 圧縮処理を実行
            var progress = new Progress<int>(percentage =>
            {
                progressWindow?.UpdateProgress(percentage, "フォルダを圧縮中...");
            });

            Logger.Log($"ArchiveCompressor.CompressAsyncを呼び出し: folderPath={folderPath}, outputPath={outputPath}, format={format}, progressWindow={progressWindow?.GetType().Name ?? "null"}");
            await ArchiveCompressor.CompressAsync(folderPath, outputPath, format, progress, cancellationToken);

            Logger.Log($"圧縮処理が完了: {folderPath} -> {outputPath}");
            
            // 完了メッセージを表示
            progressWindow?.SetCompleted("圧縮が完了しました。");
            await Task.Delay(1000);
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

            // 同時実行数を CPU コア数に制限（メモリ保護）
            var maxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4);
            var semaphore = new System.Threading.SemaphoreSlim(maxDegreeOfParallelism);

            Logger.Log($"複数フォルダ圧縮開始: {totalCount}個のフォルダ、最大並列度={maxDegreeOfParallelism}、形式={format}");

            var tasks = folderPaths.Select(async (folderPath, index) =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var success = await CompressFolderAsync(folderPath, outputDir, outputToSameDirectory, format, progressWindow, cancellationToken);

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

                        var progress = (int)((double)(index + 1) / totalCount * 100);
                        progressWindow?.UpdateProgress(progress, $"圧縮中: {Path.GetFileName(folderPath)}");
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
                progressWindow?.SetCompleted("圧縮が完了しました。");
                Logger.Log($"複数フォルダ圧縮完了: {successCount}/{totalCount}個の圧縮に成功");
                await Task.Delay(1000);
                progressWindow?.Close();
                return true;
            }
            else
            {
                var failureMessage = failedFolders.Any() ? $"\n失敗: {string.Join(", ", failedFolders)}" : "";
                progressWindow?.SetCompleted($"{successCount}/{totalCount}個のフォルダの圧縮が完了しました。{failureMessage}");
                Logger.Log($"複数フォルダ圧縮完了: {successCount}成功, {totalCount - successCount}失敗");
                await Task.Delay(1000);
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
