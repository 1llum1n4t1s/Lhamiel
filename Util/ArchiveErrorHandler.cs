using System.IO;
using System.Text;
using Cube.FileSystem.SevenZip;

namespace Lhamiel.Util;

/// <summary>
/// アーカイブエラーの詳細情報を提供するクラス
/// </summary>
public class ArchiveErrorInfo
{
    /// <summary>
    /// エラーの種類
    /// </summary>
    public ArchiveErrorType ErrorType { get; set; }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// 詳細なエラー情報
    /// </summary>
    public string Details { get; set; } = "";

    /// <summary>
    /// 問題のあるファイルパス
    /// </summary>
    public string? ProblematicFilePath { get; set; }

    /// <summary>
    /// 推奨される対処法
    /// </summary>
    public string RecommendedAction { get; set; } = "";

    /// <summary>
    /// エラーが回復可能かどうか
    /// </summary>
    public bool IsRecoverable { get; set; }

    /// <summary>
    /// 元の例外
    /// </summary>
    public Exception? OriginalException { get; set; }
}

/// <summary>
/// アーカイブエラーの種類
/// </summary>
public enum ArchiveErrorType
{
    /// <summary>
    /// 不明なエラー
    /// </summary>
    Unknown,

    /// <summary>
    /// ファイルが破損している
    /// </summary>
    CorruptedFile,

    /// <summary>
    /// ファイルが存在しない
    /// </summary>
    FileNotFound,

    /// <summary>
    /// アクセス権限がない
    /// </summary>
    AccessDenied,

    /// <summary>
    /// ディスク容量不足
    /// </summary>
    InsufficientDiskSpace,

    /// <summary>
    /// サポートされていない形式
    /// </summary>
    UnsupportedFormat,

    /// <summary>
    /// ファイルが使用中
    /// </summary>
    FileInUse
}

/// <summary>
/// アーカイブエラーハンドラー
/// </summary>
public static class ArchiveErrorHandler
{
    /// <summary>
    /// 例外から詳細なエラー情報を取得
    /// </summary>
    /// <param name="ex">発生した例外</param>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <param name="outputPath">出力先パス</param>
    /// <returns>詳細なエラー情報</returns>
    public static ArchiveErrorInfo AnalyzeError(Exception ex, string archivePath, string outputPath)
    {
        var errorInfo = new ArchiveErrorInfo
        {
            OriginalException = ex,
            ProblematicFilePath = archivePath
        };

        // 例外の種類に基づいてエラー情報を分析
        switch (ex)
        {
            case FileNotFoundException:
                errorInfo.ErrorType = ArchiveErrorType.FileNotFound;
                errorInfo.Message = "アーカイブファイルが見つかりません";
                errorInfo.Details = $"指定されたファイルが存在しません: {archivePath}";
                errorInfo.RecommendedAction = "ファイルパスを確認し、ファイルが存在することを確認してください";
                errorInfo.IsRecoverable = false;
                break;

            case UnauthorizedAccessException:
                errorInfo.ErrorType = ArchiveErrorType.AccessDenied;
                errorInfo.Message = "アクセス権限がありません";
                errorInfo.Details = $"ファイルまたはディレクトリへのアクセスが拒否されました: {archivePath}";
                errorInfo.RecommendedAction = "管理者権限で実行するか、ファイルのアクセス権限を確認してください";
                errorInfo.IsRecoverable = true;
                break;

            case IOException ioEx:
                if (IsDiskSpaceError(ioEx))
                {
                    errorInfo.ErrorType = ArchiveErrorType.InsufficientDiskSpace;
                    errorInfo.Message = "ディスク容量が不足しています";
                    errorInfo.Details = $"展開先ディスクの容量が不足しています: {outputPath}";
                    errorInfo.RecommendedAction = "ディスク容量を確保するか、別のディスクに展開してください";
                    errorInfo.IsRecoverable = true;
                }
                else if (IsFileInUseError(ioEx))
                {
                    errorInfo.ErrorType = ArchiveErrorType.FileInUse;
                    errorInfo.Message = "ファイルが使用中です";
                    errorInfo.Details = $"ファイルが他のアプリケーションで使用されています: {archivePath}";
                    errorInfo.RecommendedAction = "ファイルを使用しているアプリケーションを閉じてから再試行してください";
                    errorInfo.IsRecoverable = true;
                }
                else
                {
                    errorInfo.ErrorType = ArchiveErrorType.Unknown;
                    errorInfo.Message = "I/Oエラーが発生しました";
                    errorInfo.Details = ioEx.Message;
                    errorInfo.RecommendedAction = "ファイルの状態を確認し、再試行してください";
                    errorInfo.IsRecoverable = true;
                }
                break;

            case InvalidOperationException:
                if (IsCorruptedFileError(ex))
                {
                    errorInfo.ErrorType = ArchiveErrorType.CorruptedFile;
                    errorInfo.Message = "アーカイブファイルが破損しています";
                    errorInfo.Details = GetCorruptionDetails(ex, archivePath);
                    errorInfo.RecommendedAction = "ファイルの再ダウンロードまたは修復を試してください";
                    errorInfo.IsRecoverable = false;
                }
                else
                {
                    errorInfo.ErrorType = ArchiveErrorType.Unknown;
                    errorInfo.Message = "無効な操作が実行されました";
                    errorInfo.Details = ex.Message;
                    errorInfo.RecommendedAction = "操作を確認し、再試行してください";
                    errorInfo.IsRecoverable = true;
                }
                break;

            case NotSupportedException:
                errorInfo.ErrorType = ArchiveErrorType.UnsupportedFormat;
                errorInfo.Message = "サポートされていないファイル形式です";
                errorInfo.Details = $"このファイル形式はサポートされていません: {Path.GetExtension(archivePath)}";
                errorInfo.RecommendedAction = "サポートされている形式（ZIP、7Z、RAR等）のファイルを使用してください";
                errorInfo.IsRecoverable = false;
                break;

            default:
                errorInfo.ErrorType = ArchiveErrorType.Unknown;
                errorInfo.Message = "予期しないエラーが発生しました";
                errorInfo.Details = ex.Message;
                errorInfo.RecommendedAction = "エラーの詳細を確認し、必要に応じてサポートに連絡してください";
                errorInfo.IsRecoverable = true;
                break;
        }

        return errorInfo;
    }

    /// <summary>
    /// アーカイブファイルの破損状況を詳細に分析
    /// </summary>
    /// <param name="archivePath">アーカイブファイルのパス</param>
    /// <returns>破損分析結果</returns>
    public static ArchiveCorruptionAnalysis AnalyzeCorruption(string archivePath)
    {
        var analysis = new ArchiveCorruptionAnalysis
        {
            ArchivePath = archivePath,
            IsCorrupted = false,
            CorruptionType = CorruptionType.None,
            RecoverableFiles = new List<string>(),
            CorruptedFiles = new List<string>(),
            ErrorDetails = new List<string>()
        };

        try
        {
            using var reader = new ArchiveReader(archivePath);
            
            // アーカイブの基本情報を取得
            analysis.TotalFiles = reader.Items.Count();
            analysis.TotalSize = 0; // 現在のライブラリではサイズ情報が取得できないため0を設定
            var tempPath = Path.Combine(Path.GetTempPath(), $"lhamiel-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempPath);
            Exception? extractionException = null;

            try
            {
                reader.Save(tempPath);
            }
            catch (Exception ex)
            {
                extractionException = ex;
                analysis.ErrorDetails.Add($"一時展開でエラー: {ex.Message}");
                analysis.IsCorrupted = true;
            }

            try
            {
                // 各ファイルの整合性をチェック
                foreach (var item in reader.Items)
                {
                    try
                    {
                        var itemPath = Path.Combine(tempPath, item.FullName);
                        var exists = item.IsDirectory ? Directory.Exists(itemPath) : File.Exists(itemPath);

                        if (exists)
                        {
                            analysis.RecoverableFiles.Add(item.FullName);
                        }
                        else
                        {
                            analysis.CorruptedFiles.Add(item.FullName);
                            analysis.IsCorrupted = true;
                            if (extractionException != null)
                            {
                                analysis.ErrorDetails.Add($"ファイル '{item.FullName}' の展開に失敗: {extractionException.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        analysis.CorruptedFiles.Add(item.FullName);
                        analysis.ErrorDetails.Add($"ファイル '{item.FullName}' でエラー: {ex.Message}");
                        analysis.IsCorrupted = true;
                    }
                }
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
                    analysis.ErrorDetails.Add($"一時ディレクトリ削除に失敗: {ex.Message}");
                }
            }

            // 破損の種類を判定
            if (analysis.IsCorrupted)
            {
                if (analysis.CorruptedFiles.Count == analysis.TotalFiles)
                {
                    analysis.CorruptionType = CorruptionType.Complete;
                }
                else if (analysis.CorruptedFiles.Count > analysis.TotalFiles / 2)
                {
                    analysis.CorruptionType = CorruptionType.Severe;
                }
                else
                {
                    analysis.CorruptionType = CorruptionType.Partial;
                }
            }
        }
        catch (Exception ex)
        {
            analysis.IsCorrupted = true;
            analysis.CorruptionType = CorruptionType.Complete;
            analysis.ErrorDetails.Add($"アーカイブ全体の読み込みエラー: {ex.Message}");
        }

        return analysis;
    }

    /// <summary>
    /// ディスク容量エラーかどうかを判定
    /// </summary>
    private static bool IsDiskSpaceError(IOException ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("disk") && message.Contains("space") ||
               message.Contains("not enough space") ||
               message.Contains("insufficient space");
    }

    /// <summary>
    /// ファイル使用中エラーかどうかを判定
    /// </summary>
    private static bool IsFileInUseError(IOException ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("being used by another process") ||
               message.Contains("file in use") ||
               message.Contains("access denied");
    }

    /// <summary>
    /// 破損ファイルエラーかどうかを判定
    /// </summary>
    private static bool IsCorruptedFileError(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("corrupt") ||
               message.Contains("damaged") ||
               message.Contains("invalid") ||
               message.Contains("crc") ||
               message.Contains("checksum");
    }

    /// <summary>
    /// 破損の詳細情報を取得
    /// </summary>
    private static string GetCorruptionDetails(Exception ex, string archivePath)
    {
        var details = new StringBuilder();
        details.AppendLine($"アーカイブファイル: {archivePath}");
        details.AppendLine($"エラーメッセージ: {ex.Message}");
        
        try
        {
            var fileInfo = new FileInfo(archivePath);
            details.AppendLine($"ファイルサイズ: {fileInfo.Length:N0} バイト");
            details.AppendLine($"最終更新日時: {fileInfo.LastWriteTime}");
        }
        catch
        {
            details.AppendLine("ファイル情報の取得に失敗しました");
        }

        return details.ToString();
    }
}

/// <summary>
/// アーカイブ破損分析結果
/// </summary>
public class ArchiveCorruptionAnalysis
{
    /// <summary>
    /// アーカイブファイルのパス
    /// </summary>
    public string ArchivePath { get; set; } = "";

    /// <summary>
    /// 破損しているかどうか
    /// </summary>
    public bool IsCorrupted { get; set; }

    /// <summary>
    /// 破損の種類
    /// </summary>
    public CorruptionType CorruptionType { get; set; }

    /// <summary>
    /// 総ファイル数
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// 総サイズ
    /// </summary>
    public ulong TotalSize { get; set; }

    /// <summary>
    /// 回復可能なファイル一覧
    /// </summary>
    public List<string> RecoverableFiles { get; set; } = new();

    /// <summary>
    /// 破損したファイル一覧
    /// </summary>
    public List<string> CorruptedFiles { get; set; } = new();

    /// <summary>
    /// エラー詳細一覧
    /// </summary>
    public List<string> ErrorDetails { get; set; } = new();

    /// <summary>
    /// 回復可能なファイルの割合
    /// </summary>
    public double RecoveryRate => TotalFiles > 0 ? (double)RecoverableFiles.Count / TotalFiles * 100 : 0;
}

/// <summary>
/// 破損の種類
/// </summary>
public enum CorruptionType
{
    /// <summary>
    /// 破損なし
    /// </summary>
    None,

    /// <summary>
    /// 部分的な破損
    /// </summary>
    Partial,

    /// <summary>
    /// 深刻な破損
    /// </summary>
    Severe,

    /// <summary>
    /// 完全な破損
    /// </summary>
    Complete
}
