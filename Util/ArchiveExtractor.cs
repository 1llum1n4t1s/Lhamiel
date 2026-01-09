using System.IO;
using System.IO.Compression;
using Cube.FileSystem.SevenZip;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;
using System.Threading;

namespace Lhamiel.Util;

/// <summary>
/// アーカイブ展開機能
/// </summary>
public class ArchiveExtractor
{
    /// <summary>
    /// サポートされている展開形式の一覧
    /// </summary>
    private static readonly string[] SupportedExtensions = { ".zip", ".7z", ".tar", ".gz", ".bz2", ".lzma", ".xz", ".rar", ".lzh", ".cab", ".arj", ".z", ".exe" };

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
            return IsSelfExtractingArchive(filePath);
        }
        
        return true;
    }

    /// <summary>
    /// ファイルが自己展開圧縮ファイルかどうかを判定する
    /// </summary>
    /// <param name="filePath">確認するファイルのパス</param>
    /// <returns>自己展開圧縮ファイルの場合はtrue、そうでなければfalse</returns>
    private static bool IsSelfExtractingArchive(string filePath)
    {
        try
        {
            Logger.Log($"IsSelfExtractingArchive開始: {filePath}");
            
            // ファイルサイズを確認（小さすぎるファイルは自己展開圧縮ファイルではない）
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length < 1024 * 1024) // 1MB未満は除外
            {
                Logger.Log($"ファイルサイズが小さすぎます: {fileInfo.Length} bytes");
                return false;
            }

            // ファイルの先頭バイトを読み取って、自己展開圧縮ファイルの特徴を確認
            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream);
            
            // ファイルの先頭から4バイトを読み取り
            var header = reader.ReadBytes(4);
            
            // MZヘッダー（DOS実行可能ファイル）の確認
            if (header[0] == 0x4D && header[1] == 0x5A) // "MZ"
            {
                Logger.Log("MZヘッダーを確認（DOS実行可能ファイル）");
                
                // ファイル全体をスキャンして圧縮データの特徴を探す
                // unzipsfxの場合は、ファイル内のどこかにZIPデータが埋め込まれている
                var isArchive = false;
                
                // 複数のスキャン戦略を試行
                var scanStrategies = new (long Start, long Size)[]
                {
                    // 戦略1: ファイル末尾から2MBをスキャン
                    (Math.Max(0, fileInfo.Length - 2 * 1024 * 1024), Math.Min(2 * 1024 * 1024, fileInfo.Length)),
                    // 戦略2: ファイル中央付近から1MBをスキャン
                    (Math.Max(0, fileInfo.Length / 2 - 512 * 1024), Math.Min(1024 * 1024, fileInfo.Length)),
                    // 戦略3: ファイル先頭から1MBをスキャン（実行可能ファイルのヘッダー部分を除く）
                    (4096, Math.Min(1024 * 1024, fileInfo.Length - 4096))
                };
                
                foreach (var strategy in scanStrategies)
                {
                    if (strategy.Size <= 0) continue;
                    
                    Logger.Log($"圧縮データスキャン戦略: 開始位置={strategy.Start}, スキャンサイズ={strategy.Size}");
                    
                    try
                    {
                        stream.Seek(strategy.Start, SeekOrigin.Begin);
                        var scanBytes = reader.ReadBytes((int)strategy.Size);
                        
                        // ZIPファイルの特徴（PK\x03\x04）を検索
                        for (int i = 0; i < scanBytes.Length - 3; i++)
                        {
                            if (scanBytes[i] == 0x50 && scanBytes[i + 1] == 0x4B && 
                                scanBytes[i + 2] == 0x03 && scanBytes[i + 3] == 0x04)
                            {
                                Logger.Log($"ZIPファイルの特徴を発見: オフセット={strategy.Start + i}");
                                isArchive = true;
                                break;
                            }
                        }
                        
                        if (isArchive) break;
                        
                        // unzipsfxの特徴的な文字列を検索（より多くのパターンを追加）
                        var searchStrings = new[] 
                        { 
                            "PKZIP", "unzipsfx", "Info-ZIP", "SFX", "ZIP", "PKWARE", 
                            "WinZip", "7-Zip", "RAR", "self-extracting", "self extracting",
                            "extract", "unzip", "decompress", "archive"
                        };
                        
                        var fileContent = System.Text.Encoding.ASCII.GetString(scanBytes);
                        
                        foreach (var searchString in searchStrings)
                        {
                            if (fileContent.Contains(searchString, StringComparison.OrdinalIgnoreCase))
                            {
                                Logger.Log($"unzipsfxの特徴を発見: {searchString}");
                                isArchive = true;
                                break;
                            }
                        }
                        
                        if (isArchive) break;
                        
                        // 7-Zipファイルの特徴（7z\xBC\xAF）を検索
                        for (int i = 0; i < scanBytes.Length - 3; i++)
                        {
                            if (scanBytes[i] == 0x37 && scanBytes[i + 1] == 0x7A && 
                                scanBytes[i + 2] == 0xBC && scanBytes[i + 3] == 0xAF)
                            {
                                Logger.Log("7-Zipファイルの特徴を発見");
                                isArchive = true;
                                break;
                            }
                        }
                        
                        if (isArchive) break;
                        
                        // RARファイルの特徴（Rar!\x1A\x07）を検索
                        for (int i = 0; i < scanBytes.Length - 5; i++)
                        {
                            if (scanBytes[i] == 0x52 && scanBytes[i + 1] == 0x61 && 
                                scanBytes[i + 2] == 0x72 && scanBytes[i + 3] == 0x21 && 
                                scanBytes[i + 4] == 0x1A && scanBytes[i + 5] == 0x07)
                            {
                                Logger.Log("RARファイルの特徴を発見");
                                isArchive = true;
                                break;
                            }
                        }
                        
                        if (isArchive) break;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"スキャン戦略でエラー: {ex.Message}");
                        continue;
                    }
                }
                
                if (isArchive)
                {
                    Logger.Log($"自己展開圧縮ファイルを確認: {filePath}");
                    return true;
                }
                else
                {
                    Logger.Log("圧縮ファイルの特徴が見つかりませんでした");
                    return false;
                }
            }
            else
            {
                Logger.Log("MZヘッダーが見つかりませんでした");
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"自己展開圧縮ファイルの判定中にエラーが発生: {ex.Message}");
            return false;
        }
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
        
        // アーカイブの内容をチェックして、二重フォルダを避ける
        var adjustedFileName = GetAdjustedFileName(archivePath, fileName);
        
        // 空文字列が返された場合は、アーカイブファイルと同じディレクトリに展開
        if (string.IsNullOrEmpty(adjustedFileName))
        {
            return directory;
        }
        
        return Path.Combine(directory, adjustedFileName);
    }

    /// <summary>
    /// アーカイブの内容をチェックして、適切なファイル名を取得する
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="defaultFileName">デフォルトのファイル名</param>
    /// <returns>調整されたファイル名</returns>
    private static string GetAdjustedFileName(string archivePath, string defaultFileName)
    {
        try
        {
            using var reader = new ArchiveReader(archivePath);
            
            // アーカイブの内容を取得
            var archiveContents = reader.Items.Select(item => item.FullName).ToList();

            if (!archiveContents.Any())
                return defaultFileName;

            // ルートレベルのディレクトリとファイルを取得
            var rootItems = archiveContents
                .Where(path => !path.Contains('/') && !path.Contains('\\'))
                .Select(path => new { Path = path, IsDirectory = path.EndsWith("/") || path.EndsWith("\\") })
                .ToList();

            // ルートに単一のディレクトリのみがある場合
            if (rootItems.Count == 1 && rootItems[0].IsDirectory)
            {
                var rootDirName = rootItems[0].Path.TrimEnd('/', '\\');
                
                // ルートディレクトリ名がアーカイブファイル名と同じ場合、二重フォルダを避ける
                if (string.Equals(rootDirName, defaultFileName, StringComparison.OrdinalIgnoreCase))
                {
                    // ルートディレクトリ内のアイテムをチェック
                    var rootDirItems = archiveContents
                        .Where(path => path.StartsWith(rootDirName + "/") || path.StartsWith(rootDirName + "\\"))
                        .ToList();

                    // ルートディレクトリ内に複数のアイテムがある場合、空文字列を返して二重フォルダを避ける
                    if (rootDirItems.Count > 1)
                    {
                        return "";
                    }
                    // ルートディレクトリ内に単一のアイテムがある場合、そのアイテム名を使用
                    else if (rootDirItems.Count == 1)
                    {
                        var innerItemPath = rootDirItems[0];
                        var innerItemName = Path.GetFileName(innerItemPath.TrimEnd('/', '\\'));
                        
                        // 内側のアイテムがディレクトリで、その名前がアーカイブファイル名と同じ場合
                        if ((innerItemPath.EndsWith("/") || innerItemPath.EndsWith("\\")) && 
                            string.Equals(innerItemName, defaultFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            // 二重フォルダを避けるため、空文字列を返す
                            return "";
                        }
                    }
                }
            }

            return defaultFileName;
        }
        catch (Exception ex)
        {
            Logger.Log($"アーカイブ内容のチェックでエラーが発生しました: {archivePath}, {ex.Message}");
            return defaultFileName;
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
    public static async Task ExtractArchiveAsync(string archivePath, string outputPath, IProgress<int>? progress = null, System.Windows.Window? parentWindow = null, CancellationToken cancellationToken = default)
    {
        Logger.Log($"ExtractArchiveAsync開始: archivePath={archivePath}, outputPath={outputPath}, parentWindow={parentWindow?.GetType().Name ?? "null"}");
        
        var extractor = new ArchiveExtractor();

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
        
        // アーカイブ内容を事前にチェックして、既存ファイルとの競合を確認
        if (parentWindow != null)
        {
            Logger.Log("アーカイブ内容の事前チェックを開始");
            try
            {
                using var reader = new ArchiveReader(archivePath);
                var conflictingFiles = CheckForConflictingFiles(reader, outputPath);
                Logger.Log($"競合ファイルチェック結果: 競合ファイル数={conflictingFiles.Count}");
                
                if (conflictingFiles.Any())
                {
                    Logger.Log($"競合ファイルを発見: {string.Join(", ", conflictingFiles.Take(5))}");
                    // UIスレッドで上書き確認を実行
                    var canOverwrite = await parentWindow.Dispatcher.InvokeAsync(() => 
                        FileOverwriteDialog.ShowMultipleFilesOverwriteDialog(
                            conflictingFiles.ToArray(), 
                            outputPath,
                            parentWindow));
                    
                    Logger.Log($"複数ファイル上書き確認ダイアログ結果: {canOverwrite}");
                    
                    if (canOverwrite == OverwriteResult.Cancel)
                    {
                        throw new OperationCanceledException("ユーザーが展開処理をキャンセルしました。");
                    }
                    else if (canOverwrite == OverwriteResult.No)
                    {
                        throw new OperationCanceledException("ユーザーが上書きを拒否しました。");
                    }
                }
                else
                {
                    Logger.Log("競合ファイルはありません");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"アーカイブ内容の事前チェックでエラーが発生しました: {ex.Message}");
                // エラーが発生した場合は続行
            }
        }
        else
        {
            Logger.Log("parentWindowがnullのため、アーカイブ内容の事前チェックをスキップ");
        }
        
        await Task.Run(() =>
        {
            var progressCallback = progress != null ? new Action<int>(p => progress.Report(p)) : null;
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
    public void ExtractArchive(string archivePath, string outputPath, Action<int>? progressCallback = null, System.Windows.Window? parentWindow = null, bool overwriteConfirmed = false, CancellationToken cancellationToken = default)
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
        try
        {
            // 出力ディレクトリを作成
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
                outputPrepared = true;
            }

            cancellationToken.ThrowIfCancellationRequested();

            using var reader = new ArchiveReader(archivePath);

            // 既存ファイルとの競合をチェック（事前チェックで既に処理済みのため、ここではスキップ）
            // 複数ファイルの上書き確認は ExtractArchiveAsync で事前に処理される
            
            // 進捗報告を設定
            if (progressCallback != null)
            {
                var progress = new Progress<Report>(report =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var percentage = report.TotalBytes > 0 ? (int)((report.Bytes * 100) / report.TotalBytes) : 0;
                    progressCallback(percentage);
                });

                reader.Save(outputPath, progress);
            }
            else
            {
                reader.Save(outputPath);
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
    /// ディレクトリ内のファイルの読み取り専用属性を削除する
    /// </summary>
    /// <param name="directoryPath">対象ディレクトリのパス</param>
    private static void RemoveReadOnlyAttributes(string directoryPath)
    {
        try
        {
            // ディレクトリ内のすべてのファイルとサブディレクトリを再帰的に処理
            foreach (var filePath in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
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
                var dirInfo = new DirectoryInfo(directoryPath);
                if ((dirInfo.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    dirInfo.Attributes &= ~FileAttributes.ReadOnly;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"ディレクトリ属性の変更に失敗しました: {directoryPath}, {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"読み取り専用属性の削除処理でエラーが発生しました: {directoryPath}, {ex.Message}");
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
            
            // 現在のライブラリでは特定ファイル展開は制限されているため、
            // 全体を展開する方法を使用
        reader.Save(outputPath);
            
        progressCallback?.Invoke(100);
            Logger.Log($"特定ファイル展開完了（全体展開）: {string.Join(", ", fileNames)}");
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
