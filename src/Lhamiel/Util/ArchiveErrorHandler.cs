using Cube.FileSystem.SevenZip;
using System.Text;
using LzhArchiveErrorKind = Kagayoi.UnLhaRe.ArchiveErrorKind;
using LzhArchiveNativeException = Kagayoi.UnLhaRe.ArchiveNativeException;
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
    /// 安全のために設定されたアーカイブ処理上限を超えた
    /// </summary>
    ResourceLimitExceeded,

    /// <summary>
    /// ファイルが使用中
    /// </summary>
    FileInUse,

    /// <summary>
    /// アーカイブがパスワード保護されている／パスワード不一致
    /// </summary>
    EncryptedOrWrongPassword,

    /// <summary>
    /// 外部デバイス (USB SSD / NAS / リムーバブルメディア) が切断された。
    /// USB スリープ / 物理的取り外し / ネットワーク断 が主因で、アプリ側のリトライでは復旧しないため
    /// ユーザーに再接続を促すべきエラー。RTK レビュー #F-003 対応。
    /// </summary>
    DeviceDisconnected
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

            // EncryptionException は IOException を継承しているため、必ず IOException ケースより前で処理する。
            // Cube.FileSystem.SevenZip は「パスワード無し/誤入力」時にこの例外を投げるが、既定の
            // IOException message "I/O error occurred." が表示され本当の原因が隠れてしまうため、専用の案内に置き換える。
            case EncryptionException:
                ApplyEncrypted(errorInfo, archivePath);
                break;

            // 7z.dll 由来の HRESULT ベース例外。SevenZipException と EncryptionException は
            // どちらも IOException の直接の派生（互いに兄弟であり、EncryptionException は
            // SevenZipException の派生ではない）なので、両者とも IOException ケースより前に置く。
            // 旧実装は SevenZipException を一律 CorruptedFile としていたが、SevenZipCode には
            // 非対応形式 (UnsupportedMethod) やパスワード誤り (WrongPassword) も含まれるため、
            // Code と内部例外 (ディスク満杯 / デバイス切断 / 使用中) で細分類して誤表示を防ぐ。
            case SevenZipException sevenZipEx:
                AnalyzeSevenZipException(sevenZipEx, errorInfo, archivePath, outputPath);
                break;

            // UnLhaRe API level 4+ が返す安定した分類を使う。メッセージ文字列の解析や、
            // I/O エラーを根拠なくディスク不足へ決め打ちしない。
            case LzhArchiveNativeException lzhEx:
                AnalyzeLzhArchiveException(lzhEx, errorInfo, archivePath);
                break;

            case IOException ioEx:
                if (IsDiskSpaceError(ioEx)) ApplyDiskSpace(errorInfo, outputPath);
                else if (IsDeviceDisconnectedError(ioEx)) ApplyDeviceDisconnected(errorInfo, outputPath);
                else if (IsFileInUseError(ioEx)) ApplyFileInUse(errorInfo, archivePath);
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
                    ApplyCorrupted(errorInfo, ex, archivePath);
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
                ApplyUnsupported(errorInfo, archivePath);
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

    /// <summary>UnLhaRe の安定したエラー種別を利用者向けカテゴリへ写像する。</summary>
    private static void AnalyzeLzhArchiveException(
        LzhArchiveNativeException ex,
        ArchiveErrorInfo errorInfo,
        string archivePath)
    {
        switch (ex.Kind)
        {
            case LzhArchiveErrorKind.Format:
            case LzhArchiveErrorKind.InvalidPath:
            case LzhArchiveErrorKind.Exists:
                // 展開は空の一時ディレクトリへ行うため、Exists は重複名など書庫側の矛盾を示す。
                ApplyCorrupted(errorInfo, ex, archivePath);
                break;

            case LzhArchiveErrorKind.Unsupported:
                ApplyUnsupported(errorInfo, archivePath);
                break;

            case LzhArchiveErrorKind.Limit:
                errorInfo.ErrorType = ArchiveErrorType.ResourceLimitExceeded;
                errorInfo.Message = App.Text("ErrorHandler.ResourceLimit");
                errorInfo.Details = ex.Message;
                errorInfo.RecommendedAction = App.Text("ErrorHandler.ResourceLimitAction");
                errorInfo.IsRecoverable = false;
                break;

            case LzhArchiveErrorKind.Io:
                errorInfo.ErrorType = ArchiveErrorType.Unknown;
                errorInfo.Message = App.Text("ErrorHandler.IOError");
                errorInfo.Details = ex.Message;
                errorInfo.RecommendedAction = App.Text("ErrorHandler.IOErrorAction");
                errorInfo.IsRecoverable = true;
                break;

            case LzhArchiveErrorKind.InvalidArgument:
                errorInfo.ErrorType = ArchiveErrorType.Unknown;
                errorInfo.Message = App.Text("ErrorHandler.InvalidOperation");
                errorInfo.Details = ex.Message;
                errorInfo.RecommendedAction = App.Text("ErrorHandler.InvalidOperationAction");
                errorInfo.IsRecoverable = false;
                break;

            default:
                // Cancelled は通常 .NET wrapper が OperationCanceledException へ変換する。
                // Unknown / BufferTooSmall / Internal は利用者入力から原因を断定できない。
                errorInfo.ErrorType = ArchiveErrorType.Unknown;
                errorInfo.Message = App.Text("ErrorHandler.Unexpected");
                errorInfo.Details = ex.Message;
                errorInfo.RecommendedAction = App.Text("ErrorHandler.UnexpectedAction");
                errorInfo.IsRecoverable = false;
                break;
        }
    }

    /// <summary>
    /// UnLhaRe の作成エラーを圧縮操作の文脈で分類する。
    /// 展開用の分類と異なり、出力先の InvalidPath / Exists を書庫破損とは扱わない。
    /// </summary>
    internal static ArchiveErrorInfo AnalyzeLzhCreateError(
        LzhArchiveNativeException ex,
        string outputPath)
    {
        var errorInfo = new ArchiveErrorInfo
        {
            OriginalException = ex,
            ProblematicFilePath = outputPath,
            Details = ex.Message
        };

        switch (ex.Kind)
        {
            case LzhArchiveErrorKind.Limit:
                errorInfo.ErrorType = ArchiveErrorType.ResourceLimitExceeded;
                errorInfo.Message = App.Text("ErrorHandler.ResourceLimit");
                errorInfo.RecommendedAction = App.Text("ErrorHandler.ResourceLimitAction");
                errorInfo.IsRecoverable = false;
                break;

            case LzhArchiveErrorKind.Io:
                errorInfo.ErrorType = ArchiveErrorType.Unknown;
                errorInfo.Message = App.Text("ErrorHandler.IOError");
                errorInfo.RecommendedAction = App.Text("ErrorHandler.IOErrorAction");
                errorInfo.IsRecoverable = true;
                break;

            case LzhArchiveErrorKind.Unsupported:
                errorInfo.ErrorType = ArchiveErrorType.UnsupportedFormat;
                errorInfo.Message = App.Text("ErrorHandler.UnsupportedFormat");
                errorInfo.RecommendedAction = App.Text("ErrorHandler.UnsupportedFormatAction");
                errorInfo.IsRecoverable = false;
                break;

            case LzhArchiveErrorKind.Format:
            case LzhArchiveErrorKind.InvalidArgument:
            case LzhArchiveErrorKind.InvalidPath:
            case LzhArchiveErrorKind.Exists:
                errorInfo.ErrorType = ArchiveErrorType.Unknown;
                errorInfo.Message = App.Text("ErrorHandler.InvalidOperation");
                errorInfo.RecommendedAction = App.Text("ErrorHandler.InvalidOperationAction");
                errorInfo.IsRecoverable = false;
                break;

            default:
                // Cancelled は通常 .NET wrapper が OperationCanceledException へ変換する。
                errorInfo.ErrorType = ArchiveErrorType.Unknown;
                errorInfo.Message = App.Text("ErrorHandler.Unexpected");
                errorInfo.RecommendedAction = App.Text("ErrorHandler.UnexpectedAction");
                errorInfo.IsRecoverable = false;
                break;
        }

        return errorInfo;
    }

    /// <summary>
    /// SevenZipException (7z.dll 由来) を <see cref="SevenZipCode"/> と内部例外から細分類する。
    /// 旧実装はすべて「アーカイブ破損」と扱っていたため、非対応形式・パスワード誤り・
    /// ディスク満杯 / デバイス切断 / 使用中といった本来別カテゴリのエラーが破損として
    /// 誤表示されていた。内部に I/O 起因の例外がラップされている場合はそちらを優先する。
    /// </summary>
    private static void AnalyzeSevenZipException(SevenZipException ex, ArchiveErrorInfo errorInfo, string archivePath, string outputPath)
    {
        // ① SevenZipException(code, inner) で本当の原因 (ディスク満杯 / デバイス切断 / 使用中) が
        //    ラップされているケースを救済する。自前の例外型 (SevenZip/Encryption) は除外する。
        if (ex.InnerException is IOException inner and not (SevenZipException or EncryptionException))
        {
            if (IsDiskSpaceError(inner)) { ApplyDiskSpace(errorInfo, outputPath); return; }
            if (IsDeviceDisconnectedError(inner)) { ApplyDeviceDisconnected(errorInfo, outputPath); return; }
            if (IsFileInUseError(inner)) { ApplyFileInUse(errorInfo, archivePath); return; }
        }

        // ② SevenZipCode で分類。WrongPassword / UnsupportedMethod を破損から切り分ける。
        switch (ex.Code)
        {
            case SevenZipCode.WrongPassword:
                ApplyEncrypted(errorInfo, archivePath);
                break;

            case SevenZipCode.UnsupportedMethod:
                ApplyUnsupported(errorInfo, archivePath);
                break;

            default:
                // DataError / CrcError / HeadersError / IsNotArc / UnexpectedEnd / DataAfterEnd /
                // Unavailable / UnknownError → アーカイブ破損として扱う。
                ApplyCorrupted(errorInfo, ex, archivePath);
                break;
        }
    }

    /// <summary>アーカイブ破損としてエラー情報を設定する。</summary>
    private static void ApplyCorrupted(ArchiveErrorInfo errorInfo, Exception ex, string archivePath)
    {
        errorInfo.ErrorType = ArchiveErrorType.CorruptedFile;
        errorInfo.Message = App.Text("ErrorHandler.Corrupted");
        errorInfo.Details = GetCorruptionDetails(ex, archivePath);
        errorInfo.RecommendedAction = App.Text("ErrorHandler.CorruptedAction");
        errorInfo.IsRecoverable = false;
    }

    /// <summary>非対応形式としてエラー情報を設定する。</summary>
    private static void ApplyUnsupported(ArchiveErrorInfo errorInfo, string archivePath)
    {
        errorInfo.ErrorType = ArchiveErrorType.UnsupportedFormat;
        errorInfo.Message = App.Text("ErrorHandler.UnsupportedFormat");
        errorInfo.Details = App.Text("ErrorHandler.UnsupportedFormatDetail", Path.GetExtension(archivePath));
        errorInfo.RecommendedAction = App.Text("ErrorHandler.UnsupportedFormatAction");
        errorInfo.IsRecoverable = false;
    }

    /// <summary>暗号化・パスワード誤りとしてエラー情報を設定する。</summary>
    private static void ApplyEncrypted(ArchiveErrorInfo errorInfo, string archivePath)
    {
        errorInfo.ErrorType = ArchiveErrorType.EncryptedOrWrongPassword;
        errorInfo.Message = App.Text("ErrorHandler.EncryptedOrWrongPassword");
        errorInfo.Details = App.Text("ErrorHandler.EncryptedOrWrongPasswordDetail", archivePath);
        errorInfo.RecommendedAction = App.Text("ErrorHandler.EncryptedOrWrongPasswordAction");
        errorInfo.IsRecoverable = true;
    }

    /// <summary>ディスク容量不足としてエラー情報を設定する。</summary>
    private static void ApplyDiskSpace(ArchiveErrorInfo errorInfo, string outputPath)
    {
        errorInfo.ErrorType = ArchiveErrorType.InsufficientDiskSpace;
        errorInfo.Message = App.Text("ErrorHandler.DiskFull");
        errorInfo.Details = App.Text("ErrorHandler.DiskFullDetail", outputPath);
        errorInfo.RecommendedAction = App.Text("ErrorHandler.DiskFullAction");
        errorInfo.IsRecoverable = true;
    }

    /// <summary>
    /// 外部デバイス切断としてエラー情報を設定する。USB SSD のスリープ / NAS タイムアウト /
    /// リムーバブルメディア取り外しが主因で、リトライ不能・再接続が必要。RTK レビュー #F-003 対応。
    /// </summary>
    private static void ApplyDeviceDisconnected(ArchiveErrorInfo errorInfo, string outputPath)
    {
        errorInfo.ErrorType = ArchiveErrorType.DeviceDisconnected;
        errorInfo.Message = App.Text("ErrorHandler.DeviceDisconnected");
        errorInfo.Details = App.Text("ErrorHandler.DeviceDisconnectedDetail", outputPath);
        errorInfo.RecommendedAction = App.Text("ErrorHandler.DeviceDisconnectedAction");
        errorInfo.IsRecoverable = true;
    }

    /// <summary>ファイル使用中としてエラー情報を設定する。</summary>
    private static void ApplyFileInUse(ArchiveErrorInfo errorInfo, string archivePath)
    {
        errorInfo.ErrorType = ArchiveErrorType.FileInUse;
        errorInfo.Message = App.Text("ErrorHandler.FileInUse");
        errorInfo.Details = App.Text("ErrorHandler.FileInUseDetail", archivePath);
        errorInfo.RecommendedAction = App.Text("ErrorHandler.FileInUseAction");
        errorInfo.IsRecoverable = true;
    }

    // Windows HResult 定数は Util/WindowsHResults.cs に集約 (RTK レビュー #B2-002 対応)。

    /// <summary>
    /// ディスク容量エラーかどうかを判定（HResultベースでOSロケール非依存）
    /// </summary>
    private static bool IsDiskSpaceError(IOException ex)
    {
        return ex.HResult is WindowsHResults.ErrorDiskFull or WindowsHResults.ErrorHandleDiskFull;
    }

    /// <summary>
    /// ファイル使用中エラーかどうかを判定（HResultベースでOSロケール非依存）
    /// </summary>
    private static bool IsFileInUseError(IOException ex)
    {
        return ex.HResult is WindowsHResults.ErrorSharingViolation or WindowsHResults.ErrorLockViolation;
    }

    /// <summary>
    /// 外部デバイス切断系エラーかどうかを判定（USB SSD スリープ、NAS タイムアウト等）。
    /// RTK レビュー #F-003 対応 — リトライ不能・ユーザー再接続が必要な永続エラーとして分類。
    /// <para>
    /// CodeRabbit 指摘対応 (#3305116049): 他の分類関数 (IsCorruptedFileError) と同様に、
    /// HResult 一致しない経路（FileSystem ドライバ差分、ローカライズ済み例外メッセージ）の
    /// フォールバックとしてメッセージ走査を追加。
    /// </para>
    /// </summary>
    private static bool IsDeviceDisconnectedError(IOException ex)
    {
        // ① HResult ベースで判定 (OS ロケール非依存)
        if (WindowsHResults.IsDeviceDisconnected(ex.HResult))
            return true;

        // ② メッセージキーワード走査 (OS ロケール依存のフォールバック)
        var message = ex.Message?.ToLowerInvariant() ?? string.Empty;
        return message.Contains("device is not ready")
            || message.Contains("not responding")
            || message.Contains("device does not exist")
            || message.Contains("the network name")        // "The network name cannot be found" 等
            || message.Contains("network name is no longer available")
            || message.Contains("デバイスの準備ができていません")
            || message.Contains("デバイスが応答")
            || message.Contains("接続が切断");
    }

    /// <summary>
    /// HResult が破損系エラーかどうかを判定（OSロケール非依存）
    /// </summary>
    internal static bool IsCorruptedHResult(int hResult)
    {
        return WindowsHResults.IsCorrupted(hResult);
    }

    /// <summary>
    /// 破損ファイルエラーかどうかを二段判定: ① HResult → ② メッセージキーワード
    /// </summary>
    private static bool IsCorruptedFileError(Exception ex)
    {
        // ① HResult ベース判定（OSロケール非依存で正確）
        if (IsCorruptedHResult(ex.HResult))
            return true;

        // ② フォールバック: メッセージキーワード走査
        var message = ex.Message.ToLowerInvariant();

        if (message.Contains("corrupt") ||
            message.Contains("damaged") ||
            message.Contains("invalid") ||
            message.Contains("crc") ||
            message.Contains("checksum"))
            return true;

        if (message.Contains("破損") ||
            message.Contains("壊れ") ||
            message.Contains("無効") ||
            message.Contains("チェックサム"))
            return true;

        return false;
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
