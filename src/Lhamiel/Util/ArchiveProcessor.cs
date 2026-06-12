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
    /// ヘッダ暗号化 (he=on) 構造解析のパスワードプロンプトをプロセス全体で 1 つに直列化するゲート。
    /// バッチ展開は IoBoundParallelism (2〜4) 並列でタスクを回すため、複数の he=on アーカイブが
    /// 同時に構造解析へ到達するとモーダルダイアログが積み重なる (codex P2 #3385210128)。
    /// 展開中の AsyncPasswordQuery が NativeArchiveGate で自然に直列化されるのと同等の体験に揃える。
    /// NativeArchiveGate 自体は GetArchiveStructureInfo 内部で取得される (非リエントラント) ため
    /// 流用できない。取得順は常に「本ゲート → NativeArchiveGate (一時取得)」の一方向のみで、
    /// 逆順取得は存在しないためデッドロックしない。
    /// </summary>
    private static readonly SemaphoreSlim StructurePasswordPromptGate = new(1, 1);

    /// <summary>
    /// 展開系パスワードダイアログの「表示そのもの」をプロセス全体で 1 つに直列化する葉ゲート。
    /// 構造解析プロンプト (本クラス) と展開中の AsyncPasswordQuery プロンプト (ArchiveExtractor)
    /// は別経路のため、StructurePasswordPromptGate だけでは he=on アーカイブと通常の暗号化
    /// アーカイブが混在するバッチでモーダルが積み重なる (codex P2 #3386575715)。
    /// 取得規約: <b>保持中に他のゲートを取得しない (葉)</b>。取得順は
    /// StructurePasswordPromptGate → NativeArchiveGate → 本ゲート の一貫階層
    /// (構造プロンプトはダイアログ後に本ゲートを解放してから NativeArchiveGate 配下の再解析へ、
    /// 展開中プロンプトは NativeArchiveGate 保持中に本ゲートを取得) のためデッドロックしない。
    /// </summary>
    internal static readonly SemaphoreSlim ExtractionPasswordDialogGate = new(1, 1);

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

        // TAR はパスワード保護非対応。明示的に TAR が要求された場合は password 解決を
        // スキップして「保護なし」で続行する (codex P2 #3384620480)。
        //
        // ここは fail-loud (throw) にしない: UI は TAR 選択時に checkbox を disable して
        // 「TAR に保護は適用されない」ことを明示しており、保存済みの IsPasswordProtectionEnabled
        // は ZIP/7z 用の選好にすぎない。シェル/CLI の明示 `--format TAR` は
        // CompressItemAsync/CompressMergedAsync が新規 snapshot (= ZIP/7z 選好の保護 ON) を
        // 取るためこの分岐に必ず到達し、throw すると「ZIP の保護設定を OFF にしないと
        // TAR 圧縮できない」誤爆になる。coerce が UI ドロップ経路 (VM での強制 false) と
        // 同じ意味論。
        //
        // 「暗号化されたつもりの無保護 TAR」footgun への防御線は
        // ArchiveCompressor.CreateArchiveWriter の「非 null password + TAR → InvalidOperationException」
        // が担う (こちらは本物のバグ検知用で fail-loud を維持)。
        if (formatHint is { } fmt && string.Equals(fmt, "TAR", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Log("TAR はパスワード保護非対応のため、保護設定をスキップして圧縮を続行します", LogLevel.Info);
            return new PasswordResolutionState(null, false);
        }

        var encryptFileNames = settings.EncryptFileNames;
        // 同梱 7-Zip 26.00 は ZIP 作成時に非 ASCII パスワードを E_INVALIDARG で拒否する
        // (upstream regression、ライブラリ CLAUDE.md の既知問題。7z は非 ASCII でも正常動作)。
        // ZIP のときはここで検証して再プロンプトし、ネイティブの不透明な失敗まで進ませない。
        var isZip = formatHint is { } zipFmt && string.Equals(zipFmt, "ZIP", StringComparison.OrdinalIgnoreCase);
        string? plaintext;

        if (string.Equals(settings.PasswordMode, "Remember", StringComparison.Ordinal))
        {
            // 保存済み ciphertext があれば復号を試行
            plaintext = CompressionPasswordSession.TryUnprotect(settings.EncryptedCompressionPassword);

            // 復号できても redaction 下限 (= MinCompressPasswordLength) 未満の保存値は使用しない
            // (codex P2 #3390183195)。4 文字フロア導入前のビルドで保存された legacy 値が対象。
            // Logger.RegisterRedactionToken は 4 文字未満で no-op のため、このまま圧縮スコープに
            // 入るとライブラリ例外経由で平文がログに残りうる。復号失敗と同様に通知 +
            // CompressNew 再プロンプト (ダイアログが 4 文字以上を強制) し、下の保存パスで
            // 新しい値に移行する (保存値の上書き)。
            var savedTooShort = plaintext is not null && plaintext.Length < View.PasswordDialog.MinCompressPasswordLength;
            if (savedTooShort)
            {
                plaintext = null;
                await UiDispatcherImpl.InvokeAsync(() =>
                    MessageServiceImpl.ShowError(
                        App.Text("Notify.SavedPasswordTooShort", View.PasswordDialog.MinCompressPasswordLength)));
            }

            // 保存済みパスワードが復号できても、ZIP + 非 ASCII は使用不能 (7-Zip 26.00 regression)。
            // 7z では引き続き有効なパスワードなので保存値は変更せず、
            // この圧縮限りの一時パスワードを再プロンプトする。
            var savedUnusableForZip = plaintext is not null && isZip && ArchiveCompressor.ContainsNonAscii(plaintext);
            if (savedUnusableForZip)
            {
                plaintext = null;
                await UiDispatcherImpl.InvokeAsync(() =>
                    MessageServiceImpl.ShowError(App.Text("Notify.SavedPasswordZipAsciiOnly")));
            }

            if (plaintext is null)
            {
                // 復号失敗 (別ユーザー/PC コピー等) → ユーザーに通知して再プロンプト。
                // ciphertext 未保存 (初回 Remember 利用) との区別はユーザー視点では不要なので
                // 通知は ciphertext があった場合のみ表示する (ZIP 非対応文字・短すぎる保存値の
                // 通知済みケースを除く)。
                if (!savedUnusableForZip && !savedTooShort && settings.EncryptedCompressionPassword is { Length: > 0 })
                {
                    await UiDispatcherImpl.InvokeAsync(() =>
                        MessageServiceImpl.ShowError(App.Text("Notify.SavedPasswordDecryptFailed")));
                }

                plaintext = await PromptCompressionPasswordAsync(
                    archiveDisplayName, isZip, parentWindow, cancellationToken);
                if (plaintext is null) return null; // user cancelled

                if (savedUnusableForZip)
                {
                    // 保存済みパスワード (7z 用に有効) は上書きせず、今回の圧縮限りで使用する。
                    Logger.Log("ZIP 用の一時パスワードを使用します (保存済みパスワードは変更しません)", LogLevel.Info);
                    return new PasswordResolutionState(plaintext, encryptFileNames)
                    {
                        RedactionScope = Logger.RegisterRedactionToken(plaintext),
                    };
                }

                // 新パスワードを DPAPI 暗号化して永続化 (Remember モードの初回保存 / 再設定)。
                // 保存失敗時は圧縮自体は継続する (UI/UX 上、パスワード保護はあくまでオプション機能なので)。
                try
                {
                    var ciphertext = CompressionPasswordSession.Protect(plaintext);
                    // codex P2 #3384706123: VM の AutoSave は 300ms デバウンスされるため、
                    // ダイアログ操作中の「PromptEachTime 切替 / 保護 OFF」がまだ永続層
                    // (SettingsManager.Current) に届いていないことがある。下の MutateAndSave の
                    // 再チェックが古い値を見て保存してしまわないよう、保存判定の直前に VM の
                    // 保留中 AutoSave を UI スレッドでフラッシュして永続層を最新化する
                    // (テスト等 VM 不在時は no-op)。
                    await UiDispatcherImpl.InvokeAsync(() =>
                    {
                        ViewModels.MainWindowViewModel.Current?.FlushPendingAutoSave();
                        return Task.CompletedTask;
                    });
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
            plaintext = await PromptCompressionPasswordAsync(
                archiveDisplayName, isZip, parentWindow, cancellationToken);
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

    private const int MaxZipPasswordAttempts = 5;

    /// <summary>
    /// 圧縮パスワードのプロンプトを表示し、ZIP のときは ASCII 制約を検証して再プロンプトする。
    /// </summary>
    /// <remarks>
    /// 同梱 7-Zip 26.00 は ZIP 作成時に非 ASCII パスワードを E_INVALIDARG で拒否する
    /// (upstream regression)。ZIP のときは入力直後に検証してエラー通知 + 再プロンプトし、
    /// ユーザーがその場で打ち直せるようにする。再入力は <see cref="MaxZipPasswordAttempts"/>
    /// 回まで (同じ非対応入力が続く場合の無限ループ防止)、超過時はキャンセル扱いで null を返す。
    /// 7z は非 ASCII パスワードでも正常動作するため検証しない。
    /// </remarks>
    /// <returns>確定したパスワード平文。キャンセルまたは試行上限超過で null。</returns>
    /// <remarks>
    /// internal: 設定パネルの「パスワード変更」(MainWindowViewModel.ChangeSavedPasswordAsync) も
    /// 同じ ZIP ASCII 検証を通すために共用する (codex P2 #3384761806)。
    /// </remarks>
    internal static async Task<string?> PromptCompressionPasswordAsync(
        string archiveDisplayName, bool isZip, Window? parentWindow, CancellationToken cancellationToken)
    {
        var isRetry = false;
        for (var attempt = 0; attempt < MaxZipPasswordAttempts; attempt++)
        {
            var plaintext = await PasswordDialogImpl.PromptForPasswordAsync(
                archiveDisplayName, PasswordDialogMode.CompressNew, isRetry, parentWindow, cancellationToken);
            if (plaintext is null) return null; // user cancelled

            if (!isZip || !ArchiveCompressor.ContainsNonAscii(plaintext))
                return plaintext;

            await UiDispatcherImpl.InvokeAsync(() =>
                MessageServiceImpl.ShowError(App.Text("Error.ZipPasswordAsciiOnly")));
            isRetry = true;
        }

        Logger.Log("ZIP 非対応文字を含むパスワード入力が上限回数を超えたため圧縮を中止します", LogLevel.Warning);
        return null;
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
            // he=on 構造解析で確定したパスワードの redaction 登録。catch の LogException 時にも
            // 有効なように try の外で保持し finally で解放する (codex P2 #3386575721)。
            // knownPassword 自体も catch から参照する (redaction 不能な 1〜3 文字パスワードの
            // 例外詳細を生ログしない分岐、codex P2 #3386732834) ため try の外で宣言する。
            IDisposable? knownPasswordRedaction = null;
            string? knownPassword = null;
            // 展開中の AsyncPasswordQuery でユーザーが入力したパスワードの捕捉
            // (codex P2 #3386876537)。ヘッダ可視の暗号化アーカイブ (パスワード ZIP /
            // he=off 7z) は構造解析プロンプトを通らず knownPassword が null のままなので、
            // ExtractArchive からのコールバックでこの層の catch/finally 寿命の redaction
            // scope を登録する。
            // コールバックは 7z.dll 由来のスレッドから呼ばれるためリスト自身を lock に使う。
            var promptedPasswordRedactions = new List<IDisposable>();
            var hasUnredactablePromptedPassword = false;
            void OnPasswordPrompted(string pw)
            {
                lock (promptedPasswordRedactions)
                {
                    promptedPasswordRedactions.Add(Logger.RegisterRedactionToken(pw));
                    if (!Logger.CanRedactToken(pw))
                        hasUnredactablePromptedPassword = true;
                }
            }
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

                // he=on (ヘッダ暗号化) の 7z/rar はパスワード無しだと開くこと自体に失敗し
                // (7z.dll は「暗号化ヘッダ」と「破損」を ctor 時点で区別できない、実機確認済み)、
                // 構造解析が空 (OpenFailed) になる。そのまま進むと ShouldSkipFolderCreation が
                // 常に false になり「Foo/Foo」二重ネストが起きる (codex P2 #3384706128)。
                // 該当拡張子ならここでパスワードを確認して再解析し、検証済みパスワードは
                // 展開・CRC 検証にも引き回す (展開中の再ダイアログを回避)。
                // 全試行失敗時は従来経路 (パスワード無し) に合流するが、展開中の再プロンプトは
                // 抑止する (suppressPasswordPrompt)。本当に破損したアーカイブはパスワード
                // コールバック自体が呼ばれずエラー表示経路に進むため UX は変わらない。
                // 明示キャンセルは展開ごと中止する。
                // (knownPassword の宣言は catch から参照するため try の外にある)
                var structurePromptExhausted = false;
                if (rawStructureInfo.OpenFailed && extension is ".7z" or ".rar")
                {
                    const int MaxStructurePasswordAttempts = 3;
                    var archiveDisplayName = Path.GetFileName(filePath);
                    // バッチ並列時にプロンプトが積み重ならないよう、ループ全体をゲートで直列化する
                    // (codex P2 #3385210128)。
                    await StructurePasswordPromptGate.WaitAsync(cancellationToken);
                    try
                    {
                        for (var attempt = 1; attempt <= MaxStructurePasswordAttempts; attempt++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            // ダイアログ表示そのものは展開中プロンプトと共有の葉ゲートで直列化する
                            // (codex P2 #3386575715)。保持中に他のゲートを取得しないこと。
                            string? pw;
                            await ExtractionPasswordDialogGate.WaitAsync(cancellationToken);
                            try
                            {
                                pw = await PasswordDialogImpl.PromptForPasswordAsync(
                                    archiveDisplayName, View.PasswordDialogMode.Extract, attempt > 1, progressWindow, cancellationToken);
                            }
                            finally
                            {
                                ExtractionPasswordDialogGate.Release();
                            }
                            if (pw is null)
                            {
                                // 明示キャンセルはこのアーカイブの展開ごと中止する (codex P2 #3385210131)。
                                // 従来経路に合流させると展開中の AsyncPasswordQuery がもう一度ダイアログを
                                // 出してしまう。展開経路のキャンセル (EncryptionException → OCE 変換) と同じ
                                // 形に揃える。バッチ側は OCE を「失敗ではなくスキップ」として扱う。
                                Logger.Log("ヘッダ暗号化解析のパスワード入力がキャンセルされたため展開を中止します");
                                throw new OperationCanceledException(App.Text("Error.UserCancelledExtraction"), cancellationToken);
                            }

                            // 解析が例外メッセージ経由で平文パスワードをログに混入させないよう、
                            // パスワードを渡す前に redaction を登録する (codex P2 #3385210137)。
                            // using var は各 iteration 終端で解放され、確定後は下の knownPassword 登録が引き継ぐ。
                            // redaction 対象外の 1〜3 文字パスワード (Extract は既存書庫互換で受理) は、
                            // GetArchiveStructureInfo がパスワード付き失敗時に例外メッセージ自体を
                            // ログへ出さないことで守る (codex P2 #3385301557)。
                            using var attemptRedaction = Logger.RegisterRedactionToken(pw);
                            var retried = ArchiveExtractor.GetArchiveStructureInfo(filePath, pw);
                            if (!retried.OpenFailed)
                            {
                                rawStructureInfo = retried;
                                knownPassword = pw;
                                Logger.Log("検証済みパスワードでアーカイブ構造を再解析しました");
                                break;
                            }
                        }
                    }
                    finally
                    {
                        StructurePasswordPromptGate.Release();
                    }

                    if (knownPassword is null)
                    {
                        // 試行上限まで失敗: 従来経路 (パスワード無し) には合流するが、展開中の
                        // AsyncPasswordQuery で再びダイアログ一式を出さないよう抑止フラグを立てる
                        // (codex P2 #3386575724)。本当に破損したアーカイブはパスワードコールバック
                        // 自体が呼ばれないため、従来どおりエラー表示経路に進む。
                        structurePromptExhausted = true;
                        Logger.Log($"パスワードでアーカイブ構造を解析できませんでした (展開中の再プロンプトは抑止): {archiveDisplayName}", LogLevel.Warning);
                    }
                }
                // 平文パスワードのログ混入防止 (defense-in-depth)。catch の LogException でも
                // 有効である必要があるため、try 内の using ではなく外側の変数に登録して
                // finally で解放する (codex P2 #3386575721: using は unwind 時に catch より
                // 先に dispose される)。
                knownPasswordRedaction = Logger.RegisterRedactionToken(knownPassword);
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
                        snapshot.NormalizeUnicodeFileNames,
                        knownPassword,
                        structurePromptExhausted,
                        OnPasswordPrompted);

                    // CRC 整合性は展開中に 7z.dll が照合済み (二度読みの再検証パスは v1.0.183 で廃止)。
                    // エントリの CRC 不一致は SetOperationResult(CRCError) → ライブラリが Cancel を
                    // 返して展開を中断 → reader.Save が SevenZipException を投げるため、
                    // ExtractArchiveAsync が成功した時点で全エントリの CRC 検証が完了している
                    // (ライブラリの ExtractCallback / CallbackBase.Make(Failed)=Cancel の構造的保証)。
                    // 旧実装はここで ArchiveIntegrityVerifier.VerifyArchiveAsync (reader.Test()) を
                    // 呼んでいたが、同じアーカイブの全エントリをもう一度フルデコードするだけの
                    // 重複処理で、大型アーカイブでは展開時間を約 2 倍にしていた。

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
                // 1〜3 文字のパスワード (knownPassword / 展開中プロンプト入力) は redaction
                // 対象外のため、例外の全文 (スタックトレース・ライブラリ例外メッセージ) を
                // 生ログしない (codex P2 #3386732834 / #3386876537)。
                bool promptedUnredactable;
                lock (promptedPasswordRedactions)
                {
                    promptedUnredactable = hasUnredactablePromptedPassword;
                }
                var sanitizeDetails =
                    (knownPassword is not null && !Logger.CanRedactToken(knownPassword)) || promptedUnredactable;
                if (!sanitizeDetails)
                    Logger.LogException($"展開処理でエラーが発生: {filePath}", ex);
                else
                    Logger.Log($"展開処理でエラーが発生 (パスワード付き・詳細抑止): {filePath} - {ex.GetType().Name} (HResult=0x{ex.HResult:X8})", LogLevel.Error);
                var errorInfo = ArchiveErrorHandler.AnalyzeError(ex, filePath, outputPath ?? string.Empty);
                // redaction 不能な短パスワードが scope にあるときは、ダイアログ本文の詳細も
                // 型名 + HResult の要約に置換する。MessageService.ShowError はダイアログ本文を
                // Logger.Log で永続化するため、上のログ抑止だけでは ex.Message 由来の
                // errorInfo.Details 経由で平文が残る (codex P2 #3389751077)。
                // errorInfo.Message は常にローカライズ済みカテゴリ文字列 (App.Text) なので安全。
                var dialogDetails = sanitizeDetails
                    ? $"{ex.GetType().Name} (HResult=0x{ex.HResult:X8})"
                    : errorInfo.Details;
                // 進捗ウィンドウを先に閉じてからダイアログを表示。Post + 破棄では進捗ウィンドウの
                // クローズ遷移と競合し、ダイアログが背面に隠れる/表示されないことがあるため、
                // ここで明示的に閉じてから await し、ダイアログの表示完了を待ってから return する。
                if (closeWindowOnCompletion)
                {
                    progressWindow?.CloseSafe();
                }
                await UiDispatcherImpl.InvokeAsync(() =>
                    MessageServiceImpl.ShowError(
                        $"{errorInfo.Message}\n\n{App.Text("Dialog.Details")}{dialogDetails}",
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
                // redaction は catch の LogException 完了まで有効にするため最後に解放する。
                knownPasswordRedaction?.Dispose();
                lock (promptedPasswordRedactions)
                {
                    foreach (var scope in promptedPasswordRedactions)
                        scope.Dispose();
                    promptedPasswordRedactions.Clear();
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

                // CompressFilesAsync が IProgress<ProgressInfo> に統一されたので直接渡す。
                // progressReporter が渡されていればそれをそのまま使い、Progress<T> の二重
                // ラップと無駄なアロケ・同期コンテキスト転送を避ける。null のときだけ
                // progressWindow への DispatchProgress 用ラッパを 1 個だけ作る。
                IProgress<ProgressInfo> compressionProgress = progressReporter
                    ?? new Progress<ProgressInfo>(info => ArchiveProgressHelper.DispatchProgress(progressWindow, info));

                // 圧縮前のディスク容量チェック。サイズ見積りは対象ツリーの再帰列挙で
                // 数十万ファイル規模では数十秒かかるため、経過をマーキー表示で伝える。
                compressionProgress.Report(new ProgressInfo(App.Text("Progress.CheckingDiskSpace")));
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

                // Flatモードで個別圧縮時にrelativePath重複があれば競合ダイアログを表示。
                // settings はメソッド冒頭で確保済み。
                List<(string fullPath, string relativePath)>? resolvedFiles = null;
                if (settings.DirectoryStructureMode == DirectoryStructureMode.Flat && Directory.Exists(sourcePath))
                {
                    // 除外パターンは .lhaignore（gitignore 互換）から圧縮実行毎に読み直す。
                    // RespectNestedGitignore=true なら各サブツリーの .gitignore も layered matcher として合成する。
                    var lhaignoreLines = LhaignoreFile.ReadLines();
                    var ignoreMatcher = GitignoreMatcher.Compile(lhaignoreLines);
                    compressionProgress.Report(new ProgressInfo(App.Text("Progress.ScanningFiles", 0)));
                    var scannedFiles = await ArchiveCompressor.ScanSourceFiles(
                        [sourcePath],
                        ignoreMatcher,
                        actualCancellationToken,
                        dirModeOverride: settings.DirectoryStructureMode,
                        normalizeUnicodeOverride: settings.NormalizeUnicodeFileNames,
                        includeHiddenAndSystemEntriesOverride: settings.IncludeHiddenAndSystemEntries,
                        respectNestedGitignore: settings.RespectNestedGitignore,
                        globalIgnoreLines: lhaignoreLines,
                        progress: compressionProgress);

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

                var inaccessibleSkipped = await ArchiveCompressor.CompressFilesAsync(
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
                        // codex P2 #3384761808: backupPath への代入は move 成功後に行う。
                        // move 自体が失敗 (ACL/AV/衝突) した時点では outputPath にはまだ
                        // 元のファイルが無傷で残っており、ここで backupPath を non-null に
                        // していると下の catch が元ファイルを「部分置換の残骸」とみなして
                        // 削除してしまう (バックアップは存在しないので復元もできない)。
                        var backupCandidate = outputPath + ".lhamiel-bak-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                        if (Directory.Exists(outputPath))
                        {
                            Directory.Move(outputPath, backupCandidate);
                            backupPath = backupCandidate;
                        }
                        else if (File.Exists(outputPath))
                        {
                            File.Move(outputPath, backupCandidate);
                            backupPath = backupCandidate;
                        }
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

                // パスワード保護圧縮でアクセス不能スキップが発生した場合は UI でも警告する
                // (codex P2 #3386876544)。スキップされたファイルは暗号化アーカイブに含まれず
                // 平文のまま元の場所に残るため、ログのみだと「全て保護された」と誤認しうる。
                // 非保護圧縮は従来どおりログのみ (1 ファイル不能で全体を死なせない resilience は維持)。
                // 進捗ウィンドウを閉じた後に表示する (クローズ遷移との競合で背面に隠れるのを防ぐ)。
                if (passwordState.Password is not null && inaccessibleSkipped > 0)
                {
                    await UiDispatcherImpl.InvokeAsync(() =>
                        MessageServiceImpl.ShowError(
                            App.Text("Notify.PartialSkipWithPassword", inaccessibleSkipped),
                            Path.GetFileName(outputPath)));
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

                // 進捗ラッパは scan より前に用意する (スキャン経過のマーキー表示に使うため)。
                // DispatchProgress 経由にすることで IsIndeterminate も正しく処理される
                // (旧実装は UpdateProgress(info.Percentage) 直叩きで、不確定進捗が来ると
                //  Percentage=-1 がバーに渡ってしまう)。
                IProgress<ProgressInfo> progress = new Progress<ProgressInfo>(info =>
                    ArchiveProgressHelper.DispatchProgress(progressWindow, info));

                // ファイルリストをスキャン。
                // 除外パターンは .lhaignore（gitignore 互換）から圧縮実行毎に読み直す。
                // RespectNestedGitignore=true なら各サブツリーの .gitignore も layered matcher として合成する。
                var lhaignoreLines = LhaignoreFile.ReadLines();
                var ignoreMatcher = GitignoreMatcher.Compile(lhaignoreLines);
                progress.Report(new ProgressInfo(App.Text("Progress.ScanningFiles", 0)));
                var scannedFiles = await ArchiveCompressor.ScanSourceFiles(
                    sourcePaths.ToList(), ignoreMatcher, actualCancellationToken,
                    dirModeOverride: settings.DirectoryStructureMode,
                    normalizeUnicodeOverride: settings.NormalizeUnicodeFileNames,
                    includeHiddenAndSystemEntriesOverride: settings.IncludeHiddenAndSystemEntries,
                    respectNestedGitignore: settings.RespectNestedGitignore,
                    globalIgnoreLines: lhaignoreLines,
                    progress: progress);

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
                var inaccessibleSkipped = await ArchiveCompressor.CompressFilesAsync(
                    sourcePaths, tempMergedOutputPath, parsedFormat, progress, actualCancellationToken,
                    resolvedFiles, settingsOverride: settings,
                    password: mergedPasswordState.Password, encryptFileNames: mergedPasswordState.EncryptFileNames);

                // atomic swap (codex P1 #3381582647 / P2 #3382065860)
                if (targetExists && !string.Equals(tempMergedOutputPath, outputPath, StringComparison.OrdinalIgnoreCase))
                {
                    string? backupPath = null;
                    try
                    {
                        // codex P2 #3384761808: backupPath への代入は move 成功後に行う
                        // (元ファイルが無傷のまま catch の残骸削除で消えるのを防ぐ。
                        //  詳細は CompressItemAsync の同処理コメント参照)。
                        var backupCandidate = outputPath + ".lhamiel-bak-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                        if (File.Exists(outputPath))
                        {
                            File.Move(outputPath, backupCandidate);
                            backupPath = backupCandidate;
                        }
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

                // パスワード保護圧縮のアクセス不能スキップ警告 (codex P2 #3386876544)。
                // 詳細は CompressItemAsync の同処理コメント参照。
                if (mergedPasswordState.Password is not null && inaccessibleSkipped > 0)
                {
                    await UiDispatcherImpl.InvokeAsync(() =>
                        MessageServiceImpl.ShowError(
                            App.Text("Notify.PartialSkipWithPassword", inaccessibleSkipped),
                            Path.GetFileName(outputPath)));
                }

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
