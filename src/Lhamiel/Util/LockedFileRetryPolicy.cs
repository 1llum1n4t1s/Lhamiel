namespace Lhamiel.Util;

/// <summary>
/// AV/EDR/Search Indexer 等による一時的なファイルロックに対する
/// 指数バックオフリトライポリシー。IO 操作をラップして透過的にリトライする。
/// </summary>
internal static class LockedFileRetryPolicy
{
    private const int HR_ERROR_SHARING_VIOLATION = unchecked((int)0x80070020);
    private const int HR_ERROR_LOCK_VIOLATION = unchecked((int)0x80070021);

    /// <summary>
    /// <paramref name="action"/> を指数バックオフ付きでリトライ実行する。
    /// 一時的なロック（SHARING_VIOLATION / LOCK_VIOLATION / IOException / UnauthorizedAccessException）
    /// のみリトライし、それ以外の例外は即座に再スローする。
    /// </summary>
    /// <param name="action">実行する IO 操作</param>
    /// <param name="contextPath">ログ用のパス情報</param>
    /// <param name="maxAttempts">最大試行回数（デフォルト 6）</param>
    /// <param name="initialDelayMs">初回リトライの待機ミリ秒（デフォルト 50、以後倍増）</param>
    internal static void Execute(Action action, string contextPath, int maxAttempts = 6, int initialDelayMs = 50)
    {
        var delayMs = initialDelayMs;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                if (attempt > 1)
                    Logger.Log($"操作成功（リトライ {attempt - 1} 回）: {contextPath}");
                return;
            }
            catch (Exception ex) when (IsTransientLockError(ex) && attempt < maxAttempts)
            {
                Logger.Log(
                    $"一時的なロックで操作失敗（試行 {attempt}/{maxAttempts}）: {contextPath}: {ex.Message}。{delayMs}ms 待機して再試行",
                    LogLevel.Warning);
                Thread.Sleep(delayMs);
                delayMs *= 2;
            }
        }
    }

    /// <summary>
    /// <paramref name="func"/> を指数バックオフ付きでリトライ実行する（戻り値あり版）。
    /// </summary>
    internal static T Execute<T>(Func<T> func, string contextPath, int maxAttempts = 6, int initialDelayMs = 50)
    {
        var delayMs = initialDelayMs;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var result = func();
                if (attempt > 1)
                    Logger.Log($"操作成功（リトライ {attempt - 1} 回）: {contextPath}");
                return result;
            }
            catch (Exception ex) when (IsTransientLockError(ex) && attempt < maxAttempts)
            {
                Logger.Log(
                    $"一時的なロックで操作失敗（試行 {attempt}/{maxAttempts}）: {contextPath}: {ex.Message}。{delayMs}ms 待機して再試行",
                    LogLevel.Warning);
                Thread.Sleep(delayMs);
                delayMs *= 2;
            }
        }
    }

    /// <summary>
    /// 非同期版。UI スレッドから呼ぶ場合やキャンセル対応が必要な場合向け。
    /// </summary>
    internal static async Task ExecuteAsync(Func<Task> action, string contextPath,
        int maxAttempts = 3, int initialDelayMs = 200, CancellationToken cancellationToken = default)
    {
        var delayMs = initialDelayMs;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await action();
                if (attempt > 1)
                    Logger.Log($"操作成功（リトライ {attempt - 1} 回）: {contextPath}");
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (IsTransientLockError(ex) && attempt < maxAttempts)
            {
                Logger.Log(
                    $"一時的なロックで操作失敗（試行 {attempt}/{maxAttempts}）: {contextPath}: {ex.Message}。{delayMs}ms 待機して再試行",
                    LogLevel.Warning);
                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2;
            }
        }
    }

    private const int HR_ERROR_FILE_NOT_FOUND = unchecked((int)0x80070002);
    private const int HR_ERROR_PATH_NOT_FOUND = unchecked((int)0x80070003);
    private const int HR_ERROR_DISK_FULL = unchecked((int)0x80070070);
    private const int HR_ERROR_HANDLE_DISK_FULL = unchecked((int)0x80070027);
    private const int HR_ERROR_FILE_EXISTS = unchecked((int)0x80070050);
    private const int HR_ERROR_ALREADY_EXISTS = unchecked((int)0x800700B7);
    private const int HR_ERROR_FILENAME_EXCED_RANGE = unchecked((int)0x800700CE);

    private static bool IsTransientLockError(Exception ex)
    {
        if (ex.HResult is HR_ERROR_SHARING_VIOLATION or HR_ERROR_LOCK_VIOLATION)
            return true;

        if (ex is not (IOException or UnauthorizedAccessException))
            return false;

        // リトライしても解決しない永続的エラーを除外
        if (ex.HResult is HR_ERROR_DISK_FULL or HR_ERROR_HANDLE_DISK_FULL or HR_ERROR_FILENAME_EXCED_RANGE
            or HR_ERROR_FILE_NOT_FOUND or HR_ERROR_PATH_NOT_FOUND
            or HR_ERROR_FILE_EXISTS or HR_ERROR_ALREADY_EXISTS)
            return false;

        // IOException: ファイルシステムドライバーによっては標準 HResult 以外の
        // ロック関連エラーを発行する場合がある。リトライ回数は制限されているため許容。
        // UnauthorizedAccessException: AV ソフトが書き込み直後に一時的にアクセスを遮断する場合がある。
        return true;
    }
}
