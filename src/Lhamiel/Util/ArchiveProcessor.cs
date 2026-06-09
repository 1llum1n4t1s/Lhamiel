#pragma warning disable CS0618 // PartialExtractionHandler は [Obsolete] だが移行完了まで使用（参照が複数メソッドに分散するためファイルレベルで抑制）
using Avalonia.Controls;
using Lhamiel.View;
namespace Lhamiel.Util;

/// <summary>
/// 圧縮パスワード解決結果（<see cref="ArchiveProcessor.TryResolveCompressionPasswordAsync"/> の戻り値）。
/// <para>
/// <see cref="RedactionScope"/> は <see cref="Logger.RegisterRedactionToken"/> の戻り IDisposable を保持しており、
/// このオブジェクト自体を <c>using</c> で受けると、解決直後から後段の log 経路 (削除確認・ディスク容量・scan 等) で
/// 平文パスワードが自動マスクされる (CodeRabbit #3381138424 対応)。
/// </para>
/// </summary>
internal sealed record PasswordResolutionState(string? Password, bool EncryptFileNames) : IDisposable
{
    internal IDisposable? RedactionScope { get; init; }

    public void Dispose() => RedactionScope?.Dispose();
}

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
    internal static IPasswordDialogService PasswordDialogImpl { get; set; } = new DefaultPasswordDialogService();

    /// <summary>
    /// 設定の <see cref="Settings.IsPasswordProtectionEnabled"/> / <see cref="Settings.PasswordMode"/> /
    /// <see cref="Settings.EncryptedCompressionPassword"/> を元に圧縮パスワードを解決する。
    /// <para>
    /// 戻り値:
    /// <list type="bullet">
    /// <item><description>保護 OFF: <see cref="PasswordResolutionState"/>(Password=null, EncryptFileNames=false)。</description></item>
    /// <item><description>保護 ON で解決成功: <see cref="PasswordResolutionState"/>(Password=平文, EncryptFileNames=設定値)。</description></item>
    /// <item><description>保護 ON でユーザーキャンセル: <c>null</c>。</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <c>PasswordMode="Remember"</c> + 保存済み ciphertext あり: DPAPI 復号を試み、成功すればそれを使う。
    /// 失敗 (別ユーザー/PC コピー等) や ciphertext 未保存のときは <see cref="PasswordDialogMode.CompressNew"/>
    /// で再プロンプトし、入力された平文を新たな ciphertext として永続化する。
    /// </para>
    /// <para>
    /// <c>PasswordMode="PromptEachTime"</c>: 毎回 <see cref="PasswordDialogMode.CompressNew"/> で入力。
    /// 設定は変更しない。
    /// </para>
    /// </summary>
    internal static async Task<PasswordResolutionState?> TryResolveCompressionPasswordAsync(
        Settings settings,
        string archiveDisplayName,
        Window? parentWindow,
        CancellationToken cancellationToken,
        string? formatHint = null)
    {
        if (!settings.IsPasswordProtectionEnabled)
            return new PasswordResolutionState(null, false);

        // TAR はパスワード保護非対応。ここに来る時点で IsPasswordProtectionEnabled=true なので、
        // 「保護要求 + TAR」という矛盾状態。サイレントに 'password なし' へダウングレードすると、
        // ユーザーが暗号化されたと思い込んだまま無保護の TAR が生成される footgun になる
        // (CodeRabbit 指摘 / CLAUDE.md「TAR は InvalidOperationException guard」)。
        // ArchiveCompressor.CreateArchiveWriter と同じ Error.PasswordNotSupportedByFormat で fail-loud にする。
        // UI のドロップ経路は MainWindowViewModel で TAR 選択時に IsPasswordProtectionEnabled を
        // 強制 false にするためここには到達せず、CLI/設定直書き等の非 UI 経路でのみ発火する。
        // この例外は MainWindowViewModel の圧縮 try/catch (Error.ProcessFiles) で捕捉されダイアログ表示される
        // (codex P2 #3381313186 のサイレント解除を fail-loud へ是正)。
        if (formatHint is { } fmt && string.Equals(fmt, "TAR", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(App.Text("Error.PasswordNotSupportedByFormat", "TAR"));

        var encryptFileNames = settings.EncryptFileNames;
        string? plaintext;

        if (string.Equals(settings.PasswordMode, "Remember", StringComparison.Ordinal))
        {
            // 保存済み ciphertext があれば復号を試行
            plaintext = CompressionPasswordSession.TryUnprotect(settings.EncryptedCompressionPassword);
            if (plaintext is null)
            {
                // 復号失敗 (別ユーザー/PC コピー等) → ユーザーに通知して再プロンプト。
                // ciphertext 未保存 (初回 Remember 利用) との区別はユーザー視点では不要なので
                // 通知は ciphertext があった場合のみ表示する。
                if (settings.EncryptedCompressionPassword is { Length: > 0 })
                {
                    await UiDispatcherImpl.InvokeAsync(() =>
                        MessageServiceImpl.ShowError(App.Text("Notify.SavedPasswordDecryptFailed")));
                }

                plaintext = await PasswordDialogImpl.PromptForPasswordAsync(
                    archiveDisplayName, PasswordDialogMode.CompressNew, isRetry: false, parentWindow, cancellationToken);
                if (plaintext is null) return null; // user cancelled

                // 新パスワードを DPAPI 暗号化して永続化 (Remember モードの初回保存 / 再設定)。
                // 保存失敗時は圧縮自体は継続する (UI/UX 上、パスワード保護はあくまでオプション機能なので)。
                try
                {
                    var ciphertext = CompressionPasswordSession.Protect(plaintext);
                    // codex P2 #3384569058: ダイアログ表示中に設定パネルで PromptEachTime へ
                    // 切替・保護 OFF された場合は保存しない。PasswordDialog の ShowDialog は
                    // owner (進捗ウィンドウ) だけを無効化し MainWindow は操作可能なため、
                    // snapshot 時点の Remember 判定と live 設定が乖離しうる。MutateAndSave は
                    // _lock 内で mutator を実行するので、mutator 内の再チェックで「mode 確認 →
                    // 保存」が atomic になる (AutoSave の Mutate とも直列化)。保存しなかった
                    // 場合も今回の圧縮自体は入力されたパスワードで継続する (PromptEachTime の
                    // 意味論と一致)。
                    var saved = false;
                    SettingsManager.Instance.MutateAndSave(s =>
                    {
                        if (s.IsPasswordProtectionEnabled
                            && string.Equals(s.PasswordMode, "Remember", StringComparison.Ordinal))
                        {
                            s.EncryptedCompressionPassword = ciphertext;
                            saved = true;
                        }
                    });
                    if (saved)
                    {
                        // codex P2 #3382276703: 設定パネルの「設定済 / 未設定」表示と
                        // 「Clear」ボタンの enable 状態を即時更新する。
                        // MainWindowViewModel.HasSavedPassword / SavedPasswordStatusText は
                        // SettingsManager.Current 直読みのため、PropertyChanged を明示発火しないと
                        // 次回起動まで UI が古い (Remember 初回保存後も「未設定」のまま、Clear 不可)。
                        ViewModels.MainWindowViewModel.RaiseSavedPasswordExternallyChanged();
                    }
                    else
                    {
                        Logger.Log("パスワード入力中に Remember モードが解除されたため保存をスキップ (今回の圧縮には使用)", LogLevel.Info);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"パスワードの DPAPI 暗号化保存に失敗 (圧縮は継続): {ex.Message}", LogLevel.Warning);
                }
            }
        }
        else
        {
            // PromptEachTime: 毎回入力。設定は変更しない。
            plaintext = await PasswordDialogImpl.PromptForPasswordAsync(
                archiveDisplayName, PasswordDialogMode.CompressNew, isRetry: false, parentWindow, cancellationToken);
            if (plaintext is null) return null; // user cancelled
        }

        // 平文を解決した瞬間にログ redaction を発火 (CodeRabbit #3381138424)。
        // 後段の log (削除確認・scan・ディスク容量・圧縮実行・後処理) を全て保護する。
        // 戻り値の PasswordResolutionState は IDisposable で、using 解放時に refcount が 1 減る
        // (refcount 化済みなので CompressFilesAsync 内側の using と重複しても安全)。
        return new PasswordResolutionState(plaintext, encryptFileNames)
        {
            RedactionScope = Logger.RegisterRedactionToken(plaintext),
        };
    }

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
            await MessageServiceImpl.ShowError(App.Text("Error.FileNotFound", filePath));
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
                    await UiDispatcherImpl.InvokeAsync(() => MessageServiceImpl.ShowError(App.Text("Error.UnsupportedFormat", extension)));
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
                        structureInfo.TotalUncompressedSize,
                        snapshot.NormalizeUnicodeFileNames);

                    // 展開後 CRC 整合性検証（設定で有効な場合のみ）
                    if (snapshot.VerifyAfterExtraction)
                    {
                        UiDispatcherImpl.Post(() => progressWindow?.SetIndeterminate(App.Text("Progress.VerifyingIntegrity")));
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
                        UiDispatcherImpl.Post(() => progressWindow?.SetIndeterminate(App.Text("Progress.ApplyingSecurityMark")));
                        var zoneId = MotwPropagator.ReadZoneIdentifier(filePath);
                        if (zoneId != null)
                        {
                            var capturedOutputPath = outputPath;
                            var capturedRootNames = structureInfo.RootItemNames;
                            var capturedNormalize = snapshot.NormalizeUnicodeFileNames;
                            await Task.Run(() =>
                            {
                                var normalizedMotwBase = ArchiveExtractor.NormalizeBaseDirectory(capturedOutputPath);
                                foreach (var rootName in capturedRootNames)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    if (!ArchiveExtractor.TryResolveSafeEntryPathFromNormalized(
                                            normalizedMotwBase, rootName, out var rootItemPath, capturedNormalize))
                                    {
                                        Logger.Log($"MotW 伝播で境界外パスを検出しスキップ: {rootName}", LogLevel.Warning);
                                        continue;
                                    }
                                    if (Directory.Exists(rootItemPath))
                                        MotwPropagator.PropagateToDirectory(rootItemPath, zoneId, cancellationToken);
                                    else if (File.Exists(rootItemPath))
                                        MotwPropagator.TryWriteZoneIdentifier(rootItemPath, zoneId);
                                }
                            }, cancellationToken);
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
            await UiDispatcherImpl.InvokeAsync(() => MessageServiceImpl.ShowError(App.Text("Error.DuringExtraction", ex.Message)));

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
    /// <param name="resolvedPasswordState">バッチ呼び出し側で解決済みのパスワード状態。<c>null</c> なら内部で <see cref="TryResolveCompressionPasswordAsync"/> を呼んで解決する（単発呼び出し時の経路）。</param>
    /// <returns>処理が成功した場合はtrue、そうでなければfalse</returns>
    internal static async Task<bool> CompressItemAsync(string sourcePath, string outputDir, bool outputToSameDirectory, string format, ProgressWindow? progressWindow, IProgress<ProgressInfo>? progressReporter = null, CancellationToken cancellationToken = default, bool closeWindowOnCompletion = true, string? overrideOutputPath = null, Settings? settingsSnapshot = null, PasswordResolutionState? resolvedPasswordState = null)
    {
        Logger.Log($"ArchiveProcessor.CompressItemAsync開始: sourcePath={sourcePath}, outputDir={outputDir}, outputToSameDirectory={outputToSameDirectory}, format={format}");

        // 対象の存在確認（軽量なチェックはUIスレッドで実施）
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
        {
            Logger.Log($"指定された対象が存在しません: {sourcePath}");
            await MessageServiceImpl.ShowError(App.Text("Error.FolderNotFound", sourcePath));
            return false;
        }

        // 圧縮形式の確認
        if (!ArchiveCompressor.WritableFormats.Contains(format))
        {
            Logger.Log($"サポートされていない圧縮形式です: {format}");
            await MessageServiceImpl.ShowError(App.Text("Error.UnsupportedCompression", format));
            return false;
        }

        // ProgressWindow のキャンセルと呼び出し元のキャンセルを両方尊重するためリンクする。
        // 旧実装は progressWindow!=null のとき引数の cancellationToken を無視していた。
        using var linkedCts = progressWindow != null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progressWindow.GetCancellationToken())
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var actualCancellationToken = linkedCts.Token;

        // catch/finally から見えるよう、上書き判定 / temp パスを Task.Run 外スコープで宣言する。
        // 圧縮成功時の atomic swap (codex P1 #3381582647) と例外時の temp 削除に必要。
        var outputPath = overrideOutputPath ?? ArchiveCompressor.GetCompressedFileName(sourcePath, format, outputDir, outputToSameDirectory);
        var targetExists = File.Exists(outputPath) || Directory.Exists(outputPath);
        var tempOutputPath = outputPath;

        // 重い処理全体を Task.Run でバックグラウンドへ移動
        return await Task.Run(async () =>
        {
            // codex P2 #3381905952: redaction scope を try/catch の外で保持する。
            // using var を try 内に置くと unwinding 時 (catch 到達前) に Dispose が走り、
            // catch 内 LogException でライブラリ例外メッセージ中の password 平文が漏れる。
            // 自分が解決した場合のみ dispose 責任を持つ (バッチ親由来は親が保持)。
            PasswordResolutionState? passwordStateForCleanup = null;
            try
            {
                Logger.Log($"圧縮処理を開始: {sourcePath}");

                // 設定スナップショットを Task.Run の先頭で 1 回だけ取って以降の race を防ぐ。
                // バッチから渡された settingsSnapshot があれば再利用 (ロック競合回避)。
                // パスワード解決にも使うため early に確保する必要がある。
                var settings = settingsSnapshot ?? SettingsManager.Instance.CreateSnapshot();

                // 上書き対象の存在は事前判定済み (targetExists)。
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
                }

                // パスワード解決 (バッチからの override がなければ内部で解決)。
                // 既存ファイル削除より前に行うことが重要: ここでキャンセルされたとき
                // 既に上書き対象を消した状態だと「元ファイルも新ファイルも無い」状態になる。
                // CodeRabbit/codex P1 指摘 #3381085172 対応。
                var ownsPasswordState = resolvedPasswordState is null;
                var passwordState = resolvedPasswordState
                    ?? await TryResolveCompressionPasswordAsync(settings, Path.GetFileName(outputPath), progressWindow, actualCancellationToken, format);
                if (passwordState is null)
                {
                    Logger.Log("ユーザーがパスワード入力をキャンセルしたため圧縮を中止します");
                    return false;
                }
                // 自分が解決した場合のみ Dispose 責任を持つ (バッチ親由来は親が using で保持)。
                // CodeRabbit #3381138424 + codex #3381905952: try 外スコープに保存して
                // catch 内 LogException が redaction 適用中に走るようにする。
                passwordStateForCleanup = ownsPasswordState ? passwordState : null;

                // 上書き対象が「保護されたパス」(shell folder / ドライブルート 等) の場合は事前拒否。
                // 実際の削除は CompressFilesAsync 成功直前まで遅らせる (codex P1 #3381582647) ので、
                // ここでは「削除可否の事前バリデーション」だけ行う。
                if (targetExists && PathValidator.IsProtectedDirectory(outputPath))
                {
                    Logger.Log($"圧縮上書き: 保護されたパスへの削除を拒否: {outputPath}", LogLevel.Warning);
                    throw new InvalidOperationException(App.Text("Error.ProtectedDirectory", outputPath));
                }

                // 既存ファイルを失わないため、圧縮は一時パスに対して行い、成功時に atomic swap する
                // (codex P1 #3381582647: 旧パスに直接書くと addedCount==0 早期 throw 等で既存が消える)。
                if (targetExists)
                {
                    tempOutputPath = outputPath + ".lhamiel-tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
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
                // settings はメソッド冒頭で確保済み。
                List<(string fullPath, string relativePath)>? resolvedFiles = null;
                if (settings.DirectoryStructureMode == DirectoryStructureMode.Flat && Directory.Exists(sourcePath))
                {
                    // 除外パターンは .lhaignore（gitignore 互換）から圧縮実行毎に読み直す。
                    // RespectNestedGitignore=true なら各サブツリーの .gitignore も layered matcher として合成する。
                    var lhaignoreLines = LhaignoreFile.ReadLines();
                    var ignoreMatcher = GitignoreMatcher.Compile(lhaignoreLines);
                    var scannedFiles = await ArchiveCompressor.ScanSourceFiles(
                        [sourcePath],
                        ignoreMatcher,
                        actualCancellationToken,
                        dirModeOverride: settings.DirectoryStructureMode,
                        normalizeUnicodeOverride: settings.NormalizeUnicodeFileNames,
                        includeHiddenAndSystemEntriesOverride: settings.IncludeHiddenAndSystemEntries,
                        respectNestedGitignore: settings.RespectNestedGitignore,
                        globalIgnoreLines: lhaignoreLines);

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

                await ArchiveCompressor.CompressFilesAsync(
                    [sourcePath], tempOutputPath, parsedFormat, compressionProgress, actualCancellationToken,
                    resolvedFiles, settingsOverride: settings,
                    password: passwordState.Password, encryptFileNames: passwordState.EncryptFileNames);

                // atomic swap: 圧縮が成功して初めて既存ファイルを破壊する
                if (targetExists && !string.Equals(tempOutputPath, outputPath, StringComparison.OrdinalIgnoreCase))
                {
                    // codex P2 #3382065860: 既存削除→Move の途中で Move が AV ロック等で失敗すると
                    // 既存が永久に失われる。バックアップ rename を挟んで、Move 失敗時に restore する。
                    string? backupPath = null;
                    try
                    {
                        backupPath = outputPath + ".lhamiel-bak-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                        if (Directory.Exists(outputPath)) Directory.Move(outputPath, backupPath);
                        else if (File.Exists(outputPath)) File.Move(outputPath, backupPath);
                        File.Move(tempOutputPath, outputPath);
                        // 成功: バックアップを削除
                        try
                        {
                            if (File.Exists(backupPath)) File.Delete(backupPath);
                            else if (Directory.Exists(backupPath)) Directory.Delete(backupPath, true);
                        }
                        catch (Exception cleanupEx)
                        {
                            Logger.Log($"バックアップ削除に失敗 (圧縮成功): {backupPath} ({cleanupEx.Message})", LogLevel.Warning);
                        }
                        Logger.Log($"既存対象を圧縮成功後に置き換えました: {outputPath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"atomic swap 失敗: {tempOutputPath} -> {outputPath} ({ex.Message})", LogLevel.Warning);
                        // バックアップを元に戻す best-effort restore (round 6 adversarial: partial Move 残骸を考慮)。
                        //
                        // Adversarial シナリオ: `File.Move(temp, outputPath)` が途中で例外を投げた場合、
                        // outputPath には残骸 (部分的に作成されたファイル) が残る可能性がある。
                        // 単純に `!File.Exists(outputPath)` で restore をスキップすると、bak だけ残って outputPath は壊れた状態。
                        // 残骸を先に削除してから bak から restore する。
                        try
                        {
                            if (backupPath is not null)
                            {
                                // 残骸削除を試みる (best-effort)
                                try
                                {
                                    if (File.Exists(outputPath)) File.Delete(outputPath);
                                    else if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
                                }
                                catch (Exception partialEx)
                                {
                                    Logger.Log($"swap 失敗時の残骸削除失敗: {outputPath} ({partialEx.Message})", LogLevel.Warning);
                                }
                                // 残骸削除に成功した場合のみ restore (残骸が残ったまま上書き move は失敗するので)
                                if (!File.Exists(outputPath) && !Directory.Exists(outputPath))
                                {
                                    if (File.Exists(backupPath)) File.Move(backupPath, outputPath);
                                    else if (Directory.Exists(backupPath)) Directory.Move(backupPath, outputPath);
                                }
                                else
                                {
                                    Logger.Log($"バックアップ復元不能: outputPath に残骸が残存、bak={backupPath} を維持", LogLevel.Error);
                                }
                            }
                        }
                        catch (Exception restoreEx)
                        {
                            Logger.Log($"バックアップ復元失敗: {backupPath} -> {outputPath} ({restoreEx.Message})", LogLevel.Error);
                        }
                        try { if (File.Exists(tempOutputPath)) File.Delete(tempOutputPath); } catch { /* best-effort */ }
                        throw new InvalidOperationException(App.Text("Error.FileLocked", Path.GetFileName(outputPath)), ex);
                    }
                }

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
                // atomic swap 用 temp ファイルが残っていれば削除 (codex P1 #3381582647)
                try { if (targetExists && !string.Equals(tempOutputPath, outputPath, StringComparison.OrdinalIgnoreCase) && File.Exists(tempOutputPath)) File.Delete(tempOutputPath); } catch { /* best-effort */ }
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
                // OperationCanceledException 経路の温存も含めた最終クリーンアップ
                try { if (targetExists && !string.Equals(tempOutputPath, outputPath, StringComparison.OrdinalIgnoreCase) && File.Exists(tempOutputPath)) File.Delete(tempOutputPath); } catch { /* best-effort */ }
                // redaction scope を最後に解放 (catch 内 LogException 実行後に Dispose されるよう保証)。
                passwordStateForCleanup?.Dispose();
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
        // codex P2 #3381905952: catch 内 LogException が redaction 適用中に走るよう、
        // batchPasswordState を try/catch 外スコープで保持する。
        PasswordResolutionState? batchPasswordForCleanup = null;
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

            // パスワード解決をバッチ単位で 1 回だけ行う。
            // 「ドロップごとに確認」モードでも 1 ドロップ操作 = 1 バッチなので 1 回の入力で済む。
            // ユーザーがキャンセルしたら全バッチをキャンセル。
            // 表示名は先頭ファイル + "（他 N 件）" を仮で渡し、ダイアログ側でアーカイブ名表示として使う。
            var firstArchiveName = Path.GetFileName(resolvedOutputPaths[0]);
            var batchDisplayName = totalCount > 1
                ? $"{firstArchiveName} (+{totalCount - 1})"
                : firstArchiveName;
            var batchPasswordState = await TryResolveCompressionPasswordAsync(
                sharedSettings, batchDisplayName, progressWindow, actualCancellationToken, format);
            if (batchPasswordState is null)
            {
                Logger.Log("バッチ圧縮: ユーザーがパスワード入力をキャンセルしたため中止します");
                if (closeWindowOnCompletion) progressWindow?.CloseSafe();
                return false;
            }
            // バッチ全体で redaction scope を保持。
            // 各 CompressItemAsync(resolvedPasswordState: batchPasswordState) は親が dispose する前提で
            // 自分では dispose しない (PasswordResolutionState IDisposable、CodeRabbit #3381138424)。
            // codex #3381905952: try 外スコープに保存 → finally で Dispose する。
            batchPasswordForCleanup = batchPasswordState;

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

                    // 事前計算された出力パスを使用して圧縮処理を実行（共有スナップショット + バッチ解決済みパスワードを再利用）
                    var success = await CompressItemAsync(sourcePath, outputDir, outputToSameDirectory, format, progressWindow, innerProgress, actualCancellationToken, closeWindowOnCompletion: false, overrideOutputPath: resolvedOutputPaths[index], settingsSnapshot: sharedSettings, resolvedPasswordState: batchPasswordState);

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
            await UiDispatcherImpl.InvokeAsync(() => MessageServiceImpl.ShowError(App.Text("Error.DuringCompression", ex.Message)));

            // 例外発生時にも確実にクリーンアップ
            if (closeWindowOnCompletion)
            {
                progressWindow?.CloseSafe();
            }

            return false;
        }
        finally
        {
            // catch 内 LogException 完了後に redaction を解除する (codex #3381905952)
            batchPasswordForCleanup?.Dispose();
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
            await MessageServiceImpl.ShowError(App.Text("Error.UnsupportedCompression", format));
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

        // ProgressWindow のキャンセルと呼び出し元キャンセルを両方尊重するためリンクする
        // (CodeRabbit #3381138436: 旧実装は progressWindow != null のとき外部 cancellationToken を無視していた)。
        using var linkedCts = progressWindow != null
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, progressWindow.GetCancellationToken())
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var actualCancellationToken = linkedCts.Token;

        // catch/finally から見えるよう、temp パスを Task.Run 外スコープで宣言する (codex P1 #3381582647)。
        var targetExists = File.Exists(outputPath);
        var tempMergedOutputPath = outputPath;

        return await Task.Run(async () =>
        {
            // codex P2 #3381905952: redaction scope を try/catch の外で保持して、catch 内
            // LogException 実行中も平文 password を mask し続ける。
            PasswordResolutionState? mergedPasswordForCleanup = null;
            try
            {
                // 設定は処理開始時点でスナップショット化して以降の race を避ける。
                var settings = SettingsManager.Instance.CreateSnapshot();

                // 上書き対象の存在は事前判定済み (targetExists)。
                if (targetExists)
                {
                    var canOverwrite = await ConflictDialogImpl.CanOverwriteFromBackgroundAsync(sourcePaths[0], outputPath, progressWindow);
                    if (!canOverwrite)
                    {
                        Logger.Log("ユーザーがまとめ圧縮をキャンセルしました");
                        return false;
                    }
                }

                // パスワード解決 (まとめ圧縮: 出力アーカイブ 1 個に対して 1 回プロンプト)。
                // 既存ファイル削除より前に行う (codex P1 #3381085172)。
                var mergedPasswordState = await TryResolveCompressionPasswordAsync(
                    settings, Path.GetFileName(outputPath), progressWindow, actualCancellationToken, format);
                if (mergedPasswordState is null)
                {
                    Logger.Log("まとめ圧縮: ユーザーがパスワード入力をキャンセルしました");
                    return false;
                }
                // 後段の全 log を redaction 保護下に置く (CodeRabbit #3381138424)。
                // codex P2 #3381905952: try 外スコープに保存 → finally で Dispose、
                // catch 内 LogException 実行時もマスクが効くようにする。
                mergedPasswordForCleanup = mergedPasswordState;

                // 既存ファイルは atomic swap 直前まで残す (codex P1 #3381582647)。
                if (targetExists)
                {
                    tempMergedOutputPath = outputPath + ".lhamiel-tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                }

                // ファイルリストをスキャン。
                // 除外パターンは .lhaignore（gitignore 互換）から圧縮実行毎に読み直す。
                // RespectNestedGitignore=true なら各サブツリーの .gitignore も layered matcher として合成する。
                var lhaignoreLines = LhaignoreFile.ReadLines();
                var ignoreMatcher = GitignoreMatcher.Compile(lhaignoreLines);
                var scannedFiles = await ArchiveCompressor.ScanSourceFiles(
                    sourcePaths.ToList(), ignoreMatcher, actualCancellationToken,
                    dirModeOverride: settings.DirectoryStructureMode,
                    normalizeUnicodeOverride: settings.NormalizeUnicodeFileNames,
                    includeHiddenAndSystemEntriesOverride: settings.IncludeHiddenAndSystemEntries,
                    respectNestedGitignore: settings.RespectNestedGitignore,
                    globalIgnoreLines: lhaignoreLines);

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

                // 解決済みリストで圧縮 (一時パスに書く)
                var parsedFormat = ArchiveCompressor.ParseFormat(format);
                await ArchiveCompressor.CompressFilesAsync(
                    sourcePaths, tempMergedOutputPath, parsedFormat, progress, actualCancellationToken,
                    resolvedFiles, settingsOverride: settings,
                    password: mergedPasswordState.Password, encryptFileNames: mergedPasswordState.EncryptFileNames);

                // atomic swap (codex P1 #3381582647 / P2 #3382065860)
                if (targetExists && !string.Equals(tempMergedOutputPath, outputPath, StringComparison.OrdinalIgnoreCase))
                {
                    string? backupPath = null;
                    try
                    {
                        backupPath = outputPath + ".lhamiel-bak-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                        if (File.Exists(outputPath)) File.Move(outputPath, backupPath);
                        File.Move(tempMergedOutputPath, outputPath);
                        try { if (File.Exists(backupPath)) File.Delete(backupPath); } catch (Exception ce) { Logger.Log($"バックアップ削除に失敗: {backupPath} ({ce.Message})", LogLevel.Warning); }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"まとめ圧縮 atomic swap 失敗: {tempMergedOutputPath} -> {outputPath} ({ex.Message})", LogLevel.Warning);
                        // round 6 adversarial: partial Move 残骸を先に削除してから bak restore (CompressItemAsync と同じ)
                        try
                        {
                            if (backupPath is not null)
                            {
                                try { if (File.Exists(outputPath)) File.Delete(outputPath); }
                                catch (Exception partialEx) { Logger.Log($"swap 失敗時の残骸削除失敗: {outputPath} ({partialEx.Message})", LogLevel.Warning); }
                                if (!File.Exists(outputPath) && File.Exists(backupPath))
                                    File.Move(backupPath, outputPath);
                                else if (File.Exists(outputPath))
                                    Logger.Log($"バックアップ復元不能: outputPath に残骸が残存、bak={backupPath} を維持", LogLevel.Error);
                            }
                        }
                        catch (Exception restoreEx)
                        {
                            Logger.Log($"バックアップ復元失敗: {backupPath} -> {outputPath} ({restoreEx.Message})", LogLevel.Error);
                        }
                        try { if (File.Exists(tempMergedOutputPath)) File.Delete(tempMergedOutputPath); } catch { /* best-effort */ }
                        throw new InvalidOperationException(App.Text("Error.FileLocked", Path.GetFileName(outputPath)), ex);
                    }
                }

                Logger.Log($"まとめ圧縮完了: {outputPath}（{resolvedFiles.Count}個のファイル）");

                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogException("まとめ圧縮でエラーが発生", ex);
                try { if (targetExists && !string.Equals(tempMergedOutputPath, outputPath, StringComparison.OrdinalIgnoreCase) && File.Exists(tempMergedOutputPath)) File.Delete(tempMergedOutputPath); } catch { /* best-effort */ }
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
                // catch 内 LogException 完了後に redaction を解除する (codex P2 #3381905952)
                mergedPasswordForCleanup?.Dispose();
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
