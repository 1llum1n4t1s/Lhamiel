using Cube.FileSystem.SevenZip;
using CompressionMethod = Cube.FileSystem.SevenZip.CompressionMethod;

namespace Lhamiel.Util;

/// <summary>
/// アーカイブ圧縮機能
/// </summary>
public static class ArchiveCompressor
{
    /// <summary>
    /// ライブラリが書き込み可能な全形式（Settings.SupportedCompressionFormats はUI選択肢のサブセット）
    /// </summary>
    internal static readonly HashSet<string> WritableFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "zip", "7z", "tar", "gz", "bz2", "xz"
    };

    /// <summary>
    /// ファイルシステム上の絶対パス（fullPath）比較に使う OS 依存コンパラ。
    /// Windows は NTFS 既定で case-insensitive、Linux/macOS は case-sensitive なので、
    /// OrdinalIgnoreCase 固定にすると case-sensitive FS 上で `A.txt` と `a.txt` が
    /// 同一視されて別ファイルを取りこぼす/誤マージする問題が起きる。
    /// </summary>
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// <see cref="PathPairComparer"/> のステートレスな共有インスタンス。
    /// <see cref="DeduplicateByIdentity"/> のようなホットパスで毎回 new すると
    /// 無駄なアロケーションになるため、<see cref="PathComparer"/> と同じく静的にキャッシュする。
    /// </summary>
    private static readonly PathPairComparer DefaultPathPairComparer = new(PathComparer);

    /// <summary>
    /// スキャン・準備フェーズの経過テキスト (マーキー表示) を更新する最小間隔（ミリ秒）。
    /// パーセンテージと違い単調増加判定が使えないため、時間スロットルのみで UI 負荷を抑える。
    /// </summary>
    internal const int ProgressTextIntervalMs = 200;

    /// <summary>
    /// 圧縮ファイル名を取得する
    /// </summary>
    /// <param name="sourcePath">圧縮対象のパス</param>
    /// <param name="extension">圧縮形式の拡張子</param>
    /// <param name="outputDirectory">出力ディレクトリ</param>
    /// <param name="outputToSameDirectory">同じディレクトリに出力するかどうか</param>
    /// <returns>圧縮ファイルのパス</returns>
    public static string GetCompressedFileName(string sourcePath, string extension, string outputDirectory = "", bool outputToSameDirectory = false)
    {
        var directory = outputToSameDirectory
            ? Path.GetDirectoryName(sourcePath) ?? ""
            : outputDirectory;

        var trimmedPath = Path.TrimEndingDirectorySeparator(sourcePath);
        var name = Path.GetFileName(trimmedPath);
        // ドットで始まるフォルダ名（.cursor など）は GetFileNameWithoutExtension が空を返すため、
        // ファイル（拡張子あり）の場合のみ拡張子を除去する
        var fileName = Directory.Exists(sourcePath) ? name : (Path.GetFileNameWithoutExtension(trimmedPath) is { Length: > 0 } stem ? stem : name);

        var lowerExtension = extension.ToLowerInvariant();

        return Path.Combine(directory, $"{fileName}.{lowerExtension}");
    }

    /// <summary>
    /// 設定文字列の圧縮形式を Format enum に直接変換する
    /// </summary>
    /// <param name="format">圧縮形式の文字列（"ZIP", "7z", "TAR" など）</param>
    /// <returns>対応する Format enum 値</returns>
    public static Format ParseFormat(string format) => format.ToUpperInvariant() switch
    {
        "ZIP" => Format.Zip,
        "7Z" => Format.SevenZip,
        "TAR" => Format.Tar,
        "GZ" => Format.GZip,
        "BZ2" => Format.BZip2,
        "XZ" => Format.XZ,
        _ => Format.Zip
    };

    /// <summary>
    /// ファイルを圧縮する
    /// </summary>
    /// <param name="sourcePaths">圧縮するファイル・フォルダのパス</param>
    /// <param name="outputPath">出力アーカイブのパス</param>
    /// <param name="format">圧縮形式</param>
    /// <param name="progress">進捗レポータ。<see cref="ArchiveExtractor.ExtractArchiveAsync"/> と同じ <see cref="IProgress{T}"/> 契約に統一。</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <param name="resolvedFiles">衝突解決済みのファイルリスト（指定時はsourcePathsのスキャンをスキップ）</param>
    /// <param name="settingsOverride">使用する設定のスナップショット（並列処理時の race を避けるため呼び出し側で明示）</param>
    /// <param name="password">圧縮パスワード（null または空文字列でパスワード保護なし）。
    /// ZIP は AES-256 (WinZip AE-2) を強制、7z は AES-256 をライブラリ既定で使用。
    /// TAR/GZ/BZ2/XZ では非 null を渡すと <see cref="InvalidOperationException"/> を投げる。</param>
    /// <param name="encryptFileNames">7z 形式でアーカイブ内ファイル名（ヘッダ）も暗号化するか（<c>-mhe=on</c> 相当）。
    /// ZIP では仕様上ヘッダ暗号化が存在しないので無視される。<paramref name="password"/> が null/空のときは無視される。</param>
    /// <returns>アクセス不能 (AccessException) でスキップしたファイル数。パスワード保護圧縮で
    /// 「アーカイブに含まれず平文のまま残ったファイルがある」ことを呼び出し側が UI 警告
    /// できるようにする (codex P2 #3386876544)。</returns>
    public static async Task<int> CompressFilesAsync(IEnumerable<string> sourcePaths, string outputPath, Format format, IProgress<ProgressInfo>? progress = null, CancellationToken cancellationToken = default, List<(string fullPath, string relativePath)>? resolvedFiles = null, Settings? settingsOverride = null, string? password = null, bool encryptFileNames = true)
    {
        var sourceList = sourcePaths.ToList();
        if (sourceList.Count == 0)
        {
            throw new ArgumentException(App.Text("Error.NoFilesToCompress"));
        }

        // パスワード平文がログに偶発的に混入するのを防ぐ defense-in-depth。
        // ライブラリ例外の ex.Message に password が含まれるケースなどを想定。
        // password が null/空のときは no-op。
        using var passwordRedactionScope = Logger.RegisterRedactionToken(password);

        // 出力ディレクトリを作成
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // 設定スナップショットを取得（race を避けるため処理開始時点で1回だけ）
        var settings = settingsOverride ?? SettingsManager.Instance.CreateSnapshot();
        // 除外パターンは .lhaignore（gitignore 互換）から圧縮実行毎に読み直す。
        // RespectNestedGitignore=true なら各サブツリーで優先候補から選んだ除外ルールファイルも
        // layered matcher として合成する。
        var lhaignoreLines = LhaignoreFile.ReadLines();
        var ignoreMatcher = GitignoreMatcher.Compile(lhaignoreLines);

        var outputCreated = false;
        // アクセス不能でスキップしたファイル数 (戻り値)。Task.Run ラムダ内で加算するため
        // メソッドスコープで宣言する (codex P2 #3386876544)。
        var inaccessibleSkipped = 0;
        // 空ディレクトリエントリ用の空マーカーディレクトリ（遅延作成）。後段 finally で必ず掃除する。
        string? emptyDirMarker = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 解決済みリストが渡された場合はそのまま使用、なければスキャン
            if (resolvedFiles is null)
                progress?.Report(new ProgressInfo(App.Text("Progress.ScanningFiles", 0)));
            var filesToCompress = resolvedFiles ?? await ScanSourceFiles(
                sourceList,
                ignoreMatcher,
                cancellationToken,
                settings.DirectoryStructureMode,
                settings.NormalizeUnicodeFileNames,
                settings.IncludeHiddenAndSystemEntries,
                respectNestedGitignore: settings.RespectNestedGitignore,
                globalIgnoreLines: lhaignoreLines,
                sourceIgnoreFileNames: settings.SourceIgnoreFileNames,
                progress: progress);

            Logger.Log($"圧縮対象のファイル総数: {filesToCompress.Count}個");

            // 圧縮処理を開始（ロック中ファイルはライブラリ側で自動的に一時コピーされる）
            progress?.Report(new ProgressInfo(0, App.Text("Compressor.Processing")));

            // 圧縮を実行（IProgress<Report>で詳細な進捗を取得）
            Logger.Log("圧縮処理を開始します");

            try
            {
                // ネイティブ 7z.dll 直列化ゲート: ライブラリ (1llum1n4t1s.Sevenzip) の共有シングルトン
                // SevenZipLibrary は ArchiveWriter の並行動作をサポートしないため、writer の
                // 生成 → Add → Save → Dispose 全体を 1 スロットに直列化する（バッチ圧縮の
                // IoBoundParallelism 並列実行時もネイティブ接触が重ならないよう保証）。
                using var nativeGate = await NativeArchiveGate.EnterAsync(cancellationToken);

                // 重い処理全体を Task.Run で実行
                await Task.Run(() =>
                {
                    // ネイティブ側（7z.dll）との連携を確実に保護するため
                    // 全ての主要オブジェクトを Task.Run の内部スコープで管理する
                    using var writer = CreateArchiveWriter(format, settings, password, encryptFileNames);

                    // ファイルとディレクトリを圧縮アーカイブに追加
                    // スキャン後にファイルが削除されている場合はスキップする
                    var addedCount = 0;
                    // 準備フェーズの経過表示: writer.Add は 1 ファイルずつ開いて読み取り可否を
                    // 検査するため、数十万ファイル規模では分単位かかる (実測: 528k ファイルで
                    // 93 秒)。無報告だと 0% のまま凍って見えるので、件数ベースの経過を流す。
                    var processedCount = 0;
                    var lastPrepareReportTick = 0L;
                    foreach (var (fullPath, relativePath) in filesToCompress)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        processedCount++;
                        var nowTick = Environment.TickCount64;
                        if (nowTick - lastPrepareReportTick >= ProgressTextIntervalMs)
                        {
                            lastPrepareReportTick = nowTick;
                            progress?.Report(new ProgressInfo(
                                App.Text("Progress.PreparingCompression", processedCount, filesToCompress.Count)));
                        }
                        if (relativePath.EndsWith('/'))
                        {
                            // 空ディレクトリエントリ: 実ディレクトリを writer.Add(realDir, "rel/") で渡すと
                            // ライブラリの AddRecursive が realDir を再走査し、スキャンで除外したはず
                            // （隠し/システム属性・.lhaignore 該当）のファイルを復活させてしまう
                            // （中身ゼロ判定のディレクトリでも実体には除外ファイルが残るため）。
                            // 空のマーカーディレクトリを src に渡すと再走査しても中身が無いので、
                            // 意図したディレクトリエントリだけがアーカイブに追加される。
                            emptyDirMarker ??= CreateEmptyDirectoryMarker();
                            // マーカーは 1 個を使い回すが、ライブラリは Add 時点でメタデータを
                            // スナップショットする (RawEntity → Entity ctor)。Add 直前に元ディレクトリの
                            // タイムスタンプをコピーすれば、ディレクトリごとの値がアーカイブに保存される
                            // (コピーしないと空ディレクトリの更新日時が圧縮実行時刻になる)。
                            TryCopyDirectoryTimestamps(fullPath, emptyDirMarker);
                            writer.Add(emptyDirMarker, relativePath);
                            addedCount++;
                            continue;
                        }
                        if (!File.Exists(fullPath))
                        {
                            Logger.Log($"ファイルが見つかりません（スキップ）: {fullPath}");
                            continue;
                        }
                        try
                        {
                            writer.Add(fullPath, relativePath);
                            addedCount++;
                        }
                        catch (AccessException ex)
                        {
                            // ライブラリ (1llum1n4t1s.Sevenzip) の ArchiveWriter.AddItem は
                            // FileShare.Read → FileShare.ReadWrite|Delete の 2 段階で読み取り試行し、
                            // 両方失敗すると AccessException を投げる。Visual Studio の .vsidx 等
                            // FileShare.None で握られたファイルはここに該当する。
                            // 1 ファイルアクセス不能で圧縮全体を死なせず、ログに残してスキップ続行する。
                            inaccessibleSkipped++;
                            Logger.Log(
                                $"ファイルにアクセスできません（スキップ）: {fullPath} - {ex.Message}",
                                LogLevel.Warning);
                        }
                    }
                    if (inaccessibleSkipped > 0)
                    {
                        Logger.Log(
                            $"アクセス不能でスキップしたファイル: {inaccessibleSkipped}個（ログを確認してください）",
                            LogLevel.Warning);
                    }

                    // 空アーカイブ生成防止 (codex P1 / CodeRabbit #3381138394): 1 件もエントリを追加
                    // できなければ fail-fast。スキャン 0 件 / 除外フィルタで全件落ち / 全件アクセス不能、
                    // どの経路でも「中身ゼロの (暗号化された) アーカイブだけが残る」のは仕様違反。
                    if (addedCount == 0)
                    {
                        throw new InvalidOperationException(App.Text("Error.AllSourcesInaccessible"));
                    }

                    // 進捗スロットリング（UIスレッド負荷軽減用）
                    var throttler = new ProgressThrottler();

                    // 進捗報告オブジェクトを生成。
                    // ・Prepare 状態 (7z.dll が Save 冒頭で全エントリを列挙するフェーズ) はバイト
                    //   進捗が動かないため、件数ベースの準備表示を続ける (ライブラリ側に時間
                    //   スロットルが無く数十万件が素通しで届くので、ここで 200ms に間引く)。
                    // ・100% 到達後〜Save 完了/Dispose までは「仕上げ処理中」(マーキー) に切替える。
                    //   ZIP のセントラルディレクトリ書き出し・数十万入力ストリームの一括 close など
                    //   バイト進捗に乗らない後処理があり、100% のまま固まって見えるため。
                    // ・pct==0 は ProgressThrottler の boundary 扱いで素通りするため 1 回に抑える
                    //   (数十万ファイル規模では pct=0 のコールバックが数千回連続する)。
                    var dataStarted = false;
                    var zeroReported = false;
                    var finalizing = false;
                    using var reportProgress = new CancellableProgress<Report>(report =>
                    {
                        if (finalizing) return;
                        if (!dataStarted && report.State == ProgressState.Prepare && report.TotalCount > 0)
                        {
                            var nowTick = Environment.TickCount64;
                            if (nowTick - lastPrepareReportTick >= ProgressTextIntervalMs)
                            {
                                lastPrepareReportTick = nowTick;
                                progress?.Report(new ProgressInfo(
                                    App.Text("Progress.PreparingCompression", report.Count, report.TotalCount)));
                            }
                            return;
                        }
                        var percentage = (int)(report.GetRatio() * 100);
                        if (percentage >= 100)
                        {
                            finalizing = true;
                            progress?.Report(new ProgressInfo(App.Text("Progress.Finalizing")));
                            return;
                        }
                        dataStarted = true;
                        if (percentage == 0)
                        {
                            if (zeroReported) return;
                            zeroReported = true;
                        }
                        if (throttler.ShouldReport(percentage))
                            progress?.Report(new ProgressInfo(percentage, ""));
                    }, cancellationToken);

                    // outputCreated を writer.Save の直前にセットする (Codex P2 指摘対応)。
                    // writer.Save が outputPath を実際に開く / 上書きを開始するのはここから。
                    // Save 前 (CreateArchiveWriter / writer.Add 等) で例外が出た場合、outputPath
                    // はまだ書かれていないため、既存アーカイブを上書き対象として指定していた
                    // ケースで誤ってユーザーの有効ファイルを削除しないようにする。
                    outputCreated = true;

                    // ネイティブメソッドの呼び出し
                    writer.Save(outputPath, reportProgress);

                    // キャンセルされていたらここで一度だけスロー（コールバック内ではスローしない）
                    cancellationToken.ThrowIfCancellationRequested();

                    // 仕上げ表示をここでも保証する (進捗が 100% に到達しないまま Save が返る
                    // ケース: スキャン後のファイル縮小・スキップ等)。この直後に writer の
                    // Dispose (全入力ストリームの一括 close) が走り、数十万ファイル規模では
                    // バイト進捗に乗らない時間がかかるため、マーキー表示のまま覆う。
                    if (!finalizing)
                    {
                        finalizing = true;
                        progress?.Report(new ProgressInfo(App.Text("Progress.Finalizing")));
                    }

                    // 全てのオブジェクトの生存を、ネイティブ処理完了直後に明示的に保証する
                    // これにより、JIT最適化による早期解放（およびそれに伴うアクセス違反）を防ぐ
                    NativeInteropHelper.KeepAliveCallbacks(writer, reportProgress, progress);
                }, cancellationToken);

                // Terminate で 100% を保証（Ice アプリケーションの実装パターンに準拠）。
                // writer の Dispose (一括 close) 完了後に確定 100% へ戻してから完了処理に進む。
                progress?.Report(new ProgressInfo(100, ""));
            }
            catch (Exception ex)
            {
                Logger.Log($"圧縮処理実行中にエラーが発生しました: {ex.Message}");
                throw;
            }

            Logger.Log($"圧縮完了: {outputPath}（{filesToCompress.Count}個のファイル）");
            return inaccessibleSkipped;
        }
        catch (OperationCanceledException oce) when (!cancellationToken.IsCancellationRequested && oce.InnerException is not null)
        {
            // ユーザー主導でないキャンセル。ライブラリ (1llum1n4t1s.Sevenzip 1.0.73) には
            // I/O 失敗 (ディスク満杯・デバイス切断等) を OperationCanceledException で包んで返す
            // 経路があり、これをキャンセル扱いすると本当の失敗が握り潰される。内側の実例外を
            // 昇格させてエラーとして処理する（修正済みライブラリでは SevenZipException で返るため
            // この経路には入らない）。
            Logger.Log($"非キャンセル要因の中断を検出、内部例外へ昇格: {oce.InnerException.Message}");
            TryDeletePartialOutput(outputPath, outputCreated, "圧縮エラー");
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(oce.InnerException);
            // 上の Throw が常に送出するため到達しないが、Task<int> 化に伴い
            // コンパイラの全経路 return/throw 要求 (CS0161) を満たすために明示する。
            throw;
        }
        catch (OperationCanceledException)
        {
            // キャンセル時: 書きかけの出力ファイルを掃除する。
            TryDeletePartialOutput(outputPath, outputCreated, "キャンセル");
            throw;
        }
        catch (Exception ex)
        {
            // 異常終了時 (SevenZipException, IOException, USB 切断による ERROR_DEV_NOT_EXIST など):
            // writer.Save の途中で例外が出た場合、書きかけの 7z/zip ファイルが Next Header (中央
            // ディレクトリ) 欠損の破損状態で残る。これを掃除してユーザーに「壊れた成果物」を
            // 渡さないようにする。削除自体が device error で失敗することもあるが best-effort で良い。
            Logger.Log($"圧縮でエラーが発生しました: {ex.Message}");
            TryDeletePartialOutput(outputPath, outputCreated, "圧縮エラー");
            throw;
        }
        finally
        {
            // 空ディレクトリマーカーを掃除する（best-effort）。成功・失敗・キャンセルいずれでも実行。
            if (emptyDirMarker is not null)
                FileOperations.CleanupTemporaryPath(emptyDirMarker, m => Logger.Log(m, LogLevel.Warning));
        }
    }

    /// <summary>
    /// 空ディレクトリエントリ追加用の「空のマーカーディレクトリ」を %TEMP% に作成して返す。
    /// 中身が常に空であることが重要（<see cref="ArchiveWriter.Add(string, string)"/> に渡すと
    /// ライブラリが src を再走査するため、実ディレクトリではなく空マーカーを使うことで
    /// 除外済みファイルの混入を防ぐ）。
    /// </summary>
    private static string CreateEmptyDirectoryMarker()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"Lhamiel_emptydir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 空ディレクトリマーカーに元ディレクトリのタイムスタンプをコピーする（best-effort）。
    /// マーカー方式ではマーカー自身のメタデータがアーカイブに保存されるため、コピーしないと
    /// 空ディレクトリの更新日時が圧縮実行時刻になってしまう。コピー失敗はアーカイブの内容
    /// 自体には影響しないため、警告ログのみ残して続行する。
    /// </summary>
    private static void TryCopyDirectoryTimestamps(string sourceDir, string markerDir)
    {
        try
        {
            Directory.SetCreationTimeUtc(markerDir, Directory.GetCreationTimeUtc(sourceDir));
            Directory.SetLastWriteTimeUtc(markerDir, Directory.GetLastWriteTimeUtc(sourceDir));
        }
        catch (Exception ex)
        {
            Logger.Log(
                $"空ディレクトリのタイムスタンプコピーに失敗（マーカーの時刻で続行）: {sourceDir} - {ex.Message}",
                LogLevel.Warning);
        }
    }

    /// <summary>
    /// 圧縮中断時の書きかけ出力ファイルを掃除する best-effort ヘルパ。
    /// <paramref name="outputCreated"/> が false（writer.Save まで到達していない）のときは何もしない。
    /// File.Delete 自身が失敗（device not exist 等）した場合は警告ログだけ残して呑む。
    /// </summary>
    private static void TryDeletePartialOutput(string outputPath, bool outputCreated, string reason)
    {
        if (!outputCreated || !File.Exists(outputPath))
            return;
        try
        {
            File.Delete(outputPath);
            Logger.Log($"{reason}時の部分ファイルを削除しました: {outputPath}");
        }
        catch (Exception ex)
        {
            Logger.Log($"{reason}時の部分ファイル削除に失敗しました: {outputPath}, {ex.Message}", LogLevel.Warning);
        }
    }

    /// <summary>
    /// ソースパスからファイルリストを構築する（まとめ圧縮の前段階）。
    /// 戻り値は (fullPath, relativePath) のリスト。衝突解決はまだ行われない。
    /// </summary>
    public static async Task<List<(string fullPath, string relativePath)>> ScanSourceFiles(
        List<string> sourceList, GitignoreMatcher matcher, CancellationToken cancellationToken = default,
        DirectoryStructureMode? dirModeOverride = null, bool? normalizeUnicodeOverride = null,
        bool? includeHiddenAndSystemEntriesOverride = null,
        bool respectNestedGitignore = false,
        IReadOnlyList<string>? globalIgnoreLines = null,
        IReadOnlyList<string>? sourceIgnoreFileNames = null,
        IProgress<ProgressInfo>? progress = null)
    {
        var filesToCompress = new List<(string fullPath, string relativePath)>();
        // スキャン中の経過表示 (マーキー + 発見済み件数)。数十万ファイル規模では列挙だけで
        // 数十秒かかり、無報告だと UI が 0% のまま凍って見えるため、時間スロットルで件数を流す。
        var lastScanReportTick = 0L;
        var dirMode = dirModeOverride ?? SettingsManager.Instance.Current.DirectoryStructureMode;
        var normalizeUnicode = normalizeUnicodeOverride ?? SettingsManager.Instance.NormalizeUnicodeFileNames;
        var includeHiddenAndSystemEntries = includeHiddenAndSystemEntriesOverride
            ?? SettingsManager.Instance.Current.IncludeHiddenAndSystemEntries;

        foreach (var sourcePath in sourceList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(sourcePath))
            {
                // 単一ファイル: ファイル名のみで判定（ソースルートが無いため）
                if (!ShouldExcludeFile(sourcePath, matcher, rootDir: null, isDirectory: false))
                {
                    filesToCompress.Add((sourcePath, NormalizeNfc(Path.GetFileName(sourcePath), normalizeUnicode)));
                }
            }
            else if (Directory.Exists(sourcePath))
            {
                Logger.Log($"ディレクトリをスキャン中: {sourcePath}");

                // 圧縮対象ディレクトリ内に候補の除外ルールファイルがあれば layered matcher を構築する
                // （その source 限定）。.lhaignore (= matcher 引数) で枝刈りしながら探索するので、
                // node_modules/ 等の除外済みサブツリー内のルールファイルは読まない。
                // ⚠️ Codex P2 指摘対応 (#3305241279): BuildLayeredMatcherForSource は matcher (= fallbackMatcher) を
                // 必ず base layer として保持するので、globalIgnoreLines が null でも .lhaignore ルールは保証される。
                var effectiveMatcher = respectNestedGitignore
                    ? BuildLayeredMatcherForSource(
                        sourcePath,
                        globalIgnoreLines,
                        matcher,
                        includeHiddenAndSystemEntries,
                        sourceIgnoreFileNames)
                    : matcher;

                var files = GetFilesRecursively(sourcePath, effectiveMatcher, includeHiddenAndSystemEntries);
                var parentDir = dirMode == DirectoryStructureMode.IncludeRoot
                    ? (Path.GetDirectoryName(sourcePath) ?? "")
                    : sourcePath;

                // ファイルが存在するディレクトリを記録（空ディレクトリ検出用）
                var directoriesWithFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var fileCount = 0;
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var relativePath = NormalizeNfc(
                        dirMode == DirectoryStructureMode.Flat
                            ? Path.GetFileName(file)
                            : Path.GetRelativePath(parentDir, file),
                        normalizeUnicode);
                    filesToCompress.Add((file, relativePath));

                    // このファイルの全祖先ディレクトリを記録。
                    // 既に登録済みの祖先に到達したら break（他のファイルで登録済みの場合は上位まで登録済み）。
                    // これにより O(N × D) から平均 O(N) に近づく。
                    var fileDir = Path.GetDirectoryName(file);
                    while (fileDir != null && fileDir.Length >= sourcePath.Length)
                    {
                        if (!directoriesWithFiles.Add(fileDir)) break;
                        fileDir = Path.GetDirectoryName(fileDir);
                    }

                    fileCount++;
                    if (fileCount % 100 == 0)
                    {
                        await Task.Yield();
                    }

                    var now = Environment.TickCount64;
                    if (now - lastScanReportTick >= ProgressTextIntervalMs)
                    {
                        lastScanReportTick = now;
                        progress?.Report(new ProgressInfo(App.Text("Progress.ScanningFiles", filesToCompress.Count)));
                    }
                }

                // Flatモードでなければ空ディレクトリを収集してエントリに追加
                var emptyDirCount = 0;
                if (dirMode != DirectoryStructureMode.Flat)
                {
                    var emptyDirs = CollectEmptyDirectories(sourcePath, effectiveMatcher, directoriesWithFiles, includeHiddenAndSystemEntries);
                    foreach (var emptyDir in emptyDirs)
                    {
                        var relativePath = NormalizeNfc(Path.GetRelativePath(parentDir, emptyDir), normalizeUnicode);
                        filesToCompress.Add((emptyDir, relativePath + "/"));
                    }

                    emptyDirCount = emptyDirs.Count;
                    if (emptyDirs.Count > 0)
                        Logger.Log($"空ディレクトリ: {emptyDirs.Count}個を追加");
                }

                // codex P2 #3384620482: 空ディレクトリそのものをドロップした場合 (files=0 かつ
                // 子の空ディレクトリも 0)、CollectEmptyDirectories は root 自身を返さないため
                // エントリが 1 件も残らず、addedCount==0 guard が「全ソースアクセス不能」という
                // 誤ったエラーで中止してしまう。IncludeRoot モードでは root 自身を
                // 空ディレクトリエントリとして追加し、「空フォルダを圧縮」を有効な操作として
                // 成立させる (空フォルダ 1 個入りのアーカイブ)。
                // ExcludeRoot/Flat では root の相対パスが "." になり意味のあるエントリを
                // 表現できないため追加しない (本当に中身ゼロなら従来通り guard が中止する)。
                if (fileCount == 0 && emptyDirCount == 0 && dirMode == DirectoryStructureMode.IncludeRoot)
                {
                    var rootRelative = NormalizeNfc(Path.GetRelativePath(parentDir, sourcePath), normalizeUnicode);
                    filesToCompress.Add((sourcePath, rootRelative + "/"));
                    Logger.Log($"空ディレクトリをルートエントリとして追加: {sourcePath}");
                }

                Logger.Log($"スキャン完了: {fileCount}個のファイルが見つかりました");
            }
            else
            {
                throw new FileNotFoundException(App.Text("Error.PathNotFound", sourcePath));
            }
        }

        // 同一 (fullPath, relativePath) の重複を最終的に排除する。
        // 呼び出し側が同じパスを複数回渡した場合（CLI で重複引数、複数選択時の
        // 偶発的重複など）や、sourceList に祖先と子孫が同時に含まれて走査で重複した
        // ケースに備える。DetectConflicts は「異なる fullPath が同じ relativePath に
        // 衝突するか」で判定するため、同一 fullPath 重複は衝突検出で素通しされてしまい、
        // このステップで除去しないと ArchiveWriter が同名エントリを重複追加してしまう。
        return DeduplicateByIdentity(filesToCompress);
    }

    /// <summary>
    /// 圧縮対象ディレクトリ <paramref name="sourceDir"/> 配下で、各ディレクトリごとに
    /// <paramref name="sourceIgnoreFileNames"/> を上から確認して除外ルールファイルを選び、
    /// 親ディレクトリ相対の layer として合成した <see cref="GitignoreMatcher"/> を返す。
    /// 子孫では祖先と同じ候補または祖先より高優先の候補だけを選べるため、階層を下る途中で
    /// 一度選ばれた高優先候補から低優先候補へ戻ることはない。
    /// 既に <c>.lhaignore</c> で除外されるディレクトリ（例: <c>node_modules/</c>）配下は探索しない。
    /// <para>
    /// Codex P2 指摘対応: <paramref name="globalIgnoreLines"/> が <c>null</c> の場合でも
    /// <paramref name="fallbackMatcher"/> のルールを必ず保持する。旧実装は <c>globalIgnoreLines ?? []</c> で
    /// フォールバックしていたが、それだと呼び出し元が既に <c>.lhaignore</c> から compile 済みの matcher を
    /// 持っている場合（生 lines を保持していないケース）に <c>fallbackMatcher</c> のルールが silent ドロップされていた。
    /// </para>
    /// </summary>
    internal static GitignoreMatcher BuildLayeredMatcherForSource(
        string sourceDir,
        IReadOnlyList<string>? globalIgnoreLines,
        GitignoreMatcher fallbackMatcher,
        bool includeHiddenAndSystemEntries,
        IReadOnlyList<string>? sourceIgnoreFileNames = null)
    {
        ArgumentNullException.ThrowIfNull(fallbackMatcher);

        var normalizedSourceIgnoreFileNames = Settings.TryNormalizeSourceIgnoreFileNames(
            sourceIgnoreFileNames ?? Settings.CreateDefaultSourceIgnoreFileNames(),
            out var normalizedNames)
            ? normalizedNames
            : Settings.CreateDefaultSourceIgnoreFileNames();

        // ベース matcher は呼び出し元から渡された fallbackMatcher (= 既にコンパイル済みの .lhaignore matcher)。
        // globalIgnoreLines が非 null なら、それを source root スコープの追加 layer として最初に重ねる。
        // 通常パスでは fallbackMatcher 自体が .lhaignore からビルドされているため、ルール重複が発生する場合
        // もあるが、gitignore セマンティクスでは同一ルールの重複評価は最終 excluded 値に影響しない（後勝ちで同じ結果）。
        var additionalLayers = new List<(string baseRelativePath, IEnumerable<string> lines)>();
        if (globalIgnoreLines is { Count: > 0 })
        {
            additionalLayers.Add((string.Empty, globalIgnoreLines));
        }

        // 枝刈り用 prune matcher は fallback + global lines + root の選択済みルール（もしあれば）を合成する。
        // DiscoverSourceIgnoreFiles が yield する layer は呼び出し時点でこの prune matcher で枝刈り済み。
        foreach (var (relativeDir, lines) in DiscoverSourceIgnoreFiles(
                     sourceDir,
                     fallbackMatcher,
                     globalIgnoreLines,
                     includeHiddenAndSystemEntries,
                     normalizedSourceIgnoreFileNames))
        {
            additionalLayers.Add((relativeDir, lines));
        }

        return additionalLayers.Count == 0
            ? fallbackMatcher
            : GitignoreMatcher.CompileLayered(fallbackMatcher, additionalLayers);
    }

    private static IEnumerable<(string relativeDir, string[] lines)> DiscoverSourceIgnoreFiles(
        string sourceDir,
        GitignoreMatcher fallbackMatcher,
        IReadOnlyList<string>? globalIgnoreLines,
        bool includeHiddenAndSystemEntries,
        IReadOnlyList<string> sourceIgnoreFileNames)
    {
        // source root 自身で最優先の除外ルールファイル（あれば）を先に読む。
        // ⚠️ ここで読んだ root ルールは、その後のサブディレクトリ走査 (= さらなる folder-local
        // ルールを探す再帰探索) でも枝刈りに使う。これをしないと、root ルールで除外される
        // 大規模サブツリー (vendor/, build/, node_modules/.pnpm/ 等) も毎回完全に走査されて
        // O(tree size) の無駄なスキャンコストが発生する。RTK レビュー Codex P2 指摘対応。
        string[]? rootLines = null;
        var rootIgnoreFile = FindFirstSourceIgnoreFile(
            sourceDir,
            sourceIgnoreFileNames,
            sourceIgnoreFileNames.Count - 1);
        if (rootIgnoreFile is { } rootSelection)
        {
            rootLines = TryReadIgnoreFileLines(rootSelection.path);
            if (rootLines is not null)
                yield return (string.Empty, rootLines);
        }

        // 枝刈り matcher = fallbackMatcher + global lines + root の選択済みルールの 3 段合流。
        // fallbackMatcher のルールを必ず保持することで、呼び出し元から渡された .lhaignore ルールが
        // silent ドロップされる経路を排除する (Codex P2 指摘 #3305241279)。
        var pruneAdditional = new List<(string baseRelativePath, IEnumerable<string> lines)>();
        if (globalIgnoreLines is { Count: > 0 })
            pruneAdditional.Add((string.Empty, globalIgnoreLines));
        if (rootLines is { Length: > 0 })
            pruneAdditional.Add((string.Empty, rootLines));

        var pruneMatcher = pruneAdditional.Count == 0
            ? fallbackMatcher
            : GitignoreMatcher.CompileLayered(fallbackMatcher, pruneAdditional);

        // 各分岐で現在有効な候補順位を引き継ぐ。候補 index は小さいほど高優先なので、
        // 子では inherited index 以下だけを探索する。これにより
        // root=.gitignore → child=.lhamielignore の昇格は許可し、
        // root=.lhamielignore → child=.gitignore の降格は抑止する。
        var activePriorityByDirectory = new Dictionary<string, int?>(PathComparer)
        {
            [Path.GetFullPath(sourceDir)] = rootIgnoreFile?.candidateIndex,
        };

        // ディレクトリツリーを併合 matcher で枝刈りしながら走査
        foreach (var dir in EnumerateDirectoriesWithPruning(sourceDir, pruneMatcher, includeHiddenAndSystemEntries))
        {
            var fullDir = Path.GetFullPath(dir);
            var parentDir = Directory.GetParent(fullDir)?.FullName;
            var inheritedPriority = parentDir is not null
                && activePriorityByDirectory.TryGetValue(parentDir, out var parentPriority)
                    ? parentPriority
                    : rootIgnoreFile?.candidateIndex;

            var maxCandidateIndex = inheritedPriority ?? sourceIgnoreFileNames.Count - 1;
            var ignoreFile = FindFirstSourceIgnoreFile(dir, sourceIgnoreFileNames, maxCandidateIndex);
            var activePriority = ignoreFile?.candidateIndex ?? inheritedPriority;
            activePriorityByDirectory[fullDir] = activePriority;

            if (ignoreFile is not { } selection)
                continue;
            var lines = TryReadIgnoreFileLines(selection.path);
            if (lines is null)
                continue;
            var rel = Path.GetRelativePath(sourceDir, dir);
            yield return (rel, lines);
        }
    }

    private static (string path, int candidateIndex)? FindFirstSourceIgnoreFile(
        string directory,
        IReadOnlyList<string> sourceIgnoreFileNames,
        int maxCandidateIndex)
    {
        var lastCandidateIndex = Math.Min(maxCandidateIndex, sourceIgnoreFileNames.Count - 1);
        for (var candidateIndex = 0; candidateIndex <= lastCandidateIndex; candidateIndex++)
        {
            var fileName = sourceIgnoreFileNames[candidateIndex];
            var path = Path.Combine(directory, fileName);
            if (File.Exists(path))
                return (path, candidateIndex);
        }

        return null;
    }

    private static string[]? TryReadIgnoreFileLines(string path)
    {
        try
        {
            return File.ReadAllLines(path);
        }
        catch (Exception ex)
        {
            Logger.Log($"除外ルールファイルの読み込みに失敗しました: {path}, {ex.Message}", LogLevel.Warning);
            return null;
        }
    }

    /// <summary>
    /// (fullPath, relativePath) ペアで同一のエントリを除去する。順序は維持し、先勝ち。
    /// fullPath 部分は OS 依存（Windows=ignore case / Linux=case sensitive）、
    /// relativePath 部分も同じコンパラで判定する（Windows 上では `A/` と `a/` が同じ
    /// アーカイブエントリ扱いになる既存セマンティクスを維持）。
    /// </summary>
    /// <remarks>
    /// 旧実装は `"{fullPath}|{relativePath}"` の文字列結合をキーにしていたが、`|` は
    /// Linux/macOS では正当なファイル名文字になりうるため、例えば `("a","b|c")` と
    /// `("a|b","c")` がキー衝突してしまう。ValueTuple + <see cref="PathPairComparer"/>
    /// に置き換えて衝突を構造的に回避する。
    /// </remarks>
    private static string NormalizeNfc(string path, bool enabled)
    {
        if (!enabled || path.IsNormalized(System.Text.NormalizationForm.FormC))
            return path;
        return path.Normalize(System.Text.NormalizationForm.FormC);
    }

    private static List<(string fullPath, string relativePath)> DeduplicateByIdentity(
        List<(string fullPath, string relativePath)> files)
    {
        if (files.Count <= 1) return files;

        var seen = new HashSet<(string fullPath, string relativePath)>(DefaultPathPairComparer);
        var result = new List<(string fullPath, string relativePath)>(files.Count);
        var skipped = 0;
        foreach (var entry in files)
        {
            if (seen.Add(entry))
                result.Add(entry);
            else
                skipped++;
        }

        if (skipped > 0)
            Logger.Log($"同一 (fullPath, relativePath) の重複 {skipped} 件を除去しました");
        return result;
    }

    /// <summary>
    /// (fullPath, relativePath) ValueTuple に対して OS 依存のパス比較を適用する
    /// <see cref="IEqualityComparer{T}"/> 実装。
    /// </summary>
    private sealed class PathPairComparer(StringComparer comparer) : IEqualityComparer<(string fullPath, string relativePath)>
    {
        public bool Equals((string fullPath, string relativePath) x, (string fullPath, string relativePath) y) =>
            comparer.Equals(x.fullPath, y.fullPath) && comparer.Equals(x.relativePath, y.relativePath);

        public int GetHashCode((string fullPath, string relativePath) obj) =>
            HashCode.Combine(
                comparer.GetHashCode(obj.fullPath ?? string.Empty),
                comparer.GetHashCode(obj.relativePath ?? string.Empty));
    }

    /// <summary>
    /// ファイルリストから衝突グループを検出する。
    /// 衝突がない場合は空リストを返す。
    /// </summary>
    /// <remarks>
    /// FileInfo は同一 fullPath に対して 1 回だけ構築して使い回すことで、重複 stat syscall を回避する。
    /// </remarks>
    public static List<Models.FileConflictGroup> DetectConflicts(List<(string fullPath, string relativePath)> files)
    {
        // 同一 fullPath の FileInfo を 1 回だけ生成してキャッシュする（衝突グループ内の stat 多発を回避）。
        // キーは fullPath なので PathComparer を使って OS のファイルシステム semantics に合わせる。
        var fileInfoCache = new Dictionary<string, (long length, DateTime lastWrite)>(PathComparer);
        (long length, DateTime lastWrite) GetInfo(string fullPath)
        {
            if (fileInfoCache.TryGetValue(fullPath, out var cached)) return cached;
            var info = new FileInfo(fullPath);
            var value = info.Exists
                ? (info.Length, info.LastWriteTime)
                : (0L, DateTime.MinValue);
            fileInfoCache[fullPath] = value;
            return value;
        }

        // 衝突判定は「同じ relativePath に対して 2 つ以上の異なる fullPath が存在する」場合のみ。
        // 同一 fullPath が重複しているだけのケース（呼び出し側の不注意な重複入力等）は
        // 衝突扱いせず、代表 1 件だけ残すのが期待挙動。
        // ScanSourceFiles 側で fullPath は既に絶対パス化されているため、ここでの
        // Path.GetFullPath 再正規化は不要（大量ファイル時のオーバーヘッド回避）。
        // 相対パス（アーカイブ内パス）は ZIP/7z 仕様に合わせて OrdinalIgnoreCase、
        // fullPath（実 FS パス）は OS 依存の PathComparer を使うことで、case-sensitive FS
        // 上の `A.txt` と `a.txt` を正しく別ファイルとして扱う。
        var groups = files
            .GroupBy(f => f.relativePath, StringComparer.OrdinalIgnoreCase)
            .Where(g => g
                .Select(f => f.fullPath)
                .Distinct(PathComparer)
                .Skip(1)
                .Any())
            .Select(g => new Models.FileConflictGroup
            {
                ConflictingName = g.Key,
                Kind = Models.ConflictKind.CompressionList,
                Entries = g.Select(f =>
                {
                    var (len, lastWrite) = GetInfo(f.fullPath);
                    return new Models.FileConflictEntry(f.fullPath, f.relativePath, len, lastWrite);
                }).ToList()
            })
            .ToList();

        return groups;
    }

    /// <summary>
    /// ArchiveWriterを作成する（スレッド数制御 + パスワード保護対応）
    /// </summary>
    /// <param name="format">圧縮形式</param>
    /// <param name="settings">設定オブジェクト</param>
    /// <param name="password">パスワード（null/空でパスワード保護なし）</param>
    /// <param name="encryptFileNames">7z でファイル名（ヘッダ）も暗号化するか（<c>-mhe=on</c> 相当）。ZIP では仕様上不可能なので無視。</param>
    /// <param name="maxThreads">最大スレッド数（0または負の値で自動設定）</param>
    /// <returns>ArchiveWriterインスタンス</returns>
    /// <exception cref="InvalidOperationException">TAR/GZ/BZ2/XZ で <paramref name="password"/> を指定した場合（これらの形式は暗号化非対応）。</exception>
    private static ArchiveWriter CreateArchiveWriter(Format format, Settings settings, string? password = null, bool encryptFileNames = true, int maxThreads = -1)
    {
        // スレッド数を [1, 論理プロセッサ数] に丸める。
        // ・maxThreads が未指定/0以下のときは論理プロセッサ数を既定とする。
        // ・将来 maxThreads を設定値から渡す場合でも、コア数を超える oversubscribe や
        //   0/負値（ライブラリ側 CompressionOption.Validate が ArgumentOutOfRangeException を
        //   投げる）が 7z.dll に届かないようにする。
        // ・7z (LZMA2) の実効並列度とメモリ使用量はメソッド毎に 7z.dll 側で内部制限されるため、
        //   ここでは論理コア数を上限とするに留め、実機での圧縮性能は据え置く。
        var threadCount = Math.Clamp(
            maxThreads > 0 ? maxThreads : Environment.ProcessorCount,
            1,
            Environment.ProcessorCount);

        var hasPassword = !string.IsNullOrEmpty(password);

        // 形式に応じたオプションを設定
        if (format == Format.SevenZip)
        {
            // 7z形式: LZMA2 + スレッド数制御
            // パスワード指定時は AES-256 が 7z 仕様上の唯一の選択肢（ライブラリの SevenZipOptionSetter は
            // em プロパティを送らないが、7z.dll が body 暗号化に AES-256 を強制する）。
            // ヘッダ暗号化（ファイル名も暗号化）は CustomParameters の "he"="on" で有効化する。
            var customParameters = new Dictionary<string, string>();
            if (hasPassword && encryptFileNames)
                customParameters["he"] = "on";

            var options = new CompressionOption
            {
                CompressionLevel = (CompressionLevel)settings.SevenZipCompressionLevel,
                CompressionMethod = CompressionMethod.Lzma2,
                ThreadCount = threadCount,
                Password = hasPassword ? password! : string.Empty,
                CustomParameters = customParameters
            };
            return new ArchiveWriter(format, options);
        }
        if (format == Format.Zip)
        {
            // 同梱 7-Zip 26.00 は ZIP 作成時に非 ASCII パスワードを E_INVALIDARG で拒否する
            // (upstream regression、ライブラリ CLAUDE.md の既知問題。7z は非 ASCII でも正常動作)。
            // ネイティブまで届くと不透明な SevenZipException になるため、ここで fail-fast して
            // 具体的なメッセージを返す。通常は TryResolveCompressionPasswordAsync 側の検証 +
            // 再プロンプトで止まるので、ここはバッチ override 等で検証を迂回した場合の防御線。
            if (hasPassword && ContainsNonAscii(password!))
                throw new InvalidOperationException(App.Text("Error.ZipPasswordAsciiOnly"));

            // ZIP形式: UTF-8エンコーディング
            // ⚠️ パスワード指定時は **必ず** EncryptionMethod=Aes256 を明示する。
            // ライブラリの ZipOptionSetter は EncryptionMethod=Default のとき em プロパティを送らず、
            // 7z.dll のデフォルトである **ZipCrypto (脆弱な旧式)** に fallback してしまう。
            // Lhamiel はセキュリティ要件として AES-256 (WinZip AE-2) を強制する。
            var options = new CompressionOption
            {
                CompressionLevel = (CompressionLevel)settings.ZipCompressionLevel,
                CompressionMethod = CompressionMethod.Deflate,
                ThreadCount = threadCount,
                CodePage = CodePage.Utf8,
                Password = hasPassword ? password! : string.Empty,
                EncryptionMethod = hasPassword ? EncryptionMethod.Aes256 : EncryptionMethod.Default
            };
            // 二重防御: ZipCrypto 落ちを構造的に防ぐ assert。
            // 上の三項演算子が壊れた場合（将来の改修ミス等）に気付けるよう、ここでも検査する。
            if (hasPassword && options.EncryptionMethod != EncryptionMethod.Aes256)
                throw new InvalidOperationException("ZIP encryption must be AES-256 (defense-in-depth check).");
            return new ArchiveWriter(format, options);
        }

        // TAR/GZ/BZ2/XZ など暗号化非対応形式: password 指定があれば fail-fast する
        // （ライブラリの CompressionOption.Validate も投げるが、こちらでガードして UI ガード漏れを検知）。
        if (hasPassword)
            throw new InvalidOperationException(App.Text("Error.PasswordNotSupportedByFormat", format.ToString()));
        return new ArchiveWriter(format);
    }

    /// <summary>
    /// 文字列に ASCII 範囲外 (U+0080 以上) の文字が含まれるかを判定する。
    /// </summary>
    /// <remarks>
    /// ZIP パスワードの ASCII 制約検証用 (同梱 7-Zip 26.00 が ZIP 作成時に
    /// 非 ASCII パスワードを E_INVALIDARG で拒否する upstream regression への対応)。
    /// <see cref="ArchiveProcessor"/> の入力時検証と <see cref="CreateArchiveWriter"/> の
    /// fail-fast guard の両方から使う。
    /// </remarks>
    internal static bool ContainsNonAscii(string value)
    {
        foreach (var c in value)
        {
            if (c > '\x7F') return true;
        }
        return false;
    }

    /// <summary>
    /// 同名フォルダが存在するかチェックする
    /// </summary>
    /// <param name="archivePath">アーカイブパス</param>
    /// <param name="folderName">チェックするフォルダ名</param>
    /// <returns>同名フォルダが存在するかどうか</returns>
    public static bool HasFolderWithSameName(string archivePath, string folderName)
    {
        if (!File.Exists(archivePath))
        {
            return false;
        }

        try
        {
            // ネイティブ 7z.dll 直列化ゲート（reader より外側で取得して生成→使用→Dispose を覆う）
            using var nativeGate = NativeArchiveGate.Enter();
            using var reader = new ArchiveReader(archivePath);

            // アーカイブ内のディレクトリエントリは "folder/" のように末尾区切り文字を伴うことが
            // あるため、比較前に両者の末尾 '/' '\' を除去して突き合わせる（末尾区切りの有無だけで
            // 同名フォルダ判定が外れるのを防ぐ）。
            var normalizedTarget = folderName.TrimEnd('/', '\\');

            // Items プロパティを使用してアーカイブ内容をチェック
            return reader.Items.Any(item =>
                item.IsDirectory &&
                string.Equals(item.FullName.TrimEnd('/', '\\'), normalizedTarget, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Logger.Log($"フォルダ構造のチェックに失敗しました: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 指定されたパスが除外対象か判定する。<paramref name="rootDir"/> が null の場合は単一ファイルモードで
    /// パス全体を <see cref="GitignoreMatcher"/> に照合する（ファイル名だけでなく、親ディレクトリの
    /// セグメントも directoryOnly ルールで判定される）。<paramref name="rootDir"/> が指定されたときは
    /// <see cref="Path.GetRelativePath(string, string)"/> で相対パス化して照合する。
    /// </summary>
    /// <param name="path">チェックするパス（絶対 or 相対）</param>
    /// <param name="matcher"><see cref="GitignoreMatcher"/>（.lhaignore および各 .gitignore から構築済）</param>
    /// <param name="rootDir">ソースルート（指定時は path をルート相対化）。<c>null</c> で単一ファイルモード</param>
    /// <param name="isDirectory">対象がディレクトリの場合は <c>true</c></param>
    /// <param name="traversalMode">
    /// DFS 枝刈りと併用する走査モード。<c>true</c> のとき各エントリを「自分自身のレベル」だけで照合し、
    /// 除外の推移性は DFS 側の枝刈りに委ねる（中間ディレクトリの否定再包含を git 同等に扱うため）。
    /// 単発ファイル判定など DFS を伴わない呼び出しは <c>false</c>（既定）。
    /// </param>
    /// <returns>除外すべき場合は <c>true</c></returns>
    internal static bool ShouldExcludeFile(string path, GitignoreMatcher matcher, string? rootDir = null, bool isDirectory = false, bool traversalMode = false)
    {
        if (!matcher.HasRules)
            return false;

        string relative;
        bool singleFileMode;
        if (rootDir is null)
        {
            // 単一ファイル: パス全体を / 区切りに正規化して渡す。
            // ファイル名だけでは "node_modules/a.js" のようなパスで `node_modules/` ルールが効かないため、
            // 親セグメントも照合できるよう IsExcluded 側でディレクトリ限定ルールが親ディレクトリ部にも適用される。
            // ルート相対のアンカードパターン（"/build" 等）は singleFileMode=true でスキップする。
            relative = GitignoreMatcher.NormalizePath(path).TrimStart('/');
            if (string.IsNullOrEmpty(relative))
                return false;
            singleFileMode = true;
        }
        else
        {
            relative = Path.GetRelativePath(rootDir, path);
            if (relative == "." || string.IsNullOrEmpty(relative))
                return false;
            singleFileMode = false;
        }

        return matcher.IsExcluded(GitignoreMatcher.NormalizePath(relative), isDirectory, singleFileMode, traversalMode);
    }

    /// <summary>
    /// アーカイブ内の相対パスが衝突するファイルを解決する
    /// </summary>
    /// <param name="files">ファイルリスト（fullPath, relativePath）</param>
    /// <param name="preservePath">true: 親フォルダ名をプレフィックスとして付与、false: 連番サフィックスで自動リネーム</param>
    /// <returns>衝突が解決されたファイルリスト</returns>
    internal static List<(string fullPath, string relativePath)> ResolveRelativePathConflicts(
        List<(string fullPath, string relativePath)> files, bool preservePath)
    {
        // 衝突があるか高速チェック（大半のケースはここで終了）
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasConflict = false;
        foreach (var (_, relativePath) in files)
        {
            if (!seen.Add(relativePath))
            {
                hasConflict = true;
                break;
            }
        }

        if (!hasConflict)
            return files;

        Logger.Log($"アーカイブ内の相対パスに衝突を検出。解決方式: {(preservePath ? "パス保持" : "リネーム")}");

        if (preservePath)
            return ResolveByPreservingPath(files);

        return ResolveByRenaming(files);
    }

    /// <summary>
    /// パス保持方式: 親フォルダ名をプレフィックスとして付与して衝突を解決
    /// </summary>
    private static List<(string fullPath, string relativePath)> ResolveByPreservingPath(
        List<(string fullPath, string relativePath)> files)
    {
        var result = new List<(string fullPath, string relativePath)>(files.Count);
        foreach (var (fullPath, relativePath) in files)
        {
            // 既にサブフォルダ付き（ディレクトリスキャン由来）ならそのまま
            if (relativePath.Contains(Path.DirectorySeparatorChar) || relativePath.Contains(Path.AltDirectorySeparatorChar))
            {
                result.Add((fullPath, relativePath));
                continue;
            }

            // ファイル単体: 親フォルダ名をプレフィックスとして付与
            var parentDir = Path.GetDirectoryName(fullPath);
            var parentName = parentDir != null ? Path.GetFileName(parentDir) : "";
            var newRelativePath = string.IsNullOrEmpty(parentName)
                ? relativePath
                : Path.Combine(parentName, relativePath);

            result.Add((fullPath, newRelativePath));
        }

        return result;
    }

    /// <summary>
    /// 複合拡張子（.tar.gz 等）を考慮してファイル名をステムと拡張子に分割する。
    /// 例: "archive.tar.gz" → ("archive", ".tar.gz")
    ///      "photo.jpg"      → ("photo", ".jpg")
    ///      "Makefile"       → ("Makefile", "")
    ///      ".gitignore"     → ("", ".gitignore")
    /// </summary>
    internal static (string stem, string extension) SplitStemAndExtension(string fileName)
    {
        // 既知の複合拡張子パターン（ArchiveFormatConstants で一元管理）
        foreach (var compoundExt in ArchiveFormatConstants.CompoundTarExtensions)
        {
            if (fileName.EndsWith(compoundExt, StringComparison.OrdinalIgnoreCase) && fileName.Length > compoundExt.Length)
            {
                return (fileName[..^compoundExt.Length], fileName[^compoundExt.Length..]);
            }
        }

        // 通常の拡張子分割
        var ext = Path.GetExtension(fileName);
        var stem = ext.Length > 0 ? fileName[..^ext.Length] : fileName;
        return (stem, ext);
    }

    /// <summary>
    /// リネーム方式: 衝突するファイル名に連番サフィックスを付与して解決する。
    /// usedPaths の値を「次に試す連番」として使い、do-while で毎回先頭から探索する
    /// O(K²) の挙動を避けて O(K) で解決する。
    /// </summary>
    private static List<(string fullPath, string relativePath)> ResolveByRenaming(
        List<(string fullPath, string relativePath)> files)
    {
        var result = new List<(string fullPath, string relativePath)>(files.Count);
        // key = 元の relativePath、value = 次に試す連番（次回は value++ したものを使う）
        var nextSuffix = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // 既に使用済みの実パス集合（衝突回避用）
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (fullPath, relativePath) in files)
        {
            if (!usedPaths.Contains(relativePath))
            {
                // 初出: そのまま使用
                usedPaths.Add(relativePath);
                nextSuffix[relativePath] = 1;
                result.Add((fullPath, relativePath));
                continue;
            }

            // 衝突: 連番サフィックスを付与（foo_1.jpg, foo_2.jpg, ...）
            var dir = Path.GetDirectoryName(relativePath) ?? "";
            var nameOnly = Path.GetFileName(relativePath);
            var (name, ext) = SplitStemAndExtension(nameOnly);
            var count = nextSuffix.TryGetValue(relativePath, out var prev) ? prev : 1;
            string newPath;
            do
            {
                newPath = string.IsNullOrEmpty(dir)
                    ? $"{name}_{count}{ext}"
                    : Path.Combine(dir, $"{name}_{count}{ext}");
                count++;
            } while (usedPaths.Contains(newPath));

            nextSuffix[relativePath] = count; // 次回はここから開始
            usedPaths.Add(newPath);
            result.Add((fullPath, newPath));
            Logger.Log($"同名ファイルをリネーム: {relativePath} → {newPath}");
        }

        return result;
    }

    /// <summary>
    /// 出力ファイルパスの衝突を連番サフィックスで回避する
    /// </summary>
    /// <param name="outputPath">希望する出力パス</param>
    /// <returns>衝突しないユニークなパス</returns>
    public static string GetUniqueOutputPath(string outputPath)
    {
        if (!File.Exists(outputPath) && !Directory.Exists(outputPath))
            return outputPath;

        var dir = Path.GetDirectoryName(outputPath) ?? "";
        var nameOnly = Path.GetFileName(outputPath);
        var (name, ext) = SplitStemAndExtension(nameOnly);

        for (var i = 1; i < 10000; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException(App.Text("Error.UniqueNameFailed", outputPath));
    }

    /// <summary>
    /// ファイルを含まない空ディレクトリを再帰的に収集する。
    /// </summary>
    /// <remarks>
    /// <c>GetFilesRecursively</c> でファイル列挙時に記録した <paramref name="directoriesWithFiles"/>
    /// の補集合を返す。`Directory.EnumerateDirectories` による 2 回目の走査を避けるため、
    /// HashSet 差分で高速に算出する。
    /// </remarks>
    /// <param name="rootDir">ルートディレクトリ</param>
    /// <param name="matcher">除外判定に使う .gitignore 互換マッチャ</param>
    /// <param name="directoriesWithFiles">ファイルが存在するディレクトリのセット</param>
    /// <param name="includeHiddenAndSystemEntries">Hidden/System 属性のエントリも列挙対象に含めるか</param>
    /// <returns>空ディレクトリのパスリスト</returns>
    private static List<string> CollectEmptyDirectories(
        string rootDir,
        GitignoreMatcher matcher,
        HashSet<string> directoriesWithFiles,
        bool includeHiddenAndSystemEntries)
    {
        var emptyDirs = new List<string>();
        try
        {
            // 枝刈り対応の DFS で全ディレクトリを列挙する。matcher で除外されたディレクトリは
            // 自身も配下も対象外。
            var dirs = EnumerateDirectoriesWithPruning(rootDir, matcher, includeHiddenAndSystemEntries);
            foreach (var dir in dirs)
            {
                // ファイルを含むディレクトリ（またはその祖先）でなければ空ディレクトリ
                if (!directoriesWithFiles.Contains(dir))
                    emptyDirs.Add(dir);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"空ディレクトリ収集中にエラー: {ex.Message}");
        }

        return emptyDirs;
    }

    private static IEnumerable<string> EnumerateDirectoriesWithPruning(string root, GitignoreMatcher matcher, bool includeHiddenAndSystemEntries)
    {
        var enumOpts = CreateNonRecursiveEnumerationOptions(includeHiddenAndSystemEntries);

        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            // Directory.EnumerateDirectories は遅延評価で UnauthorizedAccessException / IOException は
            // foreach 中に発生する。yield return より前に ToArray() で確定させて、列挙中の例外もここで
            // catch できるようにする。
            string[] dirs;
            try
            {
                dirs = Directory.EnumerateDirectories(current, "*", enumOpts).ToArray();
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (IOException) { continue; }

            foreach (var dir in dirs)
            {
                if (ShouldExcludeFile(dir, matcher, root, isDirectory: true, traversalMode: true))
                    continue;
                yield return dir;
                stack.Push(dir);
            }
        }
    }

    /// <summary>
    /// ディレクトリ内のファイルを再帰的に取得する（除外フィルタ適用）
    /// </summary>
    /// <param name="directoryPath">ディレクトリパス</param>
    /// <param name="matcher">除外判定に使う .gitignore 互換マッチャ</param>
    /// <param name="includeHiddenAndSystemEntries">Hidden/System 属性のエントリも列挙対象に含めるか</param>
    /// <returns>ファイルパスのリスト</returns>
    private static IEnumerable<string> GetFilesRecursively(string directoryPath, GitignoreMatcher matcher, bool includeHiddenAndSystemEntries)
    {
        // ユーザーが圧縮対象として明示的に指定したソースルートそのものは除外しない。
        // gitignore のセマンティクスでは ignore ルールは「子エントリ」に適用されるべきで、
        // anchored パターン (例: "/build") は親基準でルート直下にマッチするものであって、
        // ソースルート自身を意味しない。basename だけで判定すると "build" という名前の
        // フォルダを圧縮しようとしただけで空アーカイブになる回帰が起きるので、ルート
        // 自身の除外判定は行わない（配下のサブディレクトリ・ファイルは
        // EnumerateFilesWithPruning 内で root 相対パスにより正しく枝刈りされる）。
        return EnumerateFilesWithPruning(directoryPath, matcher, includeHiddenAndSystemEntries);
    }

    private static IEnumerable<string> EnumerateFilesWithPruning(string root, GitignoreMatcher matcher, bool includeHiddenAndSystemEntries)
    {
        var enumOpts = CreateNonRecursiveEnumerationOptions(includeHiddenAndSystemEntries);

        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            // Directory.Enumerate* は遅延評価で、UnauthorizedAccessException / IOException は
            // foreach 中に発生する。yield return より前に ToArray() で確定させて、列挙中の例外も
            // ここで catch できるようにする。
            string[] files;
            string[] dirs;
            try
            {
                files = Directory.EnumerateFiles(current, "*", enumOpts).ToArray();
                dirs = Directory.EnumerateDirectories(current, "*", enumOpts).ToArray();
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.Log($"アクセス権限がありません: {current}, {ex.Message}");
                continue;
            }
            catch (IOException ex)
            {
                Logger.Log($"ファイル取得中にI/Oエラー: {current}, {ex.Message}");
                continue;
            }

            foreach (var file in files)
            {
                if (!ShouldExcludeFile(file, matcher, root, isDirectory: false, traversalMode: true))
                    yield return file;
            }

            foreach (var dir in dirs)
            {
                if (!ShouldExcludeFile(dir, matcher, root, isDirectory: true, traversalMode: true))
                    stack.Push(dir);
            }
        }
    }

    private static EnumerationOptions CreateNonRecursiveEnumerationOptions(bool includeHiddenAndSystemEntries) => new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        // ReparsePoint（ジャンクション / シンボリックリンク）は常に除外する。手書き DFS
        // (EnumerateFilesWithPruning / EnumerateDirectoriesWithPruning) は RecurseSubdirectories=false で
        // 自前再帰するため .NET 組込みのループ保護が効かず、自己 / 祖先参照ジャンクションを push すると
        // 無限ループ（スキャンがハング）、ツリー外向きジャンクションを辿るとドロップ対象外のファイルを
        // アーカイブに含めてしまう（情報漏えい）。MotwPropagator も同じ理由で ReparsePoint を除外する。
        AttributesToSkip = includeHiddenAndSystemEntries
            ? FileAttributes.ReparsePoint
            : FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
    };



}
