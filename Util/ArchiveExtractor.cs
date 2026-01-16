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

        // 直接展開するため、アーカイブ名フォルダを作成
        return Path.Combine(baseDirectory, fileName);
    }


    /// <summary>
    /// 進捗レポートから現在処理中のファイル名を取得する
    /// </summary>
    private static string GetReportCurrentFileName(Report report)
    {
        try
        {
            // Report型にEntryプロパティが直接定義されていないため、dynamicを使用
            // dynamicは内部的にリフレクションを使用するが、Cube.FileSystem.SevenZipのAPI制約により必要
            dynamic dynReport = report;
            var entry = dynReport.Entry;

            if (entry != null)
            {
                // FullName を優先的に取得
                var fullName = entry.FullName as string;
                if (!string.IsNullOrWhiteSpace(fullName))
                {
                    return fullName;
                }

                // FullName が取得できなかった場合は Name を試す
                var name = entry.Name as string;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }

            return string.Empty;
        }
        catch
        {
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

        try
        {
            // 出力ディレクトリを作成
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            cancellationToken.ThrowIfCancellationRequested();

            using (var reader = new ArchiveReader(archivePath))
            {
                // アーカイブ内のエントリ情報を取得
                var entries = reader.Items.ToList();
                var totalEntries = entries.Count;
                Logger.Log($"展開するエントリ数: {totalEntries}");

                // 進捗報告を設定
                if (progressCallback != null)
                {
                    var progress = new Progress<Report>(report =>
                    {
                        // キャンセルされている場合は処理をスキップ（スレッドプールから実行されるため、例外をスローしない）
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        var percentage = report.TotalBytes > 0 ? (int)((report.Bytes * 100) / report.TotalBytes) : 0;

                        // より効率的なファイル名取得（リフレクションを使用）
                        var currentFileName = GetReportCurrentFileName(report);

                        var status = string.IsNullOrWhiteSpace(currentFileName)
                            ? "ファイルを展開中..."
                            : $"展開中: {Path.GetFileName(currentFileName)}";
                        Logger.Log($"展開進捗: {percentage}%, ファイル: {Path.GetFileName(currentFileName)}", LogLevel.Debug);
                        progressCallback(new ProgressInfo(percentage, status, currentFileName));
                    });

                    reader.Save(outputPath, progress);
                }
                else
                {
                    reader.Save(outputPath);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            Logger.Log($"アーカイブ展開完了: {archivePath} -> {outputPath}");
        }
        catch (OperationCanceledException)
        {
            // キャンセル時は何もクリーンアップしない（直接出力先に展開しているため）
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

            // ★ 最適化: 全展開を避け、対象ファイルのインデックスのみを取得
            var targetIndices = new List<uint>();
            var targetItems = new Dictionary<string, ArchiveEntity>(StringComparer.OrdinalIgnoreCase);
            var index = 0u;

            foreach (var item in reader.Items)
            {
                if (fileNameList.Contains(item.FullName))
                {
                    targetIndices.Add(index);
                    targetItems[item.FullName] = item;
                }
                index++;
            }

            if (targetIndices.Count == 0)
            {
                Logger.Log("指定されたファイルがアーカイブ内に見つかりません。", LogLevel.Warning);
                return;
            }

            // ★ 最適化: 個別展開APIを使用（全展開を回避）
            // 指定されたインデックスのファイルのみを直接出力先に展開
            var progress = new Progress<Report>(report =>
            {
                // 進捗レポートがある場合は処理
                if (progressCallback != null && report != null)
                {
                    var currentFileName = GetReportCurrentFileName(report);
                    if (!string.IsNullOrEmpty(currentFileName) && targetItems.ContainsKey(currentFileName))
                    {
                        // 対象ファイルの処理進捗を計算
                        var processedCount = targetItems.Count - fileNameList.Count + targetItems.Count(kv => kv.Value != null);
                        var progressValue = (int)((double)processedCount / fileNameList.Count * 100);
                        progressCallback(progressValue);
                    }
                }
            });

            reader.Save(outputPath, targetIndices.ToArray(), progress);

            var missingFiles = fileNameList.Where(f => !targetItems.ContainsKey(f)).ToList();
            if (missingFiles.Any())
            {
                Logger.Log($"指定されたファイルがアーカイブ内に見つかりません: {string.Join(", ", missingFiles)}", LogLevel.Warning);
            }

            Logger.Log($"特定ファイル展開完了: {string.Join(", ", fileNameList)}");
        }
        catch (Exception ex)
        {
            Logger.Log($"特定ファイル展開でエラーが発生しました: {ex.Message}");
            throw;
        }
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
