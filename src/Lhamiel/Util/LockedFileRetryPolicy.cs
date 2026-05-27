namespace Lhamiel.Util;

/// <summary>
/// AV/EDR/Search Indexer 等による一時的なファイルロックに対する
/// 指数バックオフリトライポリシー。IO 操作をラップして透過的にリトライする。
/// </summary>
internal static class LockedFileRetryPolicy
{
    // HResult 定数は Util/WindowsHResults.cs に集約 (RTK レビュー #B2-002 対応)。
    // ここでは using static や型エイリアスを使わず、明示的に WindowsHResults.* 経由で参照する。

    /// <summary>
    /// <paramref name="action"/> を指数バックオフ付きでリトライ実行する。
    /// 一時的なロック（SHARING_VIOLATION / LOCK_VIOLATION / IOException / UnauthorizedAccessException）
    /// のみリトライし、それ以外の例外は即座に再スローする。
    /// <para>
    /// <paramref name="cancellationToken"/> が指定されている場合、リトライ待機中もキャンセルを受け付ける
    /// （<see cref="System.Threading.Thread.Sleep(int)"/> の代わりに <see cref="WaitHandle"/> で
    /// キャンセル可能な待機を行う）。RTK レビュー #B1-005 対応。
    /// </para>
    /// </summary>
    /// <param name="action">実行する IO 操作</param>
    /// <param name="contextPath">ログ用のパス情報</param>
    /// <param name="maxAttempts">最大試行回数（デフォルト 6）</param>
    /// <param name="initialDelayMs">初回リトライの待機ミリ秒（デフォルト 50、以後倍増）</param>
    /// <param name="cancellationToken">リトライ待機をキャンセルするためのトークン</param>
    internal static void Execute(Action action, string contextPath, int maxAttempts = 6, int initialDelayMs = 50,
        CancellationToken cancellationToken = default)
    {
        var delayMs = initialDelayMs;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                action();
                if (attempt > 1)
                    Logger.Log($"操作成功（リトライ {attempt - 1} 回）: {contextPath}");
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (IsTransientLockError(ex) && attempt < maxAttempts)
            {
                var jitteredDelay = ApplyJitter(delayMs);
                Logger.Log(
                    $"一時的なロックで操作失敗（試行 {attempt}/{maxAttempts}）: {contextPath}: {ex.Message}。{jitteredDelay}ms 待機して再試行",
                    LogLevel.Warning);
                SleepCancellable(jitteredDelay, cancellationToken);
                delayMs *= 2;
            }
        }
    }

    /// <summary>
    /// <paramref name="func"/> を指数バックオフ付きでリトライ実行する（戻り値あり版）。
    /// <paramref name="cancellationToken"/> でリトライ待機をキャンセル可能。
    /// </summary>
    internal static T Execute<T>(Func<T> func, string contextPath, int maxAttempts = 6, int initialDelayMs = 50,
        CancellationToken cancellationToken = default)
    {
        var delayMs = initialDelayMs;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = func();
                if (attempt > 1)
                    Logger.Log($"操作成功（リトライ {attempt - 1} 回）: {contextPath}");
                return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (IsTransientLockError(ex) && attempt < maxAttempts)
            {
                var jitteredDelay = ApplyJitter(delayMs);
                Logger.Log(
                    $"一時的なロックで操作失敗（試行 {attempt}/{maxAttempts}）: {contextPath}: {ex.Message}。{jitteredDelay}ms 待機して再試行",
                    LogLevel.Warning);
                SleepCancellable(jitteredDelay, cancellationToken);
                delayMs *= 2;
            }
        }
    }

    /// <summary>
    /// <see cref="System.Threading.Thread.Sleep(int)"/> のキャンセル対応版。
    /// <paramref name="cancellationToken"/> が cancel されると即座に <see cref="OperationCanceledException"/> を投げる。
    /// </summary>
    private static void SleepCancellable(int delayMs, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            Thread.Sleep(delayMs);
            return;
        }
        // WaitHandle.WaitOne(delayMs) はタイムアウト時に false、シグナル時 (= キャンセル) に true を返す。
        if (cancellationToken.WaitHandle.WaitOne(delayMs))
            cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// 指数バックオフに ±25% のランダムジッタを加算する（thundering herd 緩和）。
    /// 並列ジョブが完全同期で再試行することによる AV/EDR との競合悪化を防ぐ。
    /// RTK レビュー #F-011 対応。
    /// </summary>
    private static int ApplyJitter(int delayMs)
    {
        // delayMs が小さいと jitter range が 0 になり Random.Next が ArgumentOutOfRange を出すので最小 1ms 保証
        var range = Math.Max(1, delayMs / 4);
        return delayMs + Random.Shared.Next(-range, range + 1);
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
                var jitteredDelay = ApplyJitter(delayMs);
                Logger.Log(
                    $"一時的なロックで操作失敗（試行 {attempt}/{maxAttempts}）: {contextPath}: {ex.Message}。{jitteredDelay}ms 待機して再試行",
                    LogLevel.Warning);
                await Task.Delay(jitteredDelay, cancellationToken);
                delayMs *= 2;
            }
        }
    }

    private static bool IsTransientLockError(Exception ex)
    {
        if (ex.HResult is WindowsHResults.ErrorSharingViolation or WindowsHResults.ErrorLockViolation)
            return true;

        if (ex is not (IOException or UnauthorizedAccessException))
            return false;

        // リトライしても解決しない永続的エラーを除外。
        // ※ デバイス切断系 (ERROR_DEV_NOT_EXIST / ERROR_NOT_READY / ERROR_BAD_NETPATH / ERROR_NETWORK_BUSY)
        //    は USB SSD スリープ / NAS タイムアウト等が原因で、リトライしても復旧しない
        //    かつユーザーに再接続を促すべき種類なので、即時打ち切り対象に含める。
        //    RTK レビュー #F-003 対応。
        if (ex.HResult is WindowsHResults.ErrorDiskFull or WindowsHResults.ErrorHandleDiskFull
            or WindowsHResults.ErrorFilenameExcedRange
            or WindowsHResults.ErrorFileNotFound or WindowsHResults.ErrorPathNotFound
            or WindowsHResults.ErrorFileExists or WindowsHResults.ErrorAlreadyExists
            or WindowsHResults.ErrorCrc or WindowsHResults.ErrorInvalidData
            or WindowsHResults.ErrorBadFormat or WindowsHResults.ErrorFileCorrupt
            or WindowsHResults.ErrorDevNotExist or WindowsHResults.ErrorNotReady
            or WindowsHResults.ErrorBadNetpath or WindowsHResults.ErrorNetworkBusy)
            return false;

        // IOException: ファイルシステムドライバーによっては標準 HResult 以外の
        // ロック関連エラーを発行する場合がある。リトライ回数は制限されているため許容。
        // UnauthorizedAccessException: AV ソフトが書き込み直後に一時的にアクセスを遮断する場合がある。
        return true;
    }
}
