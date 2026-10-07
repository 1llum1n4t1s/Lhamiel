using System.Security.Cryptography;
using System.Text;

namespace Lhamiel.Util;

/// <summary>同じファイルへの更新だけを、圧縮プロセス間でも直列化する。</summary>
internal static class CrossProcessResourceGate
{
    internal static IDisposable Enter(string path, CancellationToken cancellationToken = default)
    {
        var identity = Path.GetFullPath(path).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var mutex = new Mutex(false, @"Local\Lhamiel_Resource_" + hash);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (cancellationToken.CanBeCanceled)
                {
                    if (WaitHandle.WaitAny([cancellationToken.WaitHandle, mutex]) == 0)
                        throw new OperationCanceledException(cancellationToken);
                }
                else
                    mutex.WaitOne();
            }
            catch (AbandonedMutexException)
            {
                // 終了したプロセスから所有権を引き継ぎ、出力の存在は取得後に再確認する。
            }
            return new MutexLease(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    internal static Task<IDisposable> EnterAsync(string path, CancellationToken cancellationToken)
    {
        var acquired = new TaskCompletionSource<IDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Mutex の所有権はスレッドに属する。取得から解放まで専用スレッドで保持し、
        // 圧縮の await や UI スレッドへの復帰で所有者が変わらないようにする。
        _ = Task.Factory.StartNew(() =>
        {
            try
            {
                using var lease = Enter(path, cancellationToken);
                acquired.SetResult(new AsyncLease(release));
                release.Task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                acquired.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                acquired.TrySetException(ex);
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        return acquired.Task;
    }

    private sealed class AsyncLease(TaskCompletionSource release) : IDisposable
    {
        public void Dispose() => release.TrySetResult();
    }

    private sealed class MutexLease(Mutex mutex) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
