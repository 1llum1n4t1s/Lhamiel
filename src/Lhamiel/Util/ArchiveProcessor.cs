#pragma warning disable CS0618 // PartialExtractionHandler は [Obsolete] だが移行完了まで使用（参照が複数メソッドに分散するためファイルレベルで抑制）
using Lhamiel.View;
namespace Lhamiel.Util;

/// <summary>
/// アーカイブ処理を共通化するクラス
/// </summary>
public static class ArchiveProcessor
{
    // 進捗ディスパッチ系のヘルパは ArchiveProgressHelper.cs に分離。

    // テスト可能化用の差し替えポイント（DI コンテナは導入せず internal static プロパティで差し替え）
    internal static IMessageService MessageServiceImpl { get; set; } = new DefaultMessageService();
    internal static IUiDispatcher UiDispatcherImpl { get; set; } = new DefaultUiDispatcher();
    internal static IConflictDialogService ConflictDialogImpl { get; set; } = new DefaultConflictDialogService();

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
    /// <param name="settingsSnapshot">設定のスナップショット（バッチ処理時に呼び出し側で 1 回だけ取得して渡すと、各ファイルごとのロック競合＆アロケを削減できる）</param>
    public static async Task<(string? outputPath, ArchiveExtractor.ArchiveStructureInfo? structureInfo)> ExtractArchiveAsync(string filePath, string outputDir, bool outputToSameDirectory, ProgressWindow? progressWindow, CancellationToken cancellationToken = default, bool enablePartialExtraction = false, IProgress<ProgressInfo>? individualProgress = null, bool closeWindowOnCompletion = true, Settings? settingsSnapshot = null)
    {
        Logger.Log($"ArchiveProcessor.ExtractArchiveAsync開始: filePath={filePath}, outputDir={outputDir}, outputToSameDirectory={outputToSameDirectory}");

        // ファイル存在確認などの軽量なチェックはUIスレッドで実施
        if (!File.Exists(filePath))
        {
            Logger.Log($"指定されたファイルが存在しません: {filePath}");
            _ = MessageServiceImpl.ShowError(App.Text("Error.FileNotFound", filePath));
            return (null, null);
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
                        ArchiveProgressHelper.DispatchProgress(progressWindow, info));
                }

                // ファイル拡張子の確認（ArchiveExtractor.SupportedExtensions を参照して重複管理を回避）
                var extension = Path.GetExtension(filePath).ToLowerInvariant();

                if (!ArchiveExtractor.SupportedExtensions.Contains(extension))
                {
                    Logger.Log($"サポートされていないファイル形式です: {extension}");
                    UiDispatcherImpl.Post(() => _ = MessageServiceImpl.ShowError(App.Text("Error.UnsupportedFormat", extension)));
                    return (null, null);
                }

                // --- ここから重いI/O処理 ---

                // 1. 出力先の決定 (バックグラウンドで実行)
                var baseDirectory = ArchiveExtractor.GetBaseOutputDirectory(filePath, outputDir, outputToSameDirectory);

                // アーカイブの構造を一度だけ解析
                var rawStructureInfo = ArchiveExtractor.GetArchiveStructureInfo(filePath);
                // 設定は処理開始時点でスナップショットを取って一貫性を保つ（UIの設定変更と race しない）。
                // バッチ処理から渡された settingsSnapshot があればそれを再利用し、各ファイルごとの
                // ロック競合＆浅コピーアロケを回避する。
                var snapshot = settingsSnapshot ?? SettingsManager.Instance.CreateSnapshot();
                var createFolder = snapshot.CreateArchiveNameFolder;
                // 後段の FolderOpener が同じ値を使うよう、スナップショットした createFolder を
                // ArchiveStructureInfo に同梱して返す（with 式で rawStructureInfo の他プロパティを
                // そのまま引き継ぐため、ArchiveStructureInfo にプロパティが追加されても自動追従する）。
                structureInfo = rawStructureInfo with
                {
                    CapturedCreateArchiveNameFolder = createFolder,
                };

                // 出力先を決定
                if (!createFolder)
                {
                    // フォルダ作成OFF: 常にbaseDirectoryに直接展開
                    outputPath = baseDirectory;
                    Logger.Log($"フォルダ作成OFF: baseDirectoryに直接展開 -> {outputPath}");
                }
                else if (structureInfo.ShouldSkipFolderCreation)
                {
                    // フォルダ作成ON だが、ルートフォルダがアーカイブ名と一致 → 二重ネスト防止のためフォルダ作成スキップ
                    outputPath = baseDirectory;
                    Logger.Log($"フォルダ作成スキップ（二重ネスト防止）: {outputPath}");
                }
                else
                {
                    // フォルダ作成ON: アーカイブ名フォルダを作成
                    var archiveName = ArchiveExtractor.GetArchiveBaseName(filePath);
                    outputPath = Path.Combine(baseDirectory, archiveName);
                    Logger.Log($"フォルダ作成ON: {outputPath}");
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
                        (percentage, _) => UiDispatcherImpl.Post(() => progressWindow?.UpdateProgress(percentage)),
                        failedFile => ShowErrorRecoveryDialogAsync(failedFile, progressWindow),
                        cancellationToken);

                    if (result.SuccessCount > 0)
                    {
                        var summary = PartialExtractionHandler.GenerateResultSummary(result);
                        Logger.Log($"部分展開完了:\n{summary}");

                        UiDispatcherImpl.Post(() =>
                            progressWindow?.SetCompleted(App.Text("Progress.ExtractionComplete", result.SuccessCount, result.TotalFiles)));

                        if (closeWindowOnCompletion)
                        {
                            progressWindow?.CloseSafe();
                        }
                        return (outputPath, structureInfo);
                    }
                    return (null, null);
                }
                else
                {
                    // 上書き確認パスの精密化:
                    // フォルダ作成時: outputPath（baseDir/archiveName）の存在をチェック → overwriteCheckPaths=null
                    // baseDir直接展開時: 展開されるトップレベルアイテムのパスのみをチェック
                    IReadOnlyList<string>? overwriteCheckPaths = null;
                    if (outputPath == baseDirectory && !string.IsNullOrEmpty(structureInfo.SingleRootItemName))
                    {
                        overwriteCheckPaths = [Path.Combine(outputPath, structureInfo.SingleRootItemName)];
                    }

                    // 一時フォルダ方式（上書き確認あり）or 直接展開
                    // structureInfo.TotalUncompressedSize は GetArchiveStructureInfo で計算済み。
                    // ExtractArchiveAsync 側で再度 reader を開いて Items を走査するのを避ける。
                    await ArchiveExtractor.ExtractArchiveAsync(filePath, outputPath,
                        progress,
                        progressWindow,
                        cancellationToken,
                        overwriteCheckPaths,
                        progressWindow,
                        structureInfo.TotalUncompressedSize);

                    // 展開後 CRC 整合性検証（設定で有効な場合のみ）
                    if (snapshot.VerifyAfterExtraction)
                    {
                        progressWindow?.SetIndeterminate(App.Text("Progress.VerifyingIntegrity"));
                        var verification = await ArchiveIntegrityVerifier.VerifyArchiveAsync(filePath, cancellationToken);
                        if (!verification.IsValid)
                        {
                            Logger.Log($"展開後 CRC 検証失敗: {filePath} - {verification.ErrorMessage}", LogLevel.Warning);
                            await UiDispatcherImpl.InvokeAsync(() =>
                                MessageServiceImpl.ShowError(
                                    App.Text("Error.CrcVerificationFailed", Path.GetFileName(filePath), verification.ErrorMessage ?? "")));
                        }
                    }

                    // Mark of the Web 伝播（設定で有効 かつ 元アーカイブに Zone.Identifier がある場合）
                    // 既存ファイルに誤って Zone.Identifier を付与しないよう、ディレクトリ全体ではなく
                    // 展開されたルートアイテムのみに限定する（outputPath が既存フォルダの場合も安全）
                    if (snapshot.PropagateMarkOfTheWeb && outputPath != null && structureInfo != null)
                    {
                        progressWindow?.SetIndeterminate(App.Text("Progress.ApplyingSecurityMark"));
                        var zoneId = MotwPropagator.ReadZoneIdentifier(filePath);
                        if (zoneId != null)
                        {
                            foreach (var rootName in structureInfo.RootItemNames)
                            {
                                var rootItemPath = Path.Combine(outputPath, rootName);
                                if (Directory.Exists(rootItemPath))
                                    MotwPropagator.PropagateToDirectory(rootItemPath, zoneId, cancellationToken);
                                else if (File.Exists(rootItemPath))
                                    MotwPropagator.TryWriteZoneIdentifier(rootItemPath, zoneId);
                            }
                        }
                    }

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
                // 進捗ウィンドウを先に閉じてからダイアログを表示。Post + 破棄では進捗ウィンドウの
                // クローズ遷移と競合し、ダイアログが背面に隠れる/表示されないことがあるため、
                // ここで明示的に閉じてから await し、ダイアログの表示完了を待ってから return する。
                if (closeWindowOnCompletion)
                {
                    progressWindow?.CloseSafe();
                }
                await UiDispatcherImpl.InvokeAsync(() =>
                    MessageServiceImpl.ShowError(
                        $"{errorInfo.Message}\n\n{App.Text("Dialog.Details")}{errorInfo.Details}",
                        App.Text("Error.ExtractionTitle")));
                return ((string?)null, (ArchiveExtractor.ArchiveStructureInfo?)null);
            }
            finally
            {
                // 例外発生時にも確実にクリーンアップ（catch 内で既に閉じていれば CloseSafe が no-op）
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
    public static async Task<List<(string SourcePath, string OutputPath, ArchiveExtractor.ArchiveStructureInfo StructureInfo)>> ExtractArchivesAsync(string[] filePaths, string outputDir, bool outputToSameDirectory, ProgressWindow? progressWindow, CancellationToken cancellationToken = default, bool closeWindowOnCompletion = true)
    {
        var results = new List<(string SourcePath, string OutputPath, ArchiveExtractor.ArchiveStructureInfo StructureInfo)>();
        try
        {
            var totalCount = filePaths.Length;
            var successCount = 0;
            var failedFiles = new List<string>();
            var lockObject = new object();

            // ディスクI/O負荷を考慮し、並列数をCPUコア数ではなく制限
            var maxDegreeOfParallelism = ArchiveProgressHelper.IoBoundParallelism;
            using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);

            Logger.Log($"複数ファイル展開開始: {totalCount}個のファイル、最大並列度={maxDegreeOfParallelism}");

            // バッチ処理の開始時点で 1 回だけスナップショットを取って全タスクに配る。
            // 各並列タスクが個別に CreateSnapshot すると同じ設定の浅コピー + ロック競合が
            // 並列度分発生するため、それを回避する。
            var sharedSettings = SettingsManager.Instance.CreateSnapshot();

            // 全タスク横断で共有するスロットラー（UIスレッドへの通知頻度を全体で制限）
            var sharedThrottler = new ProgressThrottler();

            var tasks = filePaths.Select(async (filePath, index) =>
            {
                var acquired = false;
                try
                {
                    await semaphore.WaitAsync(cancellationToken);
                    acquired = true;
                    cancellationToken.ThrowIfCancellationRequested();

                    var mappedProgress = ArchiveProgressHelper.CreateMappedProgress(
                        totalCount, lockObject, () => successCount + failedFiles.Count, progressWindow, sharedThrottler);

                    var extractResult = await ExtractArchiveAsync(filePath, outputDir, outputToSameDirectory, progressWindow, cancellationToken, enablePartialExtraction: false, individualProgress: mappedProgress, closeWindowOnCompletion: false, settingsSnapshot: sharedSettings);
                    var finalOutputPath = extractResult.outputPath;
                    var structureInfo = extractResult.structureInfo;

                    // lock 内で状態のみ更新し、Dispatcher への通知は lock 外で実行
                    var progressToReport = 0;

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

                        // 完了数ベースの進捗を計算（並列時も単調増加が保証される）
                        progressToReport = (int)((double)(successCount + failedFiles.Count) / totalCount * 100);
                    }

                    UiDispatcherImpl.Post(() =>
                        progressWindow?.UpdateProgress(progressToReport));
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
                    // WaitAsync が失敗した場合（キャンセル等）は Release しない。
                    // 成功時のみ Release することで SemaphoreFullException / カウント超過を防ぐ。
                    if (acquired) semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);

            // 並列タスク内ではスローせず、WhenAll 後にここで一度だけスローする
            cancellationToken.ThrowIfCancellationRequested();

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
            _ = UiDispatcherImpl.InvokeAsync(() => MessageServiceImpl.ShowError(App.Text("Error.DuringExtraction", ex.Message)));

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
    /// <param name="overrideOutputPath">出力パスを明示的に指定する場合（衝突回避で事前計算済みのパス）</param>
    /// <param name="settingsSnapshot">設定のスナップショット（バッチ処理時に呼び出し側で 1 回だけ取得して渡すと、各ファイルごとのロック競合＆アロケを削減できる）</param>
    /// <returns>処理が成功した場合はtrue、そうでなければfalse</returns>
    public static async Task<bool> CompressItemAsync(string sourcePath, string outputDir, bool outputToSameDirectory, string format, ProgressWindow? progressWindow, IProgress<ProgressInfo>? progressReporter = null, CancellationToken cancellationToken = default, bool closeWindowOnCompletion = true, string? overrideOutputPath = null, Settings? settingsSnapshot = null)
    {
        Logger.Log($"ArchiveProcessor.CompressItemAsync開始: sourcePath={sourcePath}, outputDir={outputDir}, outputToSameDirectory={outputToSameDirectory}, format={format}");

        // 対象の存在確認（軽量なチェックはUIスレッドで実施）
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
        {
            Logger.Log($"指定された対象が存在しません: {sourcePath}");
            _ = MessageServiceImpl.ShowError(App.Text("Error.FolderNotFound", sourcePath));
            return false;
        }

        // 圧縮形式の確認
        if (!ArchiveCompressor.WritableFormats.Contains(format))
        {
            Logger.Log($"サポートされていない圧縮形式です: {format}");
            _ = MessageServiceImpl.ShowError(App.Text("Error.UnsupportedCompression", format));
            return false;
        }

        // ProgressWindow のキャンセルと呼び出し元のキャンセルを両方尊重するためリンクする。
        // 旧実装は progressWindow!=null のとき引数の cancellationToken を無視していた。
        using var linkedCts = progressWindow != null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progressWindow.GetCancellationToken())
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var actualCancellationToken = linkedCts.Token;

        // 重い処理全体を Task.Run でバックグラウンドへ移動
        return await Task.Run(async () =>
        {
            try
            {
                Logger.Log($"圧縮処理を開始: {sourcePath}");

                // 出力ファイル名の取得（overrideOutputPath が指定されている場合はそちらを使用）
                var outputPath = overrideOutputPath ?? ArchiveCompressor.GetCompressedFileName(sourcePath, format, outputDir, outputToSameDirectory);

                // 出力先が既に存在する場合は上書き確認
                var targetExists = File.Exists(outputPath) || Directory.Exists(outputPath);
                if (targetExists)
                {
                    Logger.Log($"出力先が既に存在します: {outputPath}");

                    var canOverwrite = await ConflictDialogImpl.CanOverwriteFromBackgroundAsync(sourcePath, outputPath, progressWindow);
                    Logger.Log($"上書き確認ダイアログ結果: canOverwrite={canOverwrite}");

                    if (!canOverwrite)
                    {
                        Logger.Log("ユーザーが圧縮処理をキャンセルしました");
                        return false;
                    }

                    // 上書きが許可された場合は既存の対象を削除。
                    // 保護されたパス（デスクトップ・マイドキュメント等の shell folder や
                    // ドライブルート）を outputPath として指定された場合の削除を拒否する。
                    // ディレクトリ削除は再帰削除のため特に危険だが、File.Delete 経路でも
                    // outputPath 自体が保護対象（エッジケース）の場合は拒否しておく。
                    try
                    {
                        if (PathValidator.IsProtectedDirectory(outputPath))
                        {
                            Logger.Log($"圧縮上書き: 保護されたパスへの削除を拒否: {outputPath}", LogLevel.Warning);
                            throw new InvalidOperationException(App.Text("Error.ProtectedDirectory", outputPath));
                        }
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
                    catch (InvalidOperationException)
                    {
                        // 保護ディレクトリエラーはそのまま再スロー
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"既存対象の削除に失敗しました: {outputPath}, {ex.Message}");
                        throw new InvalidOperationException(App.Text("Error.FileLocked", Path.GetFileName(outputPath)), ex);
                    }
                }

                // 圧縮前のディスク容量チェック
                var estimatedSize = DiskSpaceChecker.GetTotalFileSize([sourcePath]);
                if (estimatedSize > 0)
                {
                    var hasSpace = await DiskSpaceChecker.EnsureDiskSpaceAsync(
                        outputPath, estimatedSize, progressWindow, actualCancellationToken);
                    if (!hasSpace)
                        throw new OperationCanceledException(App.Text("Error.DiskSpaceCancelled"));
                }

                // 圧縮処理を実行
                Logger.Log($"ArchiveCompressor.CompressFilesAsyncを呼び出し: sourcePath={sourcePath}, outputPath={outputPath}, format={format}");

                var parsedFormat = ArchiveCompressor.ParseFormat(format);
                // CompressFilesAsync が IProgress<ProgressInfo> に統一されたので直接渡す。
                // progressReporter が渡されていればそれをそのまま使い、Progress<T> の二重
                // ラップと無駄なアロケ・同期コンテキスト転送を避ける。null のときだけ
                // progressWindow への DispatchProgress 用ラッパを 1 個だけ作る。
                IProgress<ProgressInfo> compressionProgress = progressReporter
                    ?? new Progress<ProgressInfo>(info => ArchiveProgressHelper.DispatchProgress(progressWindow, info));

                // Flatモードで個別圧縮時にrelativePath重複があれば競合ダイアログを表示。
                // 設定は処理開始時点でスナップショット化し、以降の処理全体で一貫性を保つ。
                // バッチから渡された settingsSnapshot があれば再利用する（ロック競合回避）。
                var settings = settingsSnapshot ?? SettingsManager.Instance.CreateSnapshot();
                List<(string fullPath, string relativePath)>? resolvedFiles = null;
                if (settings.DirectoryStructureMode == DirectoryStructureMode.Flat && Directory.Exists(sourcePath))
                {
                    var scannedFiles = await ArchiveCompressor.ScanSourceFiles(
                        [sourcePath],
                        new HashSet<string>(settings.ExcludedFilePatterns ?? [], StringComparer.OrdinalIgnoreCase),
                        actualCancellationToken,
                        normalizeUnicodeOverride: settings.NormalizeUnicodeFileNames);

                    var conflicts = ArchiveCompressor.DetectConflicts(scannedFiles);
                    if (conflicts.Count > 0)
                    {
                        var (result, selectedFiles) = await ConflictDialogImpl.ShowFromBackgroundAsync(conflicts, progressWindow, isTwoPane: false);
                        if (result == Models.FileConflictResult.Cancel)
                            return false;

                        // 競合ファイルを除外し、選択されたファイルを追加
                        var conflictingPaths = new HashSet<string>(
                            conflicts.SelectMany(g => g.Entries.Select(e => e.FullPath)),
                            StringComparer.OrdinalIgnoreCase);
                        resolvedFiles = scannedFiles
                            .Where(f => !conflictingPaths.Contains(f.fullPath))
                            .Concat(selectedFiles)
                            .ToList();
                        if (resolvedFiles.Count == 0)
                            return false;
                    }
                    else
                    {
                        resolvedFiles = scannedFiles;
                    }
                }

                await ArchiveCompressor.CompressFilesAsync([sourcePath], outputPath, parsedFormat, compressionProgress, actualCancellationToken, resolvedFiles, settingsOverride: settings);

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
                // 進捗ウィンドウを先に閉じてから await でダイアログ表示完了を待つ（背面隠れ防止）
                if (progressReporter == null && closeWindowOnCompletion)
                {
                    progressWindow?.CloseSafe();
                }
                await UiDispatcherImpl.InvokeAsync(() => MessageServiceImpl.ShowError(App.Text("Error.DuringCompression", ex.Message)));
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
    /// 個別圧縮時の出力パス衝突を検出し、衝突があればダイアログで確認する。
    /// ユーザーが非選択にしたソースは除外され、複数選択は自動リネームされる。
    /// </summary>
    /// <returns>解決後の (sourcePaths, outputPaths) ペア。キャンセル時は空配列</returns>
    private static async Task<(string[] sourcePaths, string[] outputPaths)> ResolveOutputPathConflictsWithDialog(
        string[] sourcePaths, string outputDir, bool outputToSameDirectory, string format, ProgressWindow? progressWindow)
    {
        // 出力パスを計算してグループ化
        var entries = sourcePaths.Select(sp => new
        {
            SourcePath = sp,
            OutputPath = ArchiveCompressor.GetCompressedFileName(sp, format, outputDir, outputToSameDirectory)
        }).ToList();

        var conflictGroups = entries
            .GroupBy(e => e.OutputPath, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        if (conflictGroups.Count == 0)
        {
            // 衝突なし: そのまま返す
            return (sourcePaths, entries.Select(e => e.OutputPath).ToArray());
        }

        // 衝突グループを FileConflictGroup に変換
        var dialogGroups = conflictGroups.Select(g =>
        {
            var outputName = Path.GetFileName(g.Key);
            return new Models.FileConflictGroup
            {
                ConflictingName = outputName,
                Entries = g.Select(e =>
                {
                    var info = File.Exists(e.SourcePath) ? new FileInfo(e.SourcePath) : null;
                    var dirInfo = Directory.Exists(e.SourcePath) ? new DirectoryInfo(e.SourcePath) : null;
                    return new Models.FileConflictEntry(
                        e.SourcePath,
                        outputName,
                        info?.Length ?? 0,
                        info?.LastWriteTime ?? dirInfo?.LastWriteTime ?? DateTime.MinValue);
                }).ToList()
            };
        }).ToList();

        Logger.Log($"個別圧縮の出力パス衝突を検出: {conflictGroups.Count}グループ");

        // ダイアログ表示（圧縮時は縦1列モード）
        var (result, selectedFiles) = await ConflictDialogImpl.ShowFromBackgroundAsync(dialogGroups, progressWindow, isTwoPane: false);
        if (result == Models.FileConflictResult.Cancel)
            return ([], []);

        // ダイアログで選択されたソースパスのセット（リネーム後のrelativePathとペア）
        var selectedSourcePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fullPath, relativePath) in selectedFiles)
        {
            selectedSourcePaths[fullPath] = relativePath;
        }

        // 衝突していないエントリ + ダイアログで選択されたエントリをマージ
        var conflictingSourcePaths = new HashSet<string>(
            conflictGroups.SelectMany(g => g.Select(e => e.SourcePath)),
            StringComparer.OrdinalIgnoreCase);

        var resolvedSources = new List<string>();
        var resolvedOutputs = new List<string>();

        foreach (var entry in entries)
        {
            if (!conflictingSourcePaths.Contains(entry.SourcePath))
            {
                // 衝突なし: そのまま
                resolvedSources.Add(entry.SourcePath);
                resolvedOutputs.Add(entry.OutputPath);
            }
            else if (selectedSourcePaths.TryGetValue(entry.SourcePath, out var renamedName))
            {
                // ダイアログで選択された: リネーム後の名前で出力
                var outputDir2 = Path.GetDirectoryName(entry.OutputPath) ?? "";
                resolvedSources.Add(entry.SourcePath);
                resolvedOutputs.Add(Path.Combine(outputDir2, renamedName));
            }
            // else: ダイアログで非選択 → スキップ
        }

        return (resolvedSources.ToArray(), resolvedOutputs.ToArray());
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
    public static async Task<bool> CompressItemsAsync(string[] sourcePaths, string outputDir, bool outputToSameDirectory, string format, ProgressWindow progressWindow, CancellationToken cancellationToken = default, bool closeWindowOnCompletion = true)
    {
        try
        {
            var totalCount = sourcePaths.Length;
            var successCount = 0;
            var failedPaths = new List<string>();
            var lockObject = new object();

            using var linkedCts = progressWindow != null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progressWindow.GetCancellationToken())
                : null;
            var actualCancellationToken = linkedCts?.Token ?? cancellationToken;

            var maxDegreeOfParallelism = ArchiveProgressHelper.IoBoundParallelism;
            using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);

            Logger.Log($"複数対象圧縮開始: {totalCount}個の対象、並列制限={maxDegreeOfParallelism}、形式={format}");

            // 出力パスを事前計算し、衝突を検出
            var (resolvedSourcePaths, resolvedOutputPaths) = await ResolveOutputPathConflictsWithDialog(
                sourcePaths, outputDir, outputToSameDirectory, format, progressWindow);

            if (resolvedSourcePaths.Length == 0)
            {
                Logger.Log("出力パス衝突の解決がキャンセルされたか、全てスキップされました");
                if (closeWindowOnCompletion) progressWindow?.CloseSafe();
                return false;
            }

            // 衝突解決後のカウントで進捗管理
            totalCount = resolvedSourcePaths.Length;

            // 全タスク横断で共有するスロットラー（UIスレッドへの通知頻度を全体で制限）
            var sharedThrottler = new ProgressThrottler();

            // バッチ開始時点で 1 回だけスナップショットを取って全タスクに配る（ロック競合回避）
            var sharedSettings = SettingsManager.Instance.CreateSnapshot();

            var tasks = resolvedSourcePaths.Select(async (sourcePath, index) =>
            {
                var acquired = false;
                try
                {
                    await semaphore.WaitAsync(actualCancellationToken);
                    acquired = true;
                    actualCancellationToken.ThrowIfCancellationRequested();

                    var innerProgress = ArchiveProgressHelper.CreateMappedProgress(
                        totalCount, lockObject, () => successCount + failedPaths.Count, progressWindow, sharedThrottler);

                    // 事前計算された出力パスを使用して圧縮処理を実行（共有スナップショットを再利用）
                    var success = await CompressItemAsync(sourcePath, outputDir, outputToSameDirectory, format, progressWindow, innerProgress, actualCancellationToken, closeWindowOnCompletion: false, overrideOutputPath: resolvedOutputPaths[index], settingsSnapshot: sharedSettings);

                    // lock 内で状態のみ更新し、Dispatcher への通知は lock 外で実行
                    var completedProgress = 0;

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

                        // 完了数ベースの進捗を計算（並列時も単調増加が保証される）
                        completedProgress = (int)((double)(successCount + failedPaths.Count) / totalCount * 100);
                    }

                    UiDispatcherImpl.Post(() =>
                        progressWindow?.UpdateProgress(completedProgress));
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
                    // WaitAsync が失敗した場合（キャンセル等）は Release しない。
                    if (acquired) semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);

            // 並列タスク内ではスローせず、WhenAll 後にここで一度だけスローする
            actualCancellationToken.ThrowIfCancellationRequested();

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
            Logger.Log($"複数対象圧縮完了: {successCount}成功, {totalCount - successCount}失敗");

            if (closeWindowOnCompletion)
            {
                progressWindow?.CloseSafe();
            }
            return successCount > 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogException("複数対象圧縮処理でエラーが発生", ex);
            _ = UiDispatcherImpl.InvokeAsync(() => MessageServiceImpl.ShowError(App.Text("Error.DuringCompression", ex.Message)));

            // 例外発生時にも確実にクリーンアップ
            if (closeWindowOnCompletion)
            {
                progressWindow?.CloseSafe();
            }

            return false;
        }
    }

    /// <summary>
    /// 複数のファイル・フォルダを1つのアーカイブにまとめて圧縮する
    /// </summary>
    /// <param name="sourcePaths">圧縮する対象のパスの配列</param>
    /// <param name="outputDir">出力ディレクトリ</param>
    /// <param name="outputToSameDirectory">同じディレクトリに出力するかどうか</param>
    /// <param name="format">圧縮形式</param>
    /// <param name="progressWindow">進行状況ウィンドウ</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <param name="closeWindowOnCompletion">完了時に進捗ウィンドウを閉じるかどうか</param>
    /// <returns>処理が成功した場合はtrue</returns>
    public static async Task<bool> CompressMergedAsync(string[] sourcePaths, string outputDir, bool outputToSameDirectory, string format, ProgressWindow? progressWindow, CancellationToken cancellationToken = default, bool closeWindowOnCompletion = true)
    {
        if (sourcePaths.Length == 0) return false;

        Logger.Log($"まとめ圧縮開始: {sourcePaths.Length}個の対象を1つのアーカイブに圧縮、形式={format}");

        // 圧縮形式の確認
        if (!ArchiveCompressor.WritableFormats.Contains(format))
        {
            Logger.Log($"サポートされていない圧縮形式です: {format}");
            _ = MessageServiceImpl.ShowError(App.Text("Error.UnsupportedCompression", format));
            return false;
        }

        // 出力先ディレクトリを決定（最初のファイルの場所を基準にする）
        var baseDir = outputToSameDirectory
            ? Path.GetDirectoryName(sourcePaths[0]) ?? ""
            : outputDir;

        // アーカイブ名: 最初のアイテム名を使用
        var firstPath = sourcePaths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var archiveName = Path.GetFileNameWithoutExtension(firstPath) is { Length: > 0 } stem
            ? stem
            : Path.GetFileName(firstPath);

        var lowerFormat = format.ToLowerInvariant();
        var outputPath = Path.Combine(baseDir, $"{archiveName}.{lowerFormat}");

        // ProgressWindow からキャンセルトークンを取得
        var actualCancellationToken = progressWindow?.GetCancellationToken() ?? cancellationToken;

        return await Task.Run(async () =>
        {
            try
            {
                // 出力先が既に存在する場合は上書き確認
                if (File.Exists(outputPath))
                {
                    var canOverwrite = await ConflictDialogImpl.CanOverwriteFromBackgroundAsync(sourcePaths[0], outputPath, progressWindow);
                    if (!canOverwrite)
                    {
                        Logger.Log("ユーザーがまとめ圧縮をキャンセルしました");
                        return false;
                    }

                    File.Delete(outputPath);
                }

                // ファイルリストをスキャン。
                // 設定は処理開始時点でスナップショット化して以降の race を避ける。
                var settings = SettingsManager.Instance.CreateSnapshot();
                var excludedPatternSet = new HashSet<string>(
                    settings.ExcludedFilePatterns ?? [],
                    StringComparer.OrdinalIgnoreCase);
                var scannedFiles = await ArchiveCompressor.ScanSourceFiles(
                    sourcePaths.ToList(), excludedPatternSet, actualCancellationToken,
                    normalizeUnicodeOverride: settings.NormalizeUnicodeFileNames);

                // 衝突検出
                var conflicts = ArchiveCompressor.DetectConflicts(scannedFiles);
                List<(string fullPath, string relativePath)> resolvedFiles;

                if (conflicts.Count > 0)
                {
                    Logger.Log($"ファイル名の衝突を検出: {conflicts.Count}グループ");

                    // 競合ダイアログを表示
                    var (result, selectedFiles) = await ConflictDialogImpl.ShowFromBackgroundAsync(conflicts, progressWindow, isTwoPane: false);
                    if (result == Models.FileConflictResult.Cancel)
                    {
                        Logger.Log("ユーザーが競合解決をキャンセルしました");
                        return false;
                    }

                    // 衝突しなかったファイル + ダイアログで選択されたファイルをマージ
                    var conflictingPaths = new HashSet<string>(
                        conflicts.SelectMany(g => g.Entries.Select(e => e.FullPath)),
                        StringComparer.OrdinalIgnoreCase);
                    resolvedFiles = scannedFiles
                        .Where(f => !conflictingPaths.Contains(f.fullPath))
                        .Concat(selectedFiles)
                        .ToList();
                }
                else
                {
                    resolvedFiles = scannedFiles;
                }

                IProgress<ProgressInfo> progress = new Progress<ProgressInfo>(info =>
                {
                    UiDispatcherImpl.Post(() => progressWindow?.UpdateProgress(info.Percentage));
                });

                // 圧縮前のディスク容量チェック
                var estimatedMergeSize = resolvedFiles.Sum(f =>
                {
                    try { return File.Exists(f.fullPath) ? new FileInfo(f.fullPath).Length : 0L; }
                    catch { return 0L; }
                });
                if (estimatedMergeSize > 0)
                {
                    var hasSpace = await DiskSpaceChecker.EnsureDiskSpaceAsync(
                        outputPath, estimatedMergeSize, progressWindow, actualCancellationToken);
                    if (!hasSpace)
                        throw new OperationCanceledException(App.Text("Error.DiskSpaceCancelled"));
                }

                // 解決済みリストが空の場合はスキップ（全ファイルが未選択）
                if (resolvedFiles.Count == 0)
                {
                    Logger.Log("まとめ圧縮: 解決済みファイルが0件のためスキップ");
                    return false;
                }

                // 解決済みリストで圧縮
                var parsedFormat = ArchiveCompressor.ParseFormat(format);
                await ArchiveCompressor.CompressFilesAsync(sourcePaths, outputPath, parsedFormat, progress, actualCancellationToken, resolvedFiles, settingsOverride: settings);

                Logger.Log($"まとめ圧縮完了: {outputPath}（{resolvedFiles.Count}個のファイル）");

                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogException("まとめ圧縮でエラーが発生", ex);
                // 進捗ウィンドウを先に閉じてから await でダイアログ表示完了を待つ
                if (closeWindowOnCompletion)
                {
                    progressWindow?.CloseSafe();
                }
                await UiDispatcherImpl.InvokeAsync(() => MessageServiceImpl.ShowError(App.Text("Error.DuringCompression", ex.Message)));
                return false;
            }
            finally
            {
                if (closeWindowOnCompletion)
                    progressWindow?.CloseSafe();
            }
        }, actualCancellationToken);
    }

    /// <summary>
    /// エラー回復ダイアログを表示
    /// </summary>
    /// <param name="failedFile">失敗したファイル情報</param>
    /// <param name="parentWindow">親ウィンドウ</param>
    /// <returns>選択されたエラー処理オプション</returns>
    private static async Task<PartialExtractionHandler.ErrorHandlingOption> ShowErrorRecoveryDialogAsync(
        PartialExtractionHandler.FailedFileInfo failedFile,
        ProgressWindow? parentWindow)
    {
        try
        {
            var errorInfo = new ArchiveErrorInfo
            {
                ErrorType = failedFile.ErrorType,
                Message = failedFile.ErrorMessage,
                Details = App.Text("Extraction.ErrorFile", failedFile.FilePath, failedFile.ErrorMessage),
                ProblematicFilePath = failedFile.FilePath,
                RecommendedAction = failedFile.IsRecoverable ? App.Text("Extraction.RetryOrSkip") : App.Text("ErrorHandler.UnexpectedAction"),
                IsRecoverable = failedFile.IsRecoverable
            };

            if (parentWindow != null)
            {
                return await UiDispatcherImpl.InvokeAsync(async () =>
                {
                    var dialog = new ErrorRecoveryDialog(errorInfo);
                    var option = await dialog.ShowDialog<PartialExtractionHandler.ErrorHandlingOption?>(parentWindow);
                    return option ?? PartialExtractionHandler.ErrorHandlingOption.StopOnError;
                });
            }
            return PartialExtractionHandler.ErrorHandlingOption.SkipOnError;
        }
        catch (Exception ex)
        {
            Logger.Log($"エラー回復ダイアログの表示でエラーが発生: {ex.Message}");
            return PartialExtractionHandler.ErrorHandlingOption.SkipOnError;
        }
    }
}
