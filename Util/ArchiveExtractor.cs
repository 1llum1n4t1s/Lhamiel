using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private static readonly HashSet<string> IgnoredSystemDirectories = new(StringComparer.OrdinalIgnoreCase) { "__MACOSX" };

    /// <summary>
    /// 定数: スマート解凍判定用：無視するシステムファイル名
    /// </summary>
    private static readonly HashSet<string> IgnoredSystemFiles = new(StringComparer.OrdinalIgnoreCase) { "desktop.ini", "Thumbs.db", ".DS_Store" };

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
    /// アーカイブの先頭2階層の解析結果を保持するデータ構造
    /// </summary>
    public class ArchiveStructureInfo
    {
        /// <summary>
        /// プロパティ: 二重フォルダ構造が検出された場合の内側のフォルダ名
        /// </summary>
        public string? DuplicateFolderName { get; init; }

        /// <summary>
        /// プロパティ: ルートレベルに単一のアイテムのみが存在するかどうか
        /// </summary>
        public bool HasSingleRootItem { get; init; }

        /// <summary>
        /// プロパティ: ルートレベルが単一アイテムの場合、その名前
        /// </summary>
        public string? SingleRootItemName { get; init; }
    }

    /// <summary>
    /// アーカイブの構造を一度の解析で取得する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <returns>解析結果を格納したArchiveStructureInfo</returns>
    public static ArchiveStructureInfo GetArchiveStructureInfo(string archivePath)
    {
        if (!File.Exists(archivePath))
        {
            return new ArchiveStructureInfo { HasSingleRootItem = false };
        }

        try
        {
            using var reader = new ArchiveReader(archivePath);
            var structure = ParseArchiveFirstTwoLevels(reader);

            var rootFolders = structure.RootFolders;
            var rootFiles = structure.RootFiles;

            var allRootItems = new HashSet<string>(rootFolders, StringComparer.OrdinalIgnoreCase);
            allRootItems.UnionWith(rootFiles);
            var rootItemsCount = allRootItems.Count;
            var hasSingleRootItem = rootItemsCount == 1;
            var singleRootItemName = allRootItems.FirstOrDefault();

            string? duplicateFolderName = null;

            // 二重フォルダ構造の判定
            if (rootFolders.Count == 1 && !rootFiles.Any())
            {
                var rootFolderName = rootFolders.First();

                // 第2階層にフォルダが1つのみで、ファイルがないことを確認
                if (structure.SecondLevelFolders.TryGetValue(rootFolderName, out var slFolders) &&
                    slFolders.Count == 1 &&
                    !(structure.SecondLevelFiles.TryGetValue(rootFolderName, out var slFiles) && slFiles.Any()))
                {
                    var secondLevelFolderName = slFolders.First();

                    // ルートフォルダ名と第2階層フォルダ名が同一か確認
                    if (string.Equals(rootFolderName, secondLevelFolderName, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicateFolderName = secondLevelFolderName;
                        Logger.Log($"二重フォルダ構造を検出: {rootFolderName}/{secondLevelFolderName}");
                    }
                }
            }

            return new ArchiveStructureInfo
            {
                DuplicateFolderName = duplicateFolderName,
                HasSingleRootItem = hasSingleRootItem,
                SingleRootItemName = singleRootItemName
            };
        }
        catch (Exception ex)
        {
            Logger.Log($"アーカイブ構造解析エラー: {ex.Message}");
            return new ArchiveStructureInfo { HasSingleRootItem = false };
        }
    }

    /// <summary>
    /// アーカイブの先頭2階層の解析結果を保持する内部データ構造
    /// </summary>
    private class ArchiveStructure
    {
        /// <summary>
        /// プロパティ: ルートレベルのフォルダ名のセット
        /// </summary>
        public HashSet<string> RootFolders { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// プロパティ: ルートレベルのファイル名のセット
        /// </summary>
        public HashSet<string> RootFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// プロパティ: 第2階層のフォルダ名の辞書（キー: ルート名）
        /// </summary>
        public Dictionary<string, HashSet<string>> SecondLevelFolders { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// プロパティ: 第2階層のファイル名の辞書（キー: ルート名）
        /// </summary>
        public Dictionary<string, HashSet<string>> SecondLevelFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// アーカイブの先頭2階層を解析し、フォルダとファイルの情報を格納した構造を返す
    /// </summary>
    /// <param name="reader">アーカイブリーダー</param>
    /// <returns>解析結果を格納したArchiveStructure</returns>
    private static ArchiveStructure ParseArchiveFirstTwoLevels(ArchiveReader reader)
    {
        var structure = new ArchiveStructure();

        // ローカル関数: 辞書のキーに対応する HashSet に値を追加（なければ作成）
        void AddToHierarchy(Dictionary<string, HashSet<string>> dict, string key, string value)
        {
            if (!dict.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                dict[key] = set;
            }
            set.Add(value);
        }

        // メソッド呼び出し: アーカイブ内の全アイテムを1回のループで走査
        foreach (var item in reader.Items)
        {
            // パスを正規化（バックスラッシュをスラッシュに）
            var path = item.FullName.Replace('\\', '/');
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0) continue;

            var rootName = parts[0];
            if (IgnoredSystemDirectories.Contains(rootName)) continue;
            if (!item.IsDirectory && parts.Length > 0 && IgnoredSystemFiles.Contains(parts[^1])) continue;

            if (parts.Length == 1)
            {
                // ルートレベルのアイテム
                if (item.IsDirectory)
                {
                    structure.RootFolders.Add(rootName);
                }
                else
                {
                    structure.RootFiles.Add(rootName);
                }
            }
            else
            {
                // 子要素を持つため、ルートはフォルダ
                structure.RootFolders.Add(rootName);

                var secondLevelName = parts[1];

                // parts.Length == 2 かつ item がファイルの場合のみ SecondLevelFiles に追加
                if (parts.Length == 2 && !item.IsDirectory)
                {
                    AddToHierarchy(structure.SecondLevelFiles, rootName, secondLevelName);
                }
                else
                {
                    // item がディレクトリであるか、より深い階層を持つ場合は、第2階層はフォルダとして扱う
                    AddToHierarchy(structure.SecondLevelFolders, rootName, secondLevelName);
                }
            }
        }

        return structure;
    }

    /// <summary>
    /// 上書き確認ダイアログを表示すべきかどうかを判定する（親フォルダ直下展開時は実際に上書きされるパスのみで判定）
    /// </summary>
    /// <param name="outputPath">展開先ディレクトリのパス</param>
    /// <param name="overwriteCheckPaths">上書き確認を行う対象パス（nullの場合はoutputPathで判定。親フォルダ直下展開時は実際に上書きされるパスのみ渡す）</param>
    /// <returns>上書き対象が存在する場合true</returns>
    public static bool ShouldShowOverwriteDialog(string outputPath, IReadOnlyList<string>? overwriteCheckPaths)
    {
        return overwriteCheckPaths is { Count: > 0 }
            ? overwriteCheckPaths.Any(p => Directory.Exists(p) || File.Exists(p))
            : (Directory.Exists(outputPath) || File.Exists(outputPath));
    }

    /// <summary>
    /// アーカイブを展開する（非同期版）
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="outputPath">展開先ディレクトリのパス</param>
    /// <param name="progress">進捗コールバック</param>
    /// <param name="parentWindow">親ウィンドウ（上書き確認ダイアログ用）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <param name="duplicateFolderName">二重フォルダ構造が検出された場合の内側のフォルダ名（スマート解凍用）</param>
    /// <param name="overwriteCheckPaths">上書き確認を行う対象パス（nullの場合はoutputPathで判定。親フォルダ直下展開時は実際に上書きされるパスのみ渡す）</param>
    /// <returns>展開処理の完了を表すTask</returns>
    public static async Task ExtractArchiveAsync(string archivePath, string outputPath, IProgress<ProgressInfo>? progress = null, Window? parentWindow = null, CancellationToken cancellationToken = default, string? duplicateFolderName = null, IReadOnlyList<string>? overwriteCheckPaths = null)
    {
        // メソッド呼び出し: ログの記録
        Logger.Log($"ExtractArchiveAsync開始: archivePath={archivePath}, outputPath={outputPath}, parentWindow={parentWindow?.GetType().Name ?? "null"}, duplicateFolderName={duplicateFolderName}");

        // メソッド呼び出し: キャンセルの確認
        cancellationToken.ThrowIfCancellationRequested();

        // 変数: 上書き確認が必要かどうかのフラグ
        // メソッド呼び出し: 実際に上書きされるパスのみ存在する場合にダイアログ表示（overwriteCheckPaths未指定時はoutputPathで判定）
        var targetExists = ShouldShowOverwriteDialog(outputPath, overwriteCheckPaths);

        // メソッド呼び出し: ログの記録
        Logger.Log($"展開先存在チェック: outputPath={outputPath}, exists={targetExists}");

        // 変数: 上書きが確定したかどうかのフラグ
        var overwriteConfirmed = false;

        if (targetExists && parentWindow != null)
        {
            // メソッド呼び出し: ログの記録
            Logger.Log($"上書き確認ダイアログを表示します: {outputPath}");

            // UIスレッドで上書き確認を実行
            // メソッド呼び出し: UIスレッドのディスパッチャーを介してダイアログを表示
            var canOverwrite = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                await FileOverwriteDialog.CanOverwriteFile(archivePath, outputPath, parentWindow));

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
            Logger.Log($"上書き確認ダイアログをスキップ（parentWindowなし）: {outputPath}");

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
                await ExtractArchive(archivePath, outputPath, progressCallback, parentWindow, overwriteConfirmed, cancellationToken, duplicateFolderName, overwriteCheckPaths);
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
    /// <param name="duplicateFolderName">二重フォルダ構造が検出された場合の内側のフォルダ名（スマート解凍用）</param>
    /// <param name="overwriteCheckPaths">上書き確認を行う対象パス（nullの場合はoutputPathで判定）</param>
    public static async Task ExtractArchive(string archivePath, string outputPath, Action<ProgressInfo>? progressCallback = null, Window? parentWindow = null, bool overwriteConfirmed = false, CancellationToken cancellationToken = default, string? duplicateFolderName = null, IReadOnlyList<string>? overwriteCheckPaths = null)
    {
        // メソッド呼び出し: ログの記録
        Logger.Log($"ExtractArchive開始: archivePath={archivePath}, outputPath={outputPath}, overwriteConfirmed={overwriteConfirmed}, duplicateFolderName={duplicateFolderName}");

        // メソッド呼び出し: ファイルの存在確認
        if (!File.Exists(archivePath))
        {
            // 例外の投下
            throw new FileNotFoundException($"アーカイブファイルが見つかりません: {archivePath}");
        }

        // メソッド呼び出し: キャンセルの確認
        cancellationToken.ThrowIfCancellationRequested();

        // 変数: 実際に上書きされるパスが存在するか（overwriteCheckPaths未指定時はoutputPathで判定）
        var outputOrOverwriteExists = ShouldShowOverwriteDialog(outputPath, overwriteCheckPaths);

        // 展開先の確認と削除処理
        if (outputOrOverwriteExists)
        {
            if (!overwriteConfirmed)
            {
                // parentWindow がない場合はUI非対応環境（ユニットテスト等）のため自動上書き
                if (parentWindow != null)
                {
                    // メソッド呼び出し: ログの記録
                    Logger.Log($"ExtractArchive内で上書き確認ダイアログを表示します: {outputPath}");

                    // メソッド呼び出し: UIスレッドで上書き確認ダイアログを表示
                    var canOverwrite = await Dispatcher.UIThread.InvokeAsync(() =>
                        FileOverwriteDialog.CanOverwriteFile(archivePath, outputPath, parentWindow));

                    if (!canOverwrite)
                    {
                        // 例外の投下
                        throw new OperationCanceledException("ユーザーが展開処理をキャンセルしました。");
                    }
                }
                else
                {
                    Logger.Log($"上書き確認ダイアログをスキップ（parentWindowなし・UI非対応環境）: {outputPath}");
                }
            }

            // 保護されたディレクトリ（デスクトップ自体など）の場合は上書き確認（削除）をさせない
            // メソッド呼び出し: 保護されたディレクトリかチェック
            if (PathValidator.IsProtectedDirectory(outputPath))
            {
                // メソッド呼び出し: ログの記録
                Logger.Log($"上書き不可: 保護されたディレクトリです: {outputPath}", LogLevel.Warning);

                // 例外の投下
                throw new InvalidOperationException($"'{outputPath}' はシステムによって保護されているため、上書き展開できません。別の場所を選択してください。");
            }
        }

        // 変数: 一時展開先ディレクトリのパスを生成
        // メソッド呼び出し: Path.GetTempPath と Guid を使用してユニークな一時ディレクトリ名を作成
        var tempOutputPath = Path.Combine(Path.GetTempPath(), $"Lhamiel_Extract_{Guid.NewGuid():N}");

        try
        {
            // 一時出力ディレクトリを作成
            // メソッド呼び出し: ディレクトリを作成
            Directory.CreateDirectory(tempOutputPath);

            // メソッド呼び出し: キャンセルの確認
            cancellationToken.ThrowIfCancellationRequested();

            // ネイティブ側（7z.dll）との連携を確実に保護するため
            // using スコープ内で reader と progress を管理する
            using (var reader = new ArchiveReader(archivePath))
            {
                // メソッド呼び出し: ログの記録
                Logger.Log($"一時ディレクトリへの展開処理開始: {archivePath} -> {tempOutputPath}");

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
                    reader.Save(tempOutputPath, progress);

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
                    reader.Save(tempOutputPath);
                }

                // reader自体の生存も保証
                NativeInteropHelper.KeepAliveCallbacks(reader);
            }

            // メソッド呼び出し: キャンセルの確認
            cancellationToken.ThrowIfCancellationRequested();

            // スマート解凍：二重フォルダの場合はリフトアップを行う
            if (duplicateFolderName != null)
            {
                var rootItemName = duplicateFolderName;

                var rootPath = Path.Combine(tempOutputPath, rootItemName);
                var innerFolderPath = Path.Combine(rootPath, rootItemName);

                if (Directory.Exists(innerFolderPath))
                {
                    Logger.Log($"スマート解凍：二重フォルダ '{rootItemName}' をリフトアップします");

                    // 一時ディレクトリを作成して、内側フォルダの中身を移動
                    var tempLiftUpPath = Path.Combine(Path.GetTempPath(), $"Lhamiel_LiftUp_{Guid.NewGuid():N}");
                    try
                    {
                        Directory.CreateDirectory(tempLiftUpPath);

                        // 内側フォルダの中身を一時ディレクトリに移動
                        foreach (var dir in Directory.GetDirectories(innerFolderPath))
                        {
                            var destDir = Path.Combine(tempLiftUpPath, Path.GetFileName(dir));
                            Directory.Move(dir, destDir);
                        }
                        foreach (var file in Directory.GetFiles(innerFolderPath))
                        {
                            var destFile = Path.Combine(tempLiftUpPath, Path.GetFileName(file));
                            File.Move(file, destFile);
                        }

                        // 空になった内側フォルダを削除
                        RemoveReadOnlyAttributes(innerFolderPath);
                        Directory.Delete(innerFolderPath, true);

                        // 一時ディレクトリの中身を外側のフォルダ(rootPath)に移動
                        foreach (var dir in Directory.GetDirectories(tempLiftUpPath))
                        {
                            var destDir = Path.Combine(rootPath, Path.GetFileName(dir));
                            Directory.Move(dir, destDir);
                        }
                        foreach (var file in Directory.GetFiles(tempLiftUpPath))
                        {
                            var destFile = Path.Combine(rootPath, Path.GetFileName(file));
                            File.Move(file, destFile);
                        }

                        Logger.Log("リフトアップが完了しました");
                    }
                    finally
                    {
                        // 一時ディレクトリをクリーンアップ
                        try
                        {
                            if (Directory.Exists(tempLiftUpPath))
                            {
                                RemoveReadOnlyAttributes(tempLiftUpPath);
                                Directory.Delete(tempLiftUpPath, true);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"リフトアップ処理の一時ディレクトリ削除に失敗しました: {tempLiftUpPath}, {ex.Message}", LogLevel.Warning);
                        }
                    }
                }
            }

            // メソッド呼び出し: キャンセルの確認
            cancellationToken.ThrowIfCancellationRequested();

            // 最終的な展開先への移動処理（原子性のため既存は削除せず退避し、移動成功後にバックアップを削除）
            // メソッド呼び出し: ログの記録
            Logger.Log($"一時ディレクトリから最終展開先へ移動します: {tempOutputPath} -> {outputPath}");

            // 変数: 退避したバックアップパス（移動成功後に削除する）
            var backupPaths = new List<string>();

            // 上書きが許可された（または確認済み）の場合は既存の対象を退避（削除せず移動で原子性を確保）
            try
            {
                if (overwriteCheckPaths is { Count: > 0 })
                {
                    // 親フォルダ直下展開時: 実際に上書きされるパスのみ退避（outputPathは退避しない）
                    foreach (var path in overwriteCheckPaths)
                    {
                        var moved = MoveExistingToBackup(path, backupPaths);
                        if (moved)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }
                }
                else
                {
                    // 複数ルート等でoutputPathを新規作成する場合: outputPathを退避してから作成
                    MoveExistingToBackup(outputPath, backupPaths);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Logger.Log($"既存対象の退避に失敗しました: {ex.Message}");
                throw new InvalidOperationException($"展開先の準備中にエラーが発生しました。ファイルが使用中か、削除権限がない可能性があります。", ex);
            }

            // メソッド呼び出し: 展開先ディレクトリを作成（存在しない場合のみ）
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            // tempOutputPath 直下の内容を outputPath に移動
            // メソッド呼び出し: ログの記録
            Logger.Log($"一時ディレクトリの内容を最終展開先に移動します");

            try
            {
                // メソッド呼び出し: 一時ディレクトリから無視対象のシステムフォルダを再帰的に削除
                foreach (var ignoredName in IgnoredSystemDirectories)
                {
                    var dirsToDelete = Directory.GetDirectories(tempOutputPath, ignoredName, SearchOption.AllDirectories)
                        .OrderByDescending(static d => d.Length)
                        .ToList();
                    foreach (var dir in dirsToDelete)
                    {
                        try
                        {
                            if (Directory.Exists(dir))
                            {
                                Directory.Delete(dir, true);
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                        {
                            Logger.Log($"無視対象ディレクトリの削除に失敗: {dir}, {ex.Message}", LogLevel.Warning);
                        }
                    }
                }

                // メソッド呼び出し: 一時ディレクトリから無視対象のシステムファイルを再帰的に削除
                foreach (var ignoredFileName in IgnoredSystemFiles)
                {
                    foreach (var file in Directory.GetFiles(tempOutputPath, ignoredFileName, SearchOption.AllDirectories))
                    {
                        try
                        {
                            if (File.Exists(file))
                            {
                                File.Delete(file);
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                        {
                            Logger.Log($"無視対象ファイルの削除に失敗: {file}, {ex.Message}", LogLevel.Warning);
                        }
                    }
                }

                // メソッド呼び出し: 一時ディレクトリ内の残りのディレクトリを移動
                foreach (var dir in Directory.GetDirectories(tempOutputPath))
                {
                    var destDir = Path.Combine(outputPath, Path.GetFileName(dir));
                    Directory.Move(dir, destDir);
                }

                // メソッド呼び出し: 一時ディレクトリ内のファイルを移動
                foreach (var file in Directory.GetFiles(tempOutputPath))
                {
                    var destFile = Path.Combine(outputPath, Path.GetFileName(file));
                    File.Move(file, destFile);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // メソッド呼び出し: ログの記録
                Logger.Log($"一時ディレクトリの内容移動に失敗しました: {ex.Message}");
                foreach (var backup in backupPaths)
                {
                    Logger.Log($"退避先（復元可能）: {backup}");
                }
                throw new InvalidOperationException($"展開先への内容移動に失敗しました。元の内容は退避先に残っています。", ex);
            }

            // 移動成功後のみバックアップを削除（原子性の完了）
            foreach (var backupPath in backupPaths)
            {
                try
                {
                    if (Directory.Exists(backupPath))
                    {
                        RemoveReadOnlyAttributes(backupPath);
                        Directory.Delete(backupPath, true);
                    }
                    else if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
                {
                    Logger.Log($"バックアップの削除に失敗しました（手動削除可能）: {backupPath}, {ex.Message}", LogLevel.Warning);
                }
            }

            // メソッド呼び出し: ログの記録
            Logger.Log($"アーカイブ展開完了: {archivePath} -> {outputPath}");

        }
        catch (OperationCanceledException)
        {
            // メソッド呼び出し: ログの記録
            Logger.Log($"展開処理がキャンセルされました。一時ディレクトリを削除: {tempOutputPath}");

            try
            {
                // メソッド呼び出し: 一時ディレクトリを削除
                if (Directory.Exists(tempOutputPath))
                {
                    // メソッド呼び出し: 属性を解除して削除
                    RemoveReadOnlyAttributes(tempOutputPath);
                    Directory.Delete(tempOutputPath, true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // メソッド呼び出し: ログの記録
                Logger.Log($"キャンセル時の一時ディレクトリ削除に失敗しました: {tempOutputPath}, {ex.Message}", LogLevel.Warning);
            }
            throw;
        }
        catch (Exception ex)
        {
            // 一時ディレクトリのクリーンアップ
            try
            {
                if (Directory.Exists(tempOutputPath))
                {
                    RemoveReadOnlyAttributes(tempOutputPath);
                    Directory.Delete(tempOutputPath, true);
                }
            }
            catch (Exception cleanupEx)
            {
                Logger.Log($"エラー発生時の一時ディレクトリ削除に失敗しました: {tempOutputPath}, {cleanupEx.Message}", LogLevel.Warning);
            }

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
    /// 既存のファイルまたはディレクトリを退避用バックアップパスへ移動する（原子性のため削除せず移動）
    /// </summary>
    /// <param name="path">退避対象のパス（ファイルまたはディレクトリ）</param>
    /// <param name="backupPaths">退避先パスを追加するリスト</param>
    /// <returns>退避を行った場合はtrue、対象が存在しなかった場合はfalse</returns>
    private static bool MoveExistingToBackup(string path, List<string> backupPaths)
    {
        if (Directory.Exists(path))
        {
            var backupPath = path + ".Lhamiel_backup_" + Guid.NewGuid().ToString("N");
            RemoveReadOnlyAttributes(path);
            Directory.Move(path, backupPath);
            backupPaths.Add(backupPath);
            return true;
        }
        if (File.Exists(path))
        {
            var backupPath = path + ".Lhamiel_backup_" + Guid.NewGuid().ToString("N");
            File.Move(path, backupPath);
            backupPaths.Add(backupPath);
            return true;
        }
        return false;
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
