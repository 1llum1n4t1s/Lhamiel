using System.IO;
using Cube.FileSystem.SevenZip;

namespace Lhamiel.Util;

/// <summary>
/// アーカイブ展開機能
/// </summary>
public class ArchiveExtractor
{
    /// <summary>
    /// サポートされている展開形式の一覧
    /// </summary>
    private static readonly string[] SupportedExtensions = { ".zip", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".tbz", ".lzma", ".tlz", ".xz", ".txz", ".rar", ".lzh", ".cab", ".arj", ".z", ".tZ", ".exe" };

    /// <summary>
    /// 指定されたファイルがサポートされているアーカイブ形式かどうかを確認する
    /// </summary>
    /// <param name="filePath">確認するファイルのパス</param>
    /// <returns>サポートされている形式の場合はtrue、そうでなければfalse</returns>
    public static bool IsSupportedArchiveType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        if (!SupportedExtensions.Contains(extension))
        {
            return false;
        }
        
        // .exeファイルの場合は自己展開圧縮ファイルかどうかを確認
        if (extension == ".exe")
        {
            return ArchiveFormatDetector.IsSelfExtractingArchive(filePath);
        }
        
        return true;
    }

    /// <summary>
    /// アーカイブファイルの展開先ディレクトリを取得する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="defaultOutputDir">デフォルトの出力ディレクトリ</param>
    /// <param name="outputToSameDirectory">同じディレクトリに出力するかどうか</param>
    /// <returns>展開先ディレクトリのパス</returns>
    public static string GetOutputDirectory(string archivePath, string defaultOutputDir, bool outputToSameDirectory = false)
    {
        var directory = Path.GetDirectoryName(archivePath) ?? "";
        var fileName = Path.GetFileNameWithoutExtension(archivePath);
        var baseDirectory = outputToSameDirectory ? directory : defaultOutputDir;

        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = directory;
        }
        
        // アーカイブの内容をチェックして、二重フォルダを避ける
        var adjustedFileName = GetAdjustedFileName(archivePath, fileName);
        
        // 空文字列が返された場合は、アーカイブファイルと同じディレクトリに展開
        if (string.IsNullOrEmpty(adjustedFileName))
        {
            return baseDirectory;
        }
        
        return Path.Combine(baseDirectory, adjustedFileName);
    }

    /// <summary>
    /// アーカイブの内容をチェックして、アーカイブ名を取得する
    /// 仕様：
    /// ケース1: ルートアイテムが1つ＋フォルダ → ルートフォルダ名を返す（直接展開、二重フォルダ防止）
    /// ケース2: ルートアイテムが複数 → アーカイブ名フォルダを返す
    /// ケース3: ルートアイテムが1つ＋ファイル → 空文字列を返す（ベースディレクトリに直接展開）
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="defaultFileName">デフォルトのファイル名（アーカイブ名）</param>
    /// <returns>ルートフォルダ名またはアーカイブ名、または空文字列</returns>
    private static string GetAdjustedFileName(string archivePath, string defaultFileName)
    {
        try
        {
            using var reader = new ArchiveReader(archivePath);

            // アーカイブの内容を取得
            var archiveContents = reader.Items.Select(item => item.FullName).ToList();

            if (!archiveContents.Any())
                return defaultFileName;

            // ルートレベルのアイテムを取得
            var rootItems = GetRootLevelItems(archiveContents);

            // ケース1: ルートアイテムが1つ＋フォルダ → ルートフォルダ名を返す
            if (rootItems.Count == 1 && rootItems[0].IsDirectory)
            {
                var rootFolderName = rootItems[0].Name;
                Logger.Log($"ケース1: ルートアイテムが1つ＋フォルダ。ルートフォルダ名を使用します: {rootFolderName}");
                return rootFolderName;
            }

            // ケース3: ルートアイテムが1つ＋ファイル → 空文字列を返す
            if (rootItems.Count == 1 && !rootItems[0].IsDirectory)
            {
                Logger.Log($"ケース3: ルートアイテムが1つ＋ファイル。展開先をベースディレクトリにします");
                return "";
            }

            // ケース2: ルートアイテムが複数 → アーカイブ名フォルダを作成
            Logger.Log($"ケース2: ルートアイテムが複数。アーカイブ名フォルダを作成します: {defaultFileName}");
            return defaultFileName;
        }
        catch (Exception ex)
        {
            Logger.Log($"アーカイブ内容のチェックでエラーが発生しました: {archivePath}, {ex.Message}");
            return defaultFileName;
        }
    }

    /// <summary>
    /// 進捗レポートから現在処理中のファイル名を取得する
    /// </summary>
    private static string GetReportCurrentFileName(Report report)
    {
        try
        {
            var reportType = report.GetType();
            Logger.Log($"Report型: {reportType.FullName}", LogLevel.Debug);

            // すべてのプロパティをログ出力（デバッグ用）
            var properties = reportType.GetProperties();
            foreach (var prop in properties)
            {
                try
                {
                    var value = prop.GetValue(report);
                    var valueStr = value?.ToString() ?? "(null)";
                    Logger.Log($"  {prop.Name} = {valueStr}", LogLevel.Debug);
                }
                catch
                {
                    Logger.Log($"  {prop.Name} = (エラー)", LogLevel.Debug);
                }
            }

            // Entry プロパティの中を確認
            var entryProperty = reportType.GetProperty("Entry");
            if (entryProperty != null)
            {
                var entry = entryProperty.GetValue(report);
                if (entry != null)
                {
                    var entryType = entry.GetType();
                    Logger.Log($"Entry型: {entryType.FullName}", LogLevel.Debug);
                    var entryProps = entryType.GetProperties();
                    foreach (var prop in entryProps)
                    {
                        try
                        {
                            var value = prop.GetValue(entry);
                            var valueStr = value?.ToString() ?? "(null)";
                            if (valueStr.Length > 100)
                                valueStr = valueStr.Substring(0, 100);
                            Logger.Log($"  Entry.{prop.Name} = {valueStr}", LogLevel.Debug);
                        }
                        catch
                        {
                            Logger.Log($"  Entry.{prop.Name} = (エラー)", LogLevel.Debug);
                        }
                    }

                    // FullName を取得
                    var fullNameProperty = entryType.GetProperty("FullName");
                    if (fullNameProperty != null)
                    {
                        var fullName = fullNameProperty.GetValue(entry)?.ToString();
                        if (!string.IsNullOrWhiteSpace(fullName))
                        {
                            Logger.Log($"✓ ファイル名を取得しました: {fullName}", LogLevel.Info);
                            return fullName;
                        }
                    }

                    var nameProperty = entryType.GetProperty("Name");
                    if (nameProperty != null)
                    {
                        var name = nameProperty.GetValue(entry)?.ToString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            Logger.Log($"✓ ファイル名を取得しました（Name）: {name}", LogLevel.Info);
                            return name;
                        }
                    }
                }
            }

            Logger.Log("✗ ファイル名を取得できませんでした", LogLevel.Warning);
            return string.Empty;
        }
        catch (Exception ex)
        {
            Logger.Log($"進捗レポートのファイル名取得に失敗しました: {ex.Message}", LogLevel.Debug);
            return string.Empty;
        }
    }



    /// <summary>
    /// アーカイブを展開する（非同期版）
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="outputPath">展開先ディレクトリのパス</param>
    /// <param name="progress">進捗コールバック</param>
    /// <param name="parentWindow">親ウィンドウ（上書き確認ダイアログ用）</param>
    /// <returns>展開処理の完了を表すTask</returns>
    public static async Task ExtractArchiveAsync(string archivePath, string outputPath, IProgress<ProgressInfo>? progress = null, System.Windows.Window? parentWindow = null, CancellationToken cancellationToken = default)
    {
        Logger.Log($"ExtractArchiveAsync開始: archivePath={archivePath}, outputPath={outputPath}, parentWindow={parentWindow?.GetType().Name ?? "null"}");

        cancellationToken.ThrowIfCancellationRequested();

        // 上書き確認が必要かどうかを事前にチェック
        var needsOverwriteConfirmation = Directory.Exists(outputPath);
        Logger.Log($"展開先ディレクトリ存在チェック: outputPath={outputPath}, exists={needsOverwriteConfirmation}");

        if (needsOverwriteConfirmation && parentWindow != null)
        {
            Logger.Log("上書き確認ダイアログを表示します");
            // UIスレッドで上書き確認を実行
            var canOverwrite = await parentWindow.Dispatcher.InvokeAsync(() =>
                FileOverwriteDialog.CanOverwriteFile(archivePath, outputPath, parentWindow));

            Logger.Log($"上書き確認ダイアログ結果: canOverwrite={canOverwrite}");

            if (!canOverwrite)
            {
                throw new OperationCanceledException("ユーザーが展開処理をキャンセルしました。");
            }
        }
        else
        {
            Logger.Log($"上書き確認ダイアログをスキップ: needsOverwriteConfirmation={needsOverwriteConfirmation}, parentWindow={parentWindow != null}");
        }

        // 非同期タスクで展開処理を実行
        await Task.Run(() =>
        {
            var extractor = new ArchiveExtractor();
            var progressCallback = progress != null ? new Action<ProgressInfo>(p => progress.Report(p)) : null;
            extractor.ExtractArchive(archivePath, outputPath, progressCallback, parentWindow, needsOverwriteConfirmation, cancellationToken);
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
    public void ExtractArchive(string archivePath, string outputPath, Action<ProgressInfo>? progressCallback = null, System.Windows.Window? parentWindow = null, bool overwriteConfirmed = false, CancellationToken cancellationToken = default)
    {
        Logger.Log($"ExtractArchive開始: archivePath={archivePath}, outputPath={outputPath}, overwriteConfirmed={overwriteConfirmed}");

        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"アーカイブファイルが見つかりません: {archivePath}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 展開先ディレクトリが既に存在する場合は上書き確認
        if (Directory.Exists(outputPath) && !overwriteConfirmed)
        {
            Logger.Log("ExtractArchive内で上書き確認ダイアログを表示します");
            var canOverwrite = FileOverwriteDialog.CanOverwriteFile(archivePath, outputPath, parentWindow);
            Logger.Log($"ExtractArchive内の上書き確認結果: canOverwrite={canOverwrite}");

            if (!canOverwrite)
            {
                throw new OperationCanceledException("ユーザーが展開処理をキャンセルしました。");
            }

            // 上書きが許可された場合は既存ディレクトリを削除
            try
            {
                RemoveReadOnlyAttributes(outputPath);
                Directory.Delete(outputPath, true);
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.Log($"ディレクトリの削除に失敗しました（アクセス拒否）: {outputPath}, {ex.Message}");
                throw new InvalidOperationException($"展開先ディレクトリ '{Path.GetFileName(outputPath)}' が他のアプリケーションで使用されているため削除できません。\nディレクトリを閉じてから再度お試しください。", ex);
            }
            catch (IOException ex)
            {
                Logger.Log($"ディレクトリの削除に失敗しました（I/Oエラー）: {outputPath}, {ex.Message}");
                throw new InvalidOperationException($"展開先ディレクトリ '{Path.GetFileName(outputPath)}' の削除に失敗しました。\nディレクトリ内のファイルが使用中である可能性があります。", ex);
            }
        }

        var outputPrepared = false;
        var tempExtractionPath = "";

        try
        {
            // 出力ディレクトリを作成
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
                outputPrepared = true;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // ルートフォルダのリフトアップが必要かどうかを確認
            tempExtractionPath = GetTemporaryExtractionPath(archivePath, outputPath);
            var useTempPath = !string.IsNullOrEmpty(tempExtractionPath) && tempExtractionPath != outputPath;

            Logger.Log($"一時展開パス使用: {useTempPath}, パス: {tempExtractionPath}");

            var extractPath = useTempPath ? tempExtractionPath : outputPath;

            using (var reader = new ArchiveReader(archivePath))
            {
                // アーカイブ内のエントリ情報を取得
                var entries = reader.Items.ToList();
                var totalEntries = entries.Count;
                Logger.Log($"展開するエントリ数: {totalEntries}");

                // 進捗報告を設定
                if (progressCallback != null)
                {
                    var lastIndex = 0;
                    var progress = new Progress<Report>(report =>
                    {
                        // キャンセルされている場合は処理をスキップ（スレッドプールから実行されるため、例外をスローしない）
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        var percentage = report.TotalBytes > 0 ? (int)((report.Bytes * 100) / report.TotalBytes) : 0;

                        // 進捗に基づいてエントリのインデックスを推測
                        var estimatedIndex = totalEntries > 0 
                            ? Math.Min((int)(percentage * totalEntries / 100.0), totalEntries - 1)
                            : 0;

                        var currentFileName = string.Empty;
                        if (estimatedIndex >= 0 && estimatedIndex < entries.Count)
                        {
                            currentFileName = entries[estimatedIndex].FullName ?? string.Empty;
                            lastIndex = estimatedIndex;
                        }

                        var status = string.IsNullOrWhiteSpace(currentFileName)
                            ? "ファイルを展開中..."
                            : $"展開中: {Path.GetFileName(currentFileName)}";
                        Logger.Log($"展開進捗: {percentage}%, エントリ {estimatedIndex + 1}/{totalEntries}, ファイル: {Path.GetFileName(currentFileName)}", LogLevel.Debug);
                        progressCallback(new ProgressInfo(percentage, status, currentFileName));
                    });

                    reader.Save(extractPath, progress);
                }
                else
                {
                    reader.Save(extractPath);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 一時パスから本来のパスにファイルをリフトアップ
            if (useTempPath && Directory.Exists(tempExtractionPath))
            {
                LiftUpFilesFromTemporaryPath(tempExtractionPath, outputPath);

                // 一時ディレクトリを削除
                try
                {
                    RemoveReadOnlyAttributes(tempExtractionPath);
                    Directory.Delete(tempExtractionPath, true);
                    Logger.Log($"一時ディレクトリを削除しました: {tempExtractionPath}");
                }
                catch (Exception ex)
                {
                    Logger.Log($"一時ディレクトリの削除に失敗しました: {tempExtractionPath}, {ex.Message}");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            Logger.Log($"アーカイブ展開完了: {archivePath} -> {outputPath}");
        }
        catch (OperationCanceledException)
        {
            if (outputPrepared && Directory.Exists(outputPath))
            {
                try
                {
                    RemoveReadOnlyAttributes(outputPath);
                    Directory.Delete(outputPath, true);
                }
                catch (Exception ex)
                {
                    Logger.Log($"キャンセル時の出力ディレクトリ削除に失敗しました: {outputPath}, {ex.Message}");
                }
            }

            if (!string.IsNullOrEmpty(tempExtractionPath) && Directory.Exists(tempExtractionPath))
            {
                try
                {
                    RemoveReadOnlyAttributes(tempExtractionPath);
                    Directory.Delete(tempExtractionPath, true);
                }
                catch (Exception ex)
                {
                    Logger.Log($"キャンセル時の一時ディレクトリ削除に失敗しました: {tempExtractionPath}, {ex.Message}");
                }
            }

            throw;
        }
        catch (Exception ex)
        {
            // 詳細なエラー分析を実行
            var errorInfo = ArchiveErrorHandler.AnalyzeError(ex, archivePath, outputPath);
            Logger.Log($"アーカイブ展開でエラーが発生しました: {errorInfo.Message}");
            Logger.Log($"エラー詳細: {errorInfo.Details}");

            // 破損ファイルの場合は詳細分析を実行
            if (errorInfo.ErrorType == ArchiveErrorType.CorruptedFile)
            {
                Logger.Log("破損ファイルの詳細分析を実行します");
                var corruptionAnalysis = ArchiveErrorHandler.AnalyzeCorruption(archivePath);
                Logger.Log($"破損分析結果: 破損={corruptionAnalysis.IsCorrupted}, 種類={corruptionAnalysis.CorruptionType}, 回復率={corruptionAnalysis.RecoveryRate:F1}%");
            }

            throw;
        }
    }

    /// <summary>
    /// 一時展開パスが必要かどうかを確認して返す
    /// ケース1: ルートアイテムが1つ＋フォルダ → 直接展開（outputPath を返す、リフトアップなし）
    /// ケース2: ルートアイテムが複数 → アーカイブ名フォルダ作成用一時パスを返す
    /// ケース3: ルートアイテムが1つ＋ファイル → 直接展開（outputPath を返す）
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="outputPath">展開先ディレクトリのパス</param>
    /// <returns>一時展開パス、または直接展開の場合は outputPath と同じ値</returns>
    private static string GetTemporaryExtractionPath(string archivePath, string outputPath)
    {
        try
        {
            using var reader = new ArchiveReader(archivePath);

            // アーカイブの内容を取得
            var archiveContents = reader.Items.Select(item => item.FullName).ToList();

            if (!archiveContents.Any())
                return outputPath;

            // ルートレベルのアイテムを取得
            var rootItems = GetRootLevelItems(archiveContents);

            // ケース1: ルートアイテムが1つ＋フォルダ（直接展開、リフトアップなし）
            if (rootItems.Count == 1 && rootItems[0].IsDirectory)
            {
                Logger.Log($"ケース1: ルートアイテムが1つ＋フォルダ。直接展開します（リフトアップなし）: {outputPath}");
                return outputPath;
            }

            // ケース2: ルートアイテムが複数
            if (rootItems.Count > 1)
            {
                // 一時ディレクトリを作成（アーカイブ名フォルダ作成用）
                var tempBasePath = Path.Combine(Path.GetTempPath(), "Lhamiel");
                if (!Directory.Exists(tempBasePath))
                {
                    Directory.CreateDirectory(tempBasePath);
                }
                var tempDirName = Path.GetFileName(outputPath) + "_temp_" + Guid.NewGuid().ToString().Substring(0, 8);
                var tempPath = Path.Combine(tempBasePath, tempDirName);

                Logger.Log($"ケース2: ルートアイテムが複数（{rootItems.Count}個）。アーカイブ名フォルダを作成します。一時展開パスを返す: {tempPath}");
                return tempPath;
            }

            // ケース3: ルートアイテムが1つ＋ファイル（直接展開）
            Logger.Log($"ケース3: ルートアイテムが1つ＋ファイル。直接展開します: {outputPath}");
            return outputPath;
        }
        catch (Exception ex)
        {
            Logger.Log($"展開パス取得でエラーが発生しました: {archivePath}, {ex.Message}");
            return outputPath;
        }
    }

    /// <summary>
    /// ルートレベルのアイテムを取得する
    /// </summary>
    /// <param name="archiveContents">アーカイブの全アイテムパス</param>
    /// <returns>ルートレベルのアイテムリスト</returns>
    private static List<(string Name, bool IsDirectory)> GetRootLevelItems(List<string> archiveContents)
    {
        var rootItems = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in archiveContents)
        {
            // パスをノーマライズ（バックスラッシュをスラッシュに統一）
            var normalizedPath = path.Replace('\\', '/');

            // 末尾のスラッシュを削除
            normalizedPath = normalizedPath.TrimEnd('/');

            // ルートレベルの要素を抽出
            var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length > 0)
            {
                var rootName = parts[0];
                var isDirectory = path.EndsWith("/") || path.EndsWith("\\") || parts.Length > 1;

                if (!rootItems.ContainsKey(rootName))
                {
                    rootItems[rootName] = isDirectory;
                }
                else
                {
                    // isDirectory が true の場合は true に更新（ファイルだと思っていたものがディレクトリだった場合）
                    rootItems[rootName] = rootItems[rootName] || isDirectory;
                }
            }
        }

        return rootItems.Select(kvp => (kvp.Key, kvp.Value)).ToList();
    }

    /// <summary>
    /// 一時展開パスからファイルをアーカイブ名フォルダに配置する
    /// ケース2: ルートアイテムが複数 → アーカイブ名のフォルダを作成して中身を配置
    /// </summary>
    /// <param name="tempPath">一時展開パス</param>
    /// <param name="outputPath">本来の展開先パス</param>
    private static void LiftUpFilesFromTemporaryPath(string tempPath, string outputPath)
    {
        try
        {
            Logger.Log($"一時パスの処理を開始: {tempPath} -> {outputPath}");

            // tempPath 直下のアイテムを取得
            var directories = Directory.GetDirectories(tempPath);
            var files = Directory.GetFiles(tempPath);

            Logger.Log($"tempPath 直下: ディレクトリ={directories.Length}個, ファイル={files.Length}個");

            // ケース2: 複数のルートアイテム → アーカイブ名フォルダを作成
            if (directories.Length > 0 || files.Length > 0)
            {
                Logger.Log($"ケース2: ルートアイテムが複数またはファイルとディレクトリが混在。アーカイブ名フォルダを作成します");

                // 本来のパスがまだ存在しない場合は作成
                if (!Directory.Exists(outputPath))
                {
                    Directory.CreateDirectory(outputPath);
                }

                // tempPath のすべてのファイルとフォルダを outputPath に移動
                foreach (var file in Directory.GetFiles(tempPath))
                {
                    var destFile = Path.Combine(outputPath, Path.GetFileName(file));
                    RemoveReadOnlyAttributes(file);

                    if (File.Exists(destFile))
                    {
                        File.Delete(destFile);
                    }

                    File.Move(file, destFile);
                    Logger.Log($"ファイルを移動: {file} -> {destFile}");
                }

                foreach (var dir in Directory.GetDirectories(tempPath))
                {
                    var destDir = Path.Combine(outputPath, Path.GetFileName(dir));

                    if (Directory.Exists(destDir))
                    {
                        RemoveReadOnlyAttributes(destDir);
                        Directory.Delete(destDir, true);
                    }

                    Directory.Move(dir, destDir);
                    Logger.Log($"ディレクトリを移動: {dir} -> {destDir}");
                }

                Logger.Log("ケース2: アーカイブ名フォルダの作成が完了しました");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"一時パスの処理でエラーが発生しました: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// ファイルまたはディレクトリの読み取り専用属性を削除する
    /// </summary>
    /// <param name="path">対象のファイルまたはディレクトリパス</param>
    private static void RemoveReadOnlyAttributes(string path)
    {
        try
        {
            // ファイルかディレクトリかを判定
            if (File.Exists(path))
            {
                try
                {
                    var fileInfo = new FileInfo(path);
                    if ((fileInfo.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        fileInfo.Attributes &= ~FileAttributes.ReadOnly;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"ファイル属性の変更に失敗しました: {path}, {ex.Message}");
                }
            }
            else if (Directory.Exists(path))
            {
                // ディレクトリ内のすべてのファイルとサブディレクトリを再帰的に処理
                foreach (var filePath in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        if ((fileInfo.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                        {
                            fileInfo.Attributes &= ~FileAttributes.ReadOnly;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"ファイル属性の変更に失敗しました: {filePath}, {ex.Message}");
                    }
                }

                // ディレクトリ自体の読み取り専用属性も削除
                try
                {
                    var dirInfo = new DirectoryInfo(path);
                    if ((dirInfo.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        dirInfo.Attributes &= ~FileAttributes.ReadOnly;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"ディレクトリ属性の変更に失敗しました: {path}, {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"読み取り専用属性の削除処理でエラーが発生しました: {path}, {ex.Message}");
        }
    }

    /// <summary>
    /// 特定のファイルを展開する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="outputPath">展開先ディレクトリのパス</param>
    /// <param name="fileNames">展開するファイル名のリスト</param>
    /// <param name="progressCallback">進捗コールバック</param>
    public void ExtractSpecificFiles(string archivePath, string outputPath, IEnumerable<string> fileNames, Action<int>? progressCallback = null)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"アーカイブファイルが見つかりません: {archivePath}");
            
        var fileNameList = fileNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!fileNameList.Any())
            throw new ArgumentException("展開するファイルが指定されていません。");
            
        if (!Directory.Exists(outputPath))
            Directory.CreateDirectory(outputPath);

        try
        {
            using var reader = new ArchiveReader(archivePath);
            var tempPath = Path.Combine(Path.GetTempPath(), $"lhamiel-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempPath);

            try
            {
                // 現在のライブラリでは特定ファイル展開は制限されているため、
                // 一時展開してから必要なものだけをコピー
                reader.Save(tempPath);

                var items = reader.Items.ToList();
                var matchedCount = 0;
                var totalTargets = fileNameList.Count;
                var missingFiles = new List<string>();

                foreach (var fileName in fileNameList)
                {
                    var item = items.FirstOrDefault(x => string.Equals(x.FullName, fileName, StringComparison.OrdinalIgnoreCase));
                    if (item == null)
                    {
                        missingFiles.Add(fileName);
                        continue;
                    }

                    CopyExtractedItem(tempPath, outputPath, item.FullName, item.IsDirectory);
                    matchedCount++;
                    var progress = totalTargets == 0 ? 100 : (int)((double)matchedCount / totalTargets * 100);
                    progressCallback?.Invoke(progress);
                }

                if (missingFiles.Any())
                {
                    Logger.Log($"指定されたファイルがアーカイブ内に見つかりません: {string.Join(", ", missingFiles)}", LogLevel.Warning);
                }

                Logger.Log($"特定ファイル展開完了: {string.Join(", ", fileNameList)}");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempPath))
                    {
                        Directory.Delete(tempPath, true);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"一時ディレクトリ削除に失敗しました: {tempPath}, {ex.Message}", LogLevel.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"特定ファイル展開でエラーが発生しました: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 一時展開した内容からファイルをコピーする
    /// </summary>
    private static void CopyExtractedItem(string tempPath, string outputPath, string fullName, bool isDirectory)
    {
        var sourcePath = Path.Combine(tempPath, fullName);
        var targetPath = Path.Combine(outputPath, fullName);

        if (isDirectory)
        {
            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }
            return;
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("展開されたファイルが見つかりません。", sourcePath);
        }

        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        File.Copy(sourcePath, targetPath, true);
    }

    /// <summary>
    /// アーカイブの内容を一覧表示
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <returns>アーカイブ内のファイル一覧</returns>
    public List<string> ListArchiveContents(string archivePath)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"アーカイブファイルが見つかりません: {archivePath}");
            
        var contents = new List<string>();
        
        try
        {
        using var reader = new ArchiveReader(archivePath);
            
            // Items プロパティを使用してアーカイブ内容を取得
            foreach (var item in reader.Items)
            {
                contents.Add(item.FullName);
            }
            
            Logger.Log($"アーカイブ内容確認完了: {contents.Count}個のファイル");
        }
        catch (Exception ex)
        {
            Logger.Log($"アーカイブ内容確認でエラーが発生しました: {ex.Message}");
            throw;
        }
        
        return contents;
    }

    /// <summary>
    /// 既存ファイルとの競合をチェックする
    /// </summary>
    /// <param name="reader">アーカイブリーダー</param>
    /// <param name="outputPath">出力先ディレクトリ</param>
    /// <returns>競合するファイルのパス一覧</returns>
    private static List<string> CheckForConflictingFiles(ArchiveReader reader, string outputPath)
    {
        Logger.Log($"CheckForConflictingFiles開始: outputPath={outputPath}");
        var conflictingFiles = new List<string>();
        
        try
        {
            var itemCount = 0;
            foreach (var item in reader.Items)
            {
                itemCount++;
                if (!item.IsDirectory)
                {
                    // アーカイブ内のファイルパスから、実際の展開先パスを計算
                    var relativePath = item.FullName;
                    var targetPath = Path.Combine(outputPath, relativePath);
                    
                    Logger.Log($"チェック中: アーカイブ内パス={relativePath}, 展開先パス={targetPath}");
                    
                    if (File.Exists(targetPath))
                    {
                        conflictingFiles.Add(targetPath);
                        Logger.Log($"競合ファイルを発見: {targetPath}");
                    }
                }
            }
            Logger.Log($"CheckForConflictingFiles完了: 総アイテム数={itemCount}, 競合ファイル数={conflictingFiles.Count}");
        }
        catch (Exception ex)
        {
            Logger.Log($"競合ファイルチェックでエラーが発生しました: {ex.Message}");
        }
        
        return conflictingFiles;
    }

    /// <summary>
    /// アーカイブの詳細情報を取得
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <returns>アーカイブ内のファイル詳細情報一覧</returns>
    public List<ArchiveFileInfo> GetArchiveFileInfos(string archivePath)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException($"アーカイブファイルが見つかりません: {archivePath}");
            
        var fileInfos = new List<ArchiveFileInfo>();
        
        try
        {
            using var reader = new ArchiveReader(archivePath);
            
            // Items プロパティを使用してアーカイブ内容の詳細情報を取得
            foreach (var item in reader.Items)
            {
                var fileInfo = new ArchiveFileInfo
                {
                    Index = (uint)item.Index,
                    Path = item.FullName,
                    Name = Path.GetFileName(item.FullName),
                    IsDirectory = item.IsDirectory,
                    Size = 0, // 現在のライブラリではサイズ情報が取得できないため0を設定
                    PackedSize = 0, // 現在のライブラリでは圧縮サイズ情報が取得できないため0を設定
                    LastWriteTime = item.LastWriteTime
                };
                
                fileInfos.Add(fileInfo);
            }
            
            Logger.Log($"アーカイブ詳細情報取得完了: {fileInfos.Count}個のエントリ");
        }
        catch (Exception ex)
        {
            Logger.Log($"アーカイブ詳細情報取得でエラーが発生しました: {ex.Message}");
            throw;
        }
        
        return fileInfos;
    }
}

/// <summary>
/// アーカイブ内のファイル情報
/// </summary>
public class ArchiveFileInfo
{
    /// <summary>
    /// アイテムインデックス
    /// </summary>
    public uint Index { get; set; }

    /// <summary>
    /// ファイルパス
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// ファイル名
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// ディレクトリかどうか
    /// </summary>
    public bool IsDirectory { get; set; }

    /// <summary>
    /// ファイルサイズ
    /// </summary>
    public ulong Size { get; set; }

    /// <summary>
    /// 圧縮サイズ
    /// </summary>
    public ulong PackedSize { get; set; }

    /// <summary>
    /// 最終更新日時
    /// </summary>
    public DateTime LastWriteTime { get; set; }
}
