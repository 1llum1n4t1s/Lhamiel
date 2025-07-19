using System;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Windows;

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
    /// <returns>処理が成功した場合はtrue、そうでなければfalse</returns>
    public static async Task<bool> ExtractArchiveAsync(string filePath, string outputDir, bool outputToSameDirectory, View.ProgressWindow progressWindow)
    {
        Logger.Log($"ArchiveProcessor.ExtractArchiveAsync開始: filePath={filePath}, outputDir={outputDir}, outputToSameDirectory={outputToSameDirectory}, progressWindow={progressWindow?.GetType().Name ?? "null"}");
        
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
                Logger.Log($"自己展開圧縮ファイルの可能性を確認: {filePath}");
                if (!IsSelfExtractingArchive(filePath))
                {
                    Logger.Log($"実行可能ファイルですが、自己展開圧縮ファイルではありません: {filePath}");
                    MessageBox.Show($"実行可能ファイルですが、自己展開圧縮ファイルではありません。\n{filePath}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                Logger.Log($"自己展開圧縮ファイルを確認: {filePath}");
            }

            Logger.Log($"展開処理を開始: {filePath}");

            // 出力先ディレクトリの取得
            var outputPath = ArchiveExtractor.GetOutputDirectory(filePath, outputDir, outputToSameDirectory);

            // ファイル名を設定
            progressWindow?.SetFileName(filePath);

            // 展開処理を実行
            var progress = new Progress<int>(percentage =>
            {
                progressWindow?.UpdateProgress(percentage, "ファイルを展開中...");
            });

            Logger.Log($"ArchiveExtractor.ExtractArchiveAsyncを呼び出し: filePath={filePath}, outputPath={outputPath}, progressWindow={progressWindow?.GetType().Name ?? "null"}");
            await ArchiveExtractor.ExtractArchiveAsync(filePath, outputPath, progress, progressWindow);

            Logger.Log($"展開処理が完了: {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogException($"展開処理でエラーが発生: {filePath}", ex);
            MessageBox.Show($"展開中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>
    /// 複数のアーカイブファイルの展開処理を実行
    /// </summary>
    /// <param name="filePaths">展開するファイルのパスの配列</param>
    /// <param name="outputDir">出力ディレクトリ</param>
    /// <param name="outputToSameDirectory">同じディレクトリに出力するかどうか</param>
    /// <param name="progressWindow">進行状況ウィンドウ</param>
    /// <returns>すべての処理が成功した場合はtrue、そうでなければfalse</returns>
    public static async Task<bool> ExtractArchivesAsync(string[] filePaths, string outputDir, bool outputToSameDirectory, View.ProgressWindow progressWindow)
    {
        try
        {
            var successCount = 0;
            var totalCount = filePaths.Length;

            foreach (var filePath in filePaths)
            {
                var success = await ExtractArchiveAsync(filePath, outputDir, outputToSameDirectory, progressWindow);
                if (success)
                {
                    successCount++;
                }
            }

            // 完了メッセージを表示
            if (successCount == totalCount)
            {
                progressWindow?.SetCompleted("展開が完了しました。");
                await Task.Delay(1000);
                progressWindow?.Close();
                return true;
            }
            else
            {
                progressWindow?.SetCompleted($"{successCount}/{totalCount}個のファイルの展開が完了しました。");
                await Task.Delay(1000);
                progressWindow?.Close();
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("複数ファイル展開処理でエラーが発生", ex);
            MessageBox.Show($"展開中にエラーが発生しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
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
} 