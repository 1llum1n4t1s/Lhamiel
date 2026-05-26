namespace Lhamiel.Util;

/// <summary>
/// Windows のシステムエラーコード (Win32 ERROR_*) を HResult 形式 (0x8007XXXX) に変換した定数集合。
/// <para>
/// OS ロケール非依存にエラーを分類するため、各ユーティリティ (ArchiveErrorHandler / LockedFileRetryPolicy /
/// MotwPropagator / DiskSpaceChecker 等) はこのクラスの定数を共有して使う。
/// 以前は <c>ArchiveErrorHandler</c> と <c>LockedFileRetryPolicy</c> に重複定義されていたが、
/// 新規エラー追加時の片側更新漏れを防ぐため一元化した (RTK レビュー #B2-002 対応)。
/// </para>
/// <para>
/// 値は <see href="https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499-">
/// Win32 System Error Codes</see> を <c>HRESULT_FROM_WIN32</c> 相当 (上位 16bit = 0x8007) で変換したもの。
/// </para>
/// </summary>
internal static class WindowsHResults
{
    // ファイル/パス不在系
    internal const int ErrorFileNotFound = unchecked((int)0x80070002);   // ERROR_FILE_NOT_FOUND
    internal const int ErrorPathNotFound = unchecked((int)0x80070003);   // ERROR_PATH_NOT_FOUND

    // 書込/容量系
    internal const int ErrorDiskFull = unchecked((int)0x80070070);        // ERROR_DISK_FULL
    internal const int ErrorHandleDiskFull = unchecked((int)0x80070027);  // ERROR_HANDLE_DISK_FULL

    // ロック/共有違反系（リトライ対象）
    internal const int ErrorSharingViolation = unchecked((int)0x80070020);  // ERROR_SHARING_VIOLATION
    internal const int ErrorLockViolation = unchecked((int)0x80070021);     // ERROR_LOCK_VIOLATION

    // 重複ファイル系
    internal const int ErrorFileExists = unchecked((int)0x80070050);     // ERROR_FILE_EXISTS
    internal const int ErrorAlreadyExists = unchecked((int)0x800700B7);  // ERROR_ALREADY_EXISTS

    // 名前長系
    internal const int ErrorFilenameExcedRange = unchecked((int)0x800700CE); // ERROR_FILENAME_EXCED_RANGE

    // 破損系（7z.dll / Windows が返す CRC・データ不正・フォーマット異常）
    internal const int ErrorCrc = unchecked((int)0x80070017);            // ERROR_CRC
    internal const int ErrorInvalidData = unchecked((int)0x8007000D);    // ERROR_INVALID_DATA
    internal const int ErrorBadFormat = unchecked((int)0x8007000B);      // ERROR_BAD_FORMAT
    internal const int ErrorFileCorrupt = unchecked((int)0x80070570);    // ERROR_FILE_CORRUPT
    internal const int ErrorDiskCorrupt = unchecked((int)0x80070571);    // ERROR_DISK_CORRUPT

    // デバイス切断・準備不可系（USB SSD のスリープによる一時切断、NAS タイムアウト等）
    // ⚠️ RTK レビュー #F-003 対応: 過去事例「23.5GiB 7z 破損 (1Work.7z)」の原因が USB ドライブ
    // 切断による ERROR_DEV_NOT_EXIST だったため、明示的に分類してリトライ対象から除外する
    // （リトライしても復旧しない、かつユーザーに再接続を促すべきエラー）。
    internal const int ErrorDevNotExist = unchecked((int)0x800703E3);    // ERROR_DEV_NOT_EXIST
    internal const int ErrorNotReady = unchecked((int)0x80070015);       // ERROR_NOT_READY
    internal const int ErrorBadNetpath = unchecked((int)0x80070035);     // ERROR_BAD_NETPATH
    internal const int ErrorNetworkBusy = unchecked((int)0x80070036);    // ERROR_NETWORK_BUSY

    /// <summary>
    /// 外部デバイス切断 / ネットワーク不通系のエラーかどうか。
    /// USB SSD のスリープ、リムーバブルメディアの取り外し、NAS タイムアウト等。
    /// これらはアプリ側のリトライで復旧しないため、即時エラー化してユーザーに通知する。
    /// </summary>
    internal static bool IsDeviceDisconnected(int hResult) =>
        hResult is ErrorDevNotExist or ErrorNotReady or ErrorBadNetpath or ErrorNetworkBusy;

    /// <summary>
    /// 破損系エラーかどうか（CRC / 不正データ / 不正フォーマット / ファイル破損 / ディスク破損）。
    /// </summary>
    internal static bool IsCorrupted(int hResult) =>
        hResult is ErrorCrc or ErrorInvalidData or ErrorBadFormat
            or ErrorFileCorrupt or ErrorDiskCorrupt;
}
