using System.IO;
using System.Security;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Cube.FileSystem.SevenZip;

namespace Lhamiel.Util;

/// <summary>
/// アーカイブ展開機能
/// </summary>
public static class ArchiveExtractor
{
    /// <summary>
    /// 定数: サポートされている展開形式の一覧
    /// </summary>
    private static readonly string[] SupportedExtensions = [".zip", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".tbz", ".lzma", ".tlz", ".xz", ".txz", ".rar", ".lzh", ".cab", ".arj", ".z", ".tZ"];

    /// <summary>
    /// 定数: スマート解凍判定用：無視するシステムディレクトリ名
    /// </summary>
    private static readonly string[] IgnoredSystemDirectories = ["__MACOSX"];

    /// <summary>
    /// 指定されたファイルがサポートされているアーカイブ形式かどうかを確認する
    /// </summary>
    /// <param name="filePath">確認するファイルのパス</param>
    /// <returns>サポートされている形式の場合はtrue、そうでなければfalse</returns>
    public static bool IsSupportedArchiveType(string filePath)
    {
        // 変数: ファイルの拡張子を取得（小文字化）
        // メソッド呼び出し: Path.GetExtensionとToLowerInvariantを呼び出し
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // メソッド呼び出し: サポートされている拡張子リストに含まれているか確認
        return SupportedExtensions.Contains(extension);
    }

    /// <summary>
    /// アーカイブファイルの展開先ディレクトリを取得する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="defaultOutputDir">デフォルトの出力ディレクトリ</param>
    /// <param name="outputToSameDirectory">同じディレクトリに出力するかどうか</param>
    /// <returns>展開先ディレクトリのパス（アーカイブ名フォルダを含む）</returns>
    public static string GetOutputDirectory(string archivePath, string defaultOutputDir, bool outputToSameDirectory = false)
    {
        // 変数: 基準となる出力ディレクトリを取得
        // メソッド呼び出し: GetBaseOutputDirectoryを呼び出し
        var baseDir = GetBaseOutputDirectory(archivePath, defaultOutputDir, outputToSameDirectory);

        // 変数: 拡張子を除いたファイル名を取得
        // メソッド呼び出し: Path.GetFileNameWithoutExtensionを呼び出し
        var fileName = Path.GetFileNameWithoutExtension(archivePath);

        // 基本動作：アーカイブ名フォルダを作成
        // メソッド呼び出し: パスを結合して返す
        return Path.Combine(baseDir, fileName);
    }

    /// <summary>
    /// 基準となる出力ディレクトリを取得（アーカイブ名フォルダを含まない）
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="defaultOutputDir">デフォルトの出力ディレクトリ</param>
    /// <param name="outputToSameDirectory">同じディレクトリに出力するかどうか</param>
    /// <returns>基準となる出力ディレクトリのパス</returns>
    public static string GetBaseOutputDirectory(string archivePath, string defaultOutputDir, bool outputToSameDirectory = false)
    {
        // 変数: アーカイブの親ディレクトリ名を取得
        // メソッド呼び出し: Path.GetDirectoryNameを呼び出し。nullの場合は空文字を使用
        var directory = Path.GetDirectoryName(archivePath) ?? "";

        // 変数: 基準ディレクトリを決定。設定に応じてアーカイブと同じ場所かデフォルト先かを選択
        var baseDirectory = outputToSameDirectory ? directory : defaultOutputDir;

        // メソッド呼び出し: 文字列が空か空白か確認
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            // 変数: 基準ディレクトリが未指定の場合はアーカイブの場所を使用
            baseDirectory = directory;
        }
        return baseDirectory;
    }

    /// <summary>
    /// アーカイブのルート要素が単一かどうかを判定し、その名前を取得する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <returns>単一のルート要素名（見つからないか複数の場合はnull）</returns>
    public static string? GetSingleRootItemName(string archivePath)
    {
        // メソッド呼び出し: ファイルの存在確認
        if (!File.Exists(archivePath)) return null;

        try
        {
            // 変数: アーカイブリーダーの初期化。usingで確実に解放
            using var reader = new ArchiveReader(archivePath);

            // 変数: ルートアイテム名を保持するセット（大文字小文字を区別しない）
            var rootItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // メソッド呼び出し: アーカイブ内の全アイテムを走査
            foreach (var item in reader.Items)
            {
                // パスを正規化（バックスラッシュをスラッシュに）
                // 変数: 正規化されたパス
                // メソッド呼び出し: Replaceを呼び出し
                var path = item.FullName.Replace('\\', '/');

                // 変数: パスをスラッシュで分割
                // メソッド呼び出し: Splitを呼び出し
                var parts = path.Split(['/'], StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length > 0)
                {
                    // 変数: ルート要素（最初のパーツ）
                    var rootItem = parts[0];

                    // システム管理用フォルダ（__MACOSXなど）は無視
                    // メソッド呼び出し: 無視対象に含まれているか確認
                    if (IgnoredSystemDirectories.Contains(rootItem))
                    {
                        continue;
                    }

                    // フォルダのみをルートアイテムとしてカウント
                    // ファイルがルートにある場合は、スマート解凍を適用しない（nullを返す）
                    if (parts.Length == 1 && !item.IsDirectory)
                    {
                        Logger.Log($"ルートにファイルが検出されました: {rootItem}。スマート解凍をスキップします。");
                        return null;
                    }

                    // メソッド呼び出し: セットに追加
                    rootItems.Add(rootItem);

                    // 2つ以上見つかった時点でnull確定
                    // プロパティ: アイテム数を確認
                    if (rootItems.Count > 1)
                    {
                        return null;
                    }
                }
            }

            // アイテム数が1つの場合のみその名前を返す
            // プロパティ: カウント確認。メソッド呼び出し: 最初（唯一）の要素を取得
            return rootItems.Count == 1 ? rootItems.First() : null;
        }
        catch (Exception ex)
        {
            // メソッド呼び出し: ログの記録
            Logger.Log($"アーカイブ構造解析エラー: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// アーカイブのルート要素が単一かどうかを判定する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <returns>ルート要素が単一の場合はtrue</returns>
    public static bool HasSingleRootItem(string archivePath)
    {
        // メソッド呼び出し: ルートアイテム名を取得し、空でないか確認
        return !string.IsNullOrEmpty(GetSingleRootItemName(archivePath));
    }

    /// <summary>
    /// アーカイブ内に二重フォルダ構造が存在するかを判定する
    /// 二重フォルダ: ルートに単一フォルダがあり、その中に同名の単一フォルダのみが存在する状態
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <returns>二重フォルダの場合は内側のフォルダ名、それ以外はnull</returns>
    public static string? DetectDuplicateFolderStructure(string archivePath)
    {
        // メソッド呼び出し: ファイルの存在確認
        if (!File.Exists(archivePath)) return null;

        try
        {
            // 変数: アーカイブリーダーの初期化。usingで確実に解放
            using var reader = new ArchiveReader(archivePath);

            // 変数: ルート要素を保持するセット（フォルダのみ）
            var rootFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // 変数: 第2階層の要素を保持する辞書（キー: ルート要素名、値: 第2階層のアイテム情報セット）
            var secondLevelItems = new Dictionary<string, HashSet<(string name, bool isDirectory)>>(StringComparer.OrdinalIgnoreCase);

            // メソッド呼び出し: アーカイブ内の全アイテムを走査
            foreach (var item in reader.Items)
            {
                // パスを正規化（バックスラッシュをスラッシュに）
                var path = item.FullName.Replace('\\', '/');
                var parts = path.Split(['/'], StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 0) continue;

                // 変数: ルート要素（A階層）
                var rootItem = parts[0];

                // システム管理用フォルダは無視
                if (IgnoredSystemDirectories.Contains(rootItem)) continue;

                // ルート要素がフォルダの場合のみ記録
                if (parts.Length == 1 && item.IsDirectory)
                {
                    rootFolders.Add(rootItem);
                }

                // 2つ以上のルート要素がある場合は二重フォルダではない
                if (rootFolders.Count > 1) return null;

                // 第2階層（B階層）の要素を記録
                if (parts.Length >= 2)
                {
                    var secondLevelItem = parts[1];
                    if (!secondLevelItems.ContainsKey(rootItem))
                    {
                        secondLevelItems[rootItem] = new HashSet<(string, bool)>();
                    }
                    // 第2階層のアイテムがディレクトリかファイルかを判定して記録
                    secondLevelItems[rootItem].Add((secondLevelItem, item.IsDirectory));
                }
            }

            // ルート要素が1つだけのフォルダの場合
            if (rootFolders.Count == 1)
            {
                var rootItem = rootFolders.First();

                // 第2階層の要素を確認
                if (secondLevelItems.TryGetValue(rootItem, out var secondLevel))
                {
                    // 第2階層にフォルダが1つだけあり、かつそれがルート要素と同名の場合
                    var secondLevelFolders = secondLevel.Where(x => x.isDirectory).ToList();
                    if (secondLevelFolders.Count == 1)
                    {
                        var secondLevelItem = secondLevelFolders[0].name;
                        if (string.Equals(rootItem, secondLevelItem, StringComparison.OrdinalIgnoreCase))
                        {
                            // 第2階層に他の要素がないかを確認（同名フォルダのみが存在する必要がある）
                            if (secondLevel.Count == 1)
                            {
                                // 二重フォルダ構造を検出
                                Logger.Log($"二重フォルダ構造を検出: {rootItem}/{secondLevelItem}");
                                return secondLevelItem;
                            }
                        }
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            // メソッド呼び出し: ログの記録
            Logger.Log($"二重フォルダ構造解析エラー: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// アーカイブを展開する（非同期版）
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="outputPath">展開先ディレクトリのパス</param>
    /// <param name="progress">進捗コールバック</param>
    /// <param name="parentWindow">親ウィンドウ（上書き確認ダイアログ用）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <param name="rootItemNameForCleanup">キャンセル時に削除すべき単一ルートアイテム名（スマート解凍用）</param>
    /// <returns>展開処理の完了を表すTask</returns>
    public static async Task ExtractArchiveAsync(string archivePath, string outputPath, IProgress<ProgressInfo>? progress = null, Window? parentWindow = null, CancellationToken cancellationToken = default, string? rootItemNameForCleanup = null)
    {
        // メソッド呼び出し: ログの記録
        Logger.Log($"ExtractArchiveAsync開始: archivePath={archivePath}, outputPath={outputPath}, parentWindow={parentWindow?.GetType().Name ?? "null"}, rootItem={rootItemNameForCleanup ?? "null"}");

        // メソッド呼び出し: キャンセルの確認
        cancellationToken.ThrowIfCancellationRequested();

        // 変数: 実際の展開先ターゲットパス
        // 三項演算子を使用してパスを構築
        var actualTargetDir = rootItemNameForCleanup != null ? Path.Combine(outputPath, rootItemNameForCleanup) : outputPath;

        // 変数: 上書き確認が必要かどうかのフラグ
        // メソッド呼び出し: ディレクトリまたはファイルの存在確認
        var targetExists = Directory.Exists(actualTargetDir) || File.Exists(actualTargetDir);

        // メソッド呼び出し: ログの記録
        Logger.Log($"展開先存在チェック: actualTargetDir={actualTargetDir}, exists={targetExists}");

        // 変数: 上書きが確定したかどうかのフラグ
        var overwriteConfirmed = false;

        if (targetExists && parentWindow != null)
        {
            // 保護されたディレクトリ（デスクトップ自体など）の場合は上書き確認（削除）をさせない
            // メソッド呼び出し: 保護されたディレクトリかチェック
            if (PathValidator.IsProtectedDirectory(actualTargetDir))
            {
                // メソッド呼び出し: ログの記録
                Logger.Log($"上書き不可: 保護されたディレクトリです: {actualTargetDir}", LogLevel.Warning);

                // 例外の投下
                throw new InvalidOperationException($"'{actualTargetDir}' はシステムによって保護されているため、上書き展開できません。別の場所を選択してください。");
            }

            // メソッド呼び出し: ログの記録
            Logger.Log($"上書き確認ダイアログを表示します: {actualTargetDir}");

            // UIスレッドで上書き確認を実行
            // メソッド呼び出し: UIスレッドのディスパッチャーを介してダイアログを表示
            var canOverwrite = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                await FileOverwriteDialog.CanOverwriteFile(archivePath, actualTargetDir, parentWindow));

            // メソッド呼び出し: ログの記録
            Logger.Log($"上書き確認ダイアログ結果: canOverwrite={canOverwrite}");

            if (!canOverwrite)
            {
                // 例外の投下
                throw new OperationCanceledException("ユーザーが展開処理をキャンセルしました。");
            }

            // 変数: 上書き確定フラグをtrueに
            overwriteConfirmed = true;
        }
        else if (targetExists)
        {
            // parentWindow がない場合は自動的に上書き（または既存仕様に合わせる）
            // メソッド呼び出し: ログの記録
            Logger.Log($"上書き確認ダイアログをスキップ（parentWindowなし）: {actualTargetDir}");

            // 変数: 上書き確定フラグをtrueに
            overwriteConfirmed = true;
        }

        // 非同期タスクで展開処理を実行
        // メソッド呼び出し: 新しいタスクを開始
        await Task.Run(async () =>
        {
            // 変数: 進捗コールバック関数の作成
            // ラムダ式を使用してIProgressをActionに変換
            var progressCallback = progress != null ? new Action<ProgressInfo>(p => progress.Report(p)) : null;

            try
            {
                // メソッド呼び出し: 静的メソッドとしてのExtractArchiveを呼び出し
                await ExtractArchive(archivePath, outputPath, progressCallback, parentWindow, overwriteConfirmed, cancellationToken, rootItemNameForCleanup);
            }
            finally
            {
                // ネイティブ側からのコールバックを確実に保護するため、処理完了まで参照を保持
                NativeInteropHelper.KeepAliveCallbacks(progressCallback, progress);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// アーカイブを展開する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="outputPath">展開先ディレクトリのパス</param>
    /// <param name="progressCallback">進捗コールバック</param>
    /// <param name="parentWindow">親ウィンドウ（上書き確認ダイアログ用）</param>
    /// <param name="overwriteConfirmed">上書き確認が既に完了しているかどうか</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <param name="rootItemNameForCleanup">キャンセル時に削除すべき単一ルートアイテム名</param>
    public static async Task ExtractArchive(string archivePath, string outputPath, Action<ProgressInfo>? progressCallback = null, Window? parentWindow = null, bool overwriteConfirmed = false, CancellationToken cancellationToken = default, string? rootItemNameForCleanup = null)
    {
        // メソッド呼び出し: ログの記録
        Logger.Log($"ExtractArchive開始: archivePath={archivePath}, outputPath={outputPath}, overwriteConfirmed={overwriteConfirmed}, rootItem={rootItemNameForCleanup ?? "null"}");

        // スマート解凍の自動判定（引数で指定されていない場合）
        if (rootItemNameForCleanup == null)
        {
            rootItemNameForCleanup = GetSingleRootItemName(archivePath);
            if (rootItemNameForCleanup != null)
            {
                Logger.Log($"スマート解凍を自動判定しました: {rootItemNameForCleanup}");
            }
        }

        // メソッド呼び出し: ファイルの存在確認
        if (!File.Exists(archivePath))
        {
            // 例外の投下
            throw new FileNotFoundException($"アーカイブファイルが見つかりません: {archivePath}");
        }

        // メソッド呼び出し: キャンセルの確認
        cancellationToken.ThrowIfCancellationRequested();

        // 変数: 実際の展開先パス
        // 三項演算子を使用してパスを構築
        var actualTargetDir = rootItemNameForCleanup != null ? Path.Combine(outputPath, rootItemNameForCleanup) : outputPath;

        // 展開先が既に存在する場合の処理
        // メソッド呼び出し: ディレクトリまたはファイルの存在確認
        if (Directory.Exists(actualTargetDir) || File.Exists(actualTargetDir))
        {
            if (!overwriteConfirmed)
            {
                // まだ確認されていない場合はここで確認
                // メソッド呼び出し: ログの記録
                Logger.Log($"ExtractArchive内で上書き確認ダイアログを表示します: {actualTargetDir}");

                // メソッド呼び出し: UIスレッドで上書き確認ダイアログを表示
                var canOverwrite = await Dispatcher.UIThread.InvokeAsync(() =>
                    FileOverwriteDialog.CanOverwriteFile(archivePath, actualTargetDir, parentWindow));

                if (!canOverwrite)
                {
                    // 例外の投下
                    throw new OperationCanceledException("ユーザーが展開処理をキャンセルしました。");
                }
            }

            // 上書きが許可された（または確認済み）の場合は既存の対象を削除
            try
            {
                // メソッド呼び出し: ログの記録
                Logger.Log($"既存の展開先を削除します: {actualTargetDir}");

                // メソッド呼び出し: ディレクトリの存在確認
                if (Directory.Exists(actualTargetDir))
                {
                    try
                    {
                        // メソッド呼び出し: ディレクトリを再帰的に削除
                        Directory.Delete(actualTargetDir, true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                    {
                        // メソッド呼び出し: ログの記録
                        Logger.Log($"削除再試行（属性解除）: {actualTargetDir}");

                        // メソッド呼び出し: 読み取り専用属性を解除
                        RemoveReadOnlyAttributes(actualTargetDir);

                        // メソッド呼び出し: OSのファイルロック解除を少し待機
                        await Task.Delay(100, cancellationToken);

                        // メソッド呼び出し: ディレクトリを再度削除試行
                        Directory.Delete(actualTargetDir, true);
                    }
                }
                // メソッド呼び出し: ファイルの存在確認
                else if (File.Exists(actualTargetDir))
                {
                    // メソッド呼び出し: ファイルを削除
                    File.Delete(actualTargetDir);
                }

                // メソッド呼び出し: ログの記録
                Logger.Log("既存の対象を正常に削除しました。");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // メソッド呼び出し: ログの記録
                Logger.Log($"既存対象の削除に失敗しました: {actualTargetDir}, {ex.Message}");

                // 例外の投下
                throw new InvalidOperationException($"展開先 '{Path.GetFileName(actualTargetDir)}' が使用中か、削除権限がありません。", ex);
            }
        }

        try
        {
            // 出力ディレクトリを作成
            // メソッド呼び出し: ディレクトリの存在確認
            if (!Directory.Exists(outputPath))
            {
                // メソッド呼び出し: ディレクトリを作成
                Directory.CreateDirectory(outputPath);
            }

            // メソッド呼び出し: キャンセルの確認
            cancellationToken.ThrowIfCancellationRequested();

            // ネイティブ側（7z.dll）との連携を確実に保護するため
            // using スコープ内で reader と progress を管理する
            using (var reader = new ArchiveReader(archivePath))
            {
                // メソッド呼び出し: ログの記録
                Logger.Log($"展開処理開始: {archivePath}");

                if (progressCallback != null)
                {
                    // 変数: 最後に報告した進捗率と時間（UIスレッドの負荷軽減用）
                    var lastPercentage = -1;
                    var lastReportTime = Environment.TickCount64;
                    const int reportInterval = 100; // 100ms間隔
                    // 進捗コールバックが複数スレッドから呼ばれる可能性に備えた同期用オブジェクト
                    var progressLock = new object();

                    // 変数: キャンセル可能な進捗報告オブジェクト
                    // using を使用してスコープを維持
                    using var progress = new CancellableProgress<Report>(report =>
                    {
                        // 進捗率を取得（ライブラリの GetRatio() と Report を信じる）
                        var ratio = report.GetRatio();
                        var percentage = (int)(ratio * 100);

                        lock (progressLock)
                        {
                            // 単調増加を保証（Ice アプリケーションの実装パターンに準拠）
                            if (percentage <= lastPercentage && percentage > 0 && percentage < 100)
                            {
                                return;
                            }

                            var currentTime = Environment.TickCount64;

                            // 以下のいずれかの条件を満たす場合のみ報告
                            // 1. 進捗が 0% または 100% (開始と完了を保証)
                            // 2. 前回の報告から 100ms 以上経過しており、かつ進捗率が変化している
                            if (percentage > 0 && percentage < 100)
                            {
                                if (percentage == lastPercentage) return;
                                if (currentTime - lastReportTime < reportInterval) return;
                            }

                            lastPercentage = percentage;
                            lastReportTime = currentTime;
                        }

                        // メソッド呼び出し: 進捗コールバックを実行
                        progressCallback(new ProgressInfo(percentage, "ファイルを展開中..."));
                    }, cancellationToken);

                    // メソッド呼び出し: アーカイブを保存
                    reader.Save(outputPath, progress);

                    // キャンセルされていたらここで一度だけスロー（コールバック内ではスローしない）
                    cancellationToken.ThrowIfCancellationRequested();

                    // Terminate で 100% を保証（Ice アプリケーションの実装パターンに準拠）
                    progressCallback(new ProgressInfo(100, "ファイルを展開中..."));

                    // ネイティブ側のコールバック完了を確実に保証
                    NativeInteropHelper.KeepAliveCallbacks(progress, progressCallback);
                }
                else
                {
                    // メソッド呼び出し: アーカイブを保存
                    reader.Save(outputPath);
                }

                // reader自体の生存も保証
                NativeInteropHelper.KeepAliveCallbacks(reader);
            }

            // メソッド呼び出し: キャンセルの確認
            cancellationToken.ThrowIfCancellationRequested();

            // メソッド呼び出し: ログの記録
            Logger.Log($"アーカイブ展開完了: {archivePath} -> {outputPath}");

            // スマート解凍：二重フォルダの場合はリフトアップを行う
            if (rootItemNameForCleanup != null)
            {
                var rootPath = Path.Combine(outputPath, rootItemNameForCleanup);
                if (Directory.Exists(rootPath))
                {
                    Logger.Log($"スマート解凍：二重フォルダ '{rootItemNameForCleanup}' をリフトアップします");

                    // リフトアップ前の競合チェック
                    var conflicts = new List<string>();
                    foreach (var dir in Directory.GetDirectories(rootPath))
                    {
                        var destDir = Path.Combine(outputPath, Path.GetFileName(dir));
                        if (Directory.Exists(destDir)) conflicts.Add(destDir);
                    }
                    foreach (var file in Directory.GetFiles(rootPath))
                    {
                        var destFile = Path.Combine(outputPath, Path.GetFileName(file));
                        if (File.Exists(destFile)) conflicts.Add(destFile);
                    }

                    // 競合がある場合は確認ダイアログを表示
                    if (conflicts.Count > 0)
                    {
                        Logger.Log($"リフトアップ時に競合が検出されました: {conflicts.Count}件");
                        var conflictMessage = conflicts.Count == 1
                            ? $"リフトアップ時に既存のアイテム '{Path.GetFileName(conflicts[0])}' と競合します。\n\n上書きしてリフトアップを続行しますか？"
                            : $"リフトアップ時に {conflicts.Count} 個の既存アイテムと競合します。\n\n上書きしてリフトアップを続行しますか？";
                        var canLiftUp = await Dispatcher.UIThread.InvokeAsync(async () =>
                            await MessageService.ShowYesNoQuestionAsync(conflictMessage, "リフトアップの確認", parentWindow));

                        if (!canLiftUp)
                        {
                            // メソッド呼び出し: ログの記録
                            Logger.Log("ユーザーがリフトアップをキャンセルしました。二重フォルダのまま残します。");
                            return;
                        }
                    }

                    // ルート要素の中身を outputPath 直下に移動
                    foreach (var dir in Directory.GetDirectories(rootPath))
                    {
                        var destDir = Path.Combine(outputPath, Path.GetFileName(dir));
                        if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
                        Directory.Move(dir, destDir);
                    }
                    foreach (var file in Directory.GetFiles(rootPath))
                    {
                        var destFile = Path.Combine(outputPath, Path.GetFileName(file));
                        if (File.Exists(destFile)) File.Delete(destFile);
                        File.Move(file, destFile);
                    }

                    // 空になったルート要素を削除
                    Directory.Delete(rootPath, true);
                    Logger.Log("リフトアップが完了しました");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 変数: クリーンアップ対象のパス
            var cleanupPath = rootItemNameForCleanup != null ? Path.Combine(outputPath, rootItemNameForCleanup) : outputPath;

            // メソッド呼び出し: ログの記録
            Logger.Log($"展開処理がキャンセルされました。クリーンアップを試行: {cleanupPath}");

            // 保護されたディレクトリは絶対に削除しない
            // メソッド呼び出し: 保護されたディレクトリかチェック
            if (PathValidator.IsProtectedDirectory(cleanupPath))
            {
                // メソッド呼び出し: ログの記録
                Logger.Log($"クリーンアップをスキップ: 保護されたディレクトリです: {cleanupPath}", LogLevel.Warning);
                throw;
            }

            try
            {
                // メソッド呼び出し: ディレクトリの存在確認
                if (Directory.Exists(cleanupPath))
                {
                    // メソッド呼び出し: 属性を解除して削除
                    RemoveReadOnlyAttributes(cleanupPath);
                    Directory.Delete(cleanupPath, true);

                    // メソッド呼び出し: ログの記録
                    Logger.Log($"キャンセルされた展開先を削除しました: {cleanupPath}");
                }
                // メソッド呼び出し: ファイルの存在確認
                else if (File.Exists(cleanupPath))
                {
                    // メソッド呼び出し: ファイルを削除
                    File.Delete(cleanupPath);

                    // メソッド呼び出し: ログの記録
                    Logger.Log($"キャンセルされた展開ファイルを削除しました: {cleanupPath}");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // メソッド呼び出し: ログの記録
                Logger.Log($"キャンセル時のクリーンアップに失敗しました: {cleanupPath}, {ex.Message}", LogLevel.Warning);
            }
            throw;
        }
        catch (Exception ex)
        {
            // 変数: エラー情報の分析結果
            // メソッド呼び出し: エラー内容を分析
            var errorInfo = ArchiveErrorHandler.AnalyzeError(ex, archivePath, outputPath);

            // メソッド呼び出し: ログの記録
            Logger.Log($"アーカイブ展開でエラーが発生しました: {errorInfo.Message}");
            Logger.Log($"エラー詳細: {errorInfo.Details}");

            // 破損ファイルの場合は詳細分析を実行
            if (errorInfo.ErrorType == ArchiveErrorType.CorruptedFile)
            {
                // メソッド呼び出し: ログの記録
                Logger.Log("破損ファイルの詳細分析を実行します");

                // 変数: 破損分析の結果
                // メソッド呼び出し: 破損状態を分析
                var corruptionAnalysis = ArchiveErrorHandler.AnalyzeCorruption(archivePath);

                // メソッド呼び出し: ログの記録
                Logger.Log($"破損分析結果: 破損={corruptionAnalysis.IsCorrupted}, 種類={corruptionAnalysis.CorruptionType}, 回復率={corruptionAnalysis.RecoveryRate:F1}%");
            }

            throw;
        }
    }




    /// <summary>
    /// ファイルまたはディレクトリの読み取り専用属性を削除する
    /// </summary>
    /// <param name="path">対象のファイルまたはディレクトリパス</param>
    internal static void RemoveReadOnlyAttributes(string path)
    {
        /// <summary>
        /// メソッド内: 例外処理をラップして実行するローカル関数
        /// </summary>
        /// <param name="action">実行する処理</param>
        /// <param name="logMessage">エラー時に表示するメッセージ</param>
        void TryExecute(Action action, string logMessage)
        {
            try
            {
                // メソッド呼び出し: 指定された処理を実行
                action();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // 変数: ログメッセージの組み立て
                var fullLogMessage = $"{logMessage}: {ex.Message}";
                // メソッド呼び出し: ログの記録を実行
                Logger.Log(fullLogMessage);
            }
        }

        // ローカル関数呼び出し: 全体の属性削除処理を試行
        TryExecute(() =>
        {
            // メソッド内: ファイルかディレクトリかを判定
            // メソッド呼び出し: ファイルの存在確認
            if (File.Exists(path))
            {
                // ローカル関数呼び出し: ファイル属性の変更を試行
                TryExecute(() =>
                {
                    // 変数: ファイル情報の取得
                    var fileInfo = new FileInfo(path);
                    // プロパティ: ファイルの属性を取得して判定
                    if ((fileInfo.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        // プロパティ: ファイルの属性から読み取り専用を解除
                        fileInfo.Attributes &= ~FileAttributes.ReadOnly;
                    }
                }, $"ファイル属性の変更に失敗しました: {path}");
            }
            // メソッド呼び出し: ディレクトリの存在確認
            else if (Directory.Exists(path))
            {
                // 反復的なヘルパーメソッドを使用して属性を解除
                // メソッド呼び出し: DirectoryInfoの作成と、反復処理の呼び出し
                RemoveReadOnlyAttributesIterative(new DirectoryInfo(path));
            }
        }, $"読み取り専用属性の削除処理でエラーが発生しました: {path}");
    }

    /// <summary>
    /// 指定されたディレクトリとその内容に対して、反復的に読み取り専用属性を解除します
    /// </summary>
    /// <param name="dirInfo">対象ディレクトリの DirectoryInfo インスタンス</param>
    private static void RemoveReadOnlyAttributesIterative(DirectoryInfo dirInfo)
    {
        // メソッド内: 対象ディレクトリの存在確認
        if (!dirInfo.Exists)
        {
            return;
        }

        // 変数: 処理対象のディレクトリを管理するスタック
        // スタックを使用して反復的に処理（スタックオーバーフロー防止）
        var stack = new Stack<DirectoryInfo>();

        // メソッド呼び出し: 初期のディレクトリをスタックに追加
        stack.Push(dirInfo);

        /// <summary>
        /// メソッド内: 例外処理をラップして実行するローカル関数
        /// </summary>
        /// <param name="action">実行する処理</param>
        /// <param name="logMessage">エラー時に表示するメッセージ</param>
        /// <param name="logLevel">ログの重要度レベル</param>
        void TryExecute(Action action, string logMessage, LogLevel logLevel = LogLevel.Error)
        {
            try
            {
                // メソッド呼び出し: 指定された処理を実行
                action();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // 変数: ログメッセージの組み立て
                var fullLogMessage = $"{logMessage}: {ex.Message}";
                // メソッド呼び出し: ログの記録を実行
                Logger.Log(fullLogMessage, logLevel);
            }
        }

        // メソッド内: スタックが空になるまで反復処理を継続
        while (stack.Count > 0)
        {
            // 変数: スタックから取り出した現在のディレクトリ
            var currentDir = stack.Pop();

            // ディレクトリ自体の属性解除
            // ローカル関数呼び出し: 現在のディレクトリの属性変更を試行
            TryExecute(() =>
            {
                // プロパティ: ディレクトリの属性を取得して判定
                if ((currentDir.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    // プロパティ: ディレクトリの属性から読み取り専用を解除
                    currentDir.Attributes &= ~FileAttributes.ReadOnly;
                }
            }, $"ディレクトリ属性変更失敗: {currentDir.FullName}");

            // ファイルの属性解除
            // ローカル関数呼び出し: ディレクトリ内のファイルに対する属性解除を試行
            TryExecute(() =>
            {
                // メソッド呼び出し: 現在のディレクトリ内の全ファイルを取得
                foreach (var file in currentDir.GetFiles())
                {
                    // ローカル関数呼び出し: 個々のファイルに対する属性変更を試行
                    TryExecute(() =>
                    {
                        // プロパティ: ファイルの属性を取得して判定
                        if ((file.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                        {
                            // プロパティ: ファイルの属性から読み取り専用を解除
                            file.Attributes &= ~FileAttributes.ReadOnly;
                        }
                    }, $"個別のファイル属性変更エラー（無視）: {file.FullName}", LogLevel.Warning);
                }
            }, $"ディレクトリアクセスエラー（ファイル属性変更中）: {currentDir.FullName}", LogLevel.Warning);

            // サブディレクトリをスタックに追加
            // ローカル関数呼び出し: サブディレクトリの取得とスタックへの追加を試行
            TryExecute(() =>
            {
                // メソッド呼び出し: 現在のディレクトリ内の全サブディレクトリを取得
                foreach (var subDir in currentDir.GetDirectories())
                {
                    // メソッド呼び出し: サブディレクトリをスタックに追加
                    stack.Push(subDir);
                }
            }, $"サブディレクトリアクセスエラー: {currentDir.FullName}", LogLevel.Warning);
        }
    }
}
