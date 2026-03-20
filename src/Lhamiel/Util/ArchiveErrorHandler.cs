using Cube.FileSystem.SevenZip;
using System.Text;
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
                errorInfo.Message = App.Text("ErrorHandler.FileNotFound");
                errorInfo.Details = App.Text("ErrorHandler.FileNotFoundDetail", archivePath);
                errorInfo.RecommendedAction = App.Text("ErrorHandler.FileNotFoundAction");
                errorInfo.IsRecoverable = false;
                break;

            case UnauthorizedAccessException:
                errorInfo.ErrorType = ArchiveErrorType.AccessDenied;
                errorInfo.Message = App.Text("ErrorHandler.AccessDenied");
                errorInfo.Details = App.Text("ErrorHandler.AccessDeniedDetail", archivePath);
                errorInfo.RecommendedAction = App.Text("ErrorHandler.AccessDeniedAction");
                errorInfo.IsRecoverable = true;
                break;

            case IOException ioEx:
                if (IsDiskSpaceError(ioEx))
                {
                    errorInfo.ErrorType = ArchiveErrorType.InsufficientDiskSpace;
                    errorInfo.Message = App.Text("ErrorHandler.DiskFull");
                    errorInfo.Details = App.Text("ErrorHandler.DiskFullDetail", outputPath);
                    errorInfo.RecommendedAction = App.Text("ErrorHandler.DiskFullAction");
                    errorInfo.IsRecoverable = true;
                }
                else if (IsFileInUseError(ioEx))
                {
                    errorInfo.ErrorType = ArchiveErrorType.FileInUse;
                    errorInfo.Message = App.Text("ErrorHandler.FileInUse");
                    errorInfo.Details = App.Text("ErrorHandler.FileInUseDetail", archivePath);
                    errorInfo.RecommendedAction = App.Text("ErrorHandler.FileInUseAction");
                    errorInfo.IsRecoverable = true;
                }
                else
                {
                    errorInfo.ErrorType = ArchiveErrorType.Unknown;
                    errorInfo.Message = App.Text("ErrorHandler.IOError");
                    errorInfo.Details = ioEx.Message;
                    errorInfo.RecommendedAction = App.Text("ErrorHandler.IOErrorAction");
                    errorInfo.IsRecoverable = true;
                }
                break;

            case InvalidOperationException:
                if (IsCorruptedFileError(ex))
                {
                    errorInfo.ErrorType = ArchiveErrorType.CorruptedFile;
                    errorInfo.Message = App.Text("ErrorHandler.Corrupted");
                    errorInfo.Details = GetCorruptionDetails(ex, archivePath);
                    errorInfo.RecommendedAction = App.Text("ErrorHandler.CorruptedAction");
                    errorInfo.IsRecoverable = false;
                }
                else
                {
                    errorInfo.ErrorType = ArchiveErrorType.Unknown;
                    errorInfo.Message = App.Text("ErrorHandler.InvalidOperation");
                    errorInfo.Details = ex.Message;
                    errorInfo.RecommendedAction = App.Text("ErrorHandler.InvalidOperationAction");
                    errorInfo.IsRecoverable = true;
                }
                break;

            case NotSupportedException:
                errorInfo.ErrorType = ArchiveErrorType.UnsupportedFormat;
                errorInfo.Message = App.Text("ErrorHandler.UnsupportedFormat");
                errorInfo.Details = App.Text("ErrorHandler.UnsupportedFormatDetail", Path.GetExtension(archivePath));
                errorInfo.RecommendedAction = App.Text("ErrorHandler.UnsupportedFormatAction");
                errorInfo.IsRecoverable = false;
                break;

            default:
                errorInfo.ErrorType = ArchiveErrorType.Unknown;
                errorInfo.Message = App.Text("ErrorHandler.Unexpected");
                errorInfo.Details = ex.Message;
                errorInfo.RecommendedAction = App.Text("ErrorHandler.UnexpectedAction");
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
            RecoverableFiles = [],
            CorruptedFiles = [],
            ErrorDetails = []
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
                // 展開されたファイル/ディレクトリを一括走査し、HashSetで O(1) 照合する
                var extractedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.EnumerateFiles(tempPath, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                    extractedFiles.Add(Path.GetRelativePath(tempPath, file));
                var extractedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var dir in Directory.EnumerateDirectories(tempPath, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                    extractedDirs.Add(Path.GetRelativePath(tempPath, dir));

                foreach (var item in reader.Items)
                {
                    try
                    {
                        var exists = item.IsDirectory
                            ? extractedDirs.Contains(item.FullName)
                            : extractedFiles.Contains(item.FullName);

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
                            else
                            {
                                analysis.ErrorDetails.Add($"ファイル '{item.FullName}' が一時展開先に見つかりませんでした。");
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
                FileOperations.CleanupTemporaryPath(tempPath, message => analysis.ErrorDetails.Add(message));
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

    // Windows HResult定数（OSロケール非依存で正確に判定するため）
    private const int HR_ERROR_DISK_FULL = unchecked((int)0x80070070);       // ERROR_DISK_FULL
    private const int HR_ERROR_HANDLE_DISK_FULL = unchecked((int)0x80070027); // ERROR_HANDLE_DISK_FULL
    private const int HR_ERROR_SHARING_VIOLATION = unchecked((int)0x80070020); // ERROR_SHARING_VIOLATION
    private const int HR_ERROR_LOCK_VIOLATION = unchecked((int)0x80070021);   // ERROR_LOCK_VIOLATION

    /// <summary>
    /// ディスク容量エラーかどうかを判定（HResultベースでOSロケール非依存）
    /// </summary>
    private static bool IsDiskSpaceError(IOException ex)
    {
        return ex.HResult is HR_ERROR_DISK_FULL or HR_ERROR_HANDLE_DISK_FULL;
    }

    /// <summary>
    /// ファイル使用中エラーかどうかを判定（HResultベースでOSロケール非依存）
    /// </summary>
    private static bool IsFileInUseError(IOException ex)
    {
        return ex.HResult is HR_ERROR_SHARING_VIOLATION or HR_ERROR_LOCK_VIOLATION;
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
        details.AppendLine(App.Text("ErrorHandler.ArchiveFile", archivePath));
        details.AppendLine(App.Text("ErrorHandler.ErrorMessage", ex.Message));

        try
        {
            var fileInfo = new FileInfo(archivePath);
            details.AppendLine(App.Text("ErrorHandler.FileSize", fileInfo.Length));
            details.AppendLine(App.Text("ErrorHandler.LastModified", fileInfo.LastWriteTime));
        }
        catch (Exception fileEx) when (fileEx is IOException or UnauthorizedAccessException)
        {
            Logger.Log($"ファイル情報の取得に失敗: {archivePath} - {fileEx.Message}", LogLevel.Warning);
            details.AppendLine(App.Text("ErrorHandler.FileInfoFailed"));
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
    public List<string> RecoverableFiles { get; set; } = [];

    /// <summary>
    /// 破損したファイル一覧
    /// </summary>
    public List<string> CorruptedFiles { get; set; } = [];

    /// <summary>
    /// エラー詳細一覧
    /// </summary>
    public List<string> ErrorDetails { get; set; } = [];

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
