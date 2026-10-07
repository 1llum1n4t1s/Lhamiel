using System.Security.Cryptography;
using System.Text;

namespace Lhamiel.Util;

/// <summary>同じファイルへの更新だけを、圧縮プロセス間でも直列化する。</summary>
internal static class CrossProcessResourceGate
{
    internal static IDisposable Enter(string path, CancellationToken cancellationToken = default)
    {
        var identities = new[]
        {
            OutputPathIdentity.GetCanonicalPath(path).ToUpperInvariant(),
            // 旧 worker の表記は long-path prefix も含め、そのまま維持する。
            Path.GetFullPath(path).ToUpperInvariant()
        }.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
        var leases = new List<IDisposable>();
        try
        {
            // 全キーを同じ順で取得し、実パス表記を使う旧 worker とも循環待ちを作らない。
            foreach (var identity in identities) leases.Add(EnterCore(identity, cancellationToken));
            return new CombinedLease(leases);
        }
        catch
        {
            DisposeReverse(leases);
            throw;
        }
    }

    // 固定 AppData 内のメタデータは、稼働中の旧 worker と同じ lexical key で保護する。
    // 出力パスの別名解決とは別契約にし、v1 台帳との共存中も mutex を分断しない。
    internal static IDisposable EnterMetadata(string path, CancellationToken cancellationToken = default)
        => EnterCore(Path.GetFullPath(path).ToUpperInvariant(), cancellationToken);

    private static IDisposable EnterCore(string identity, CancellationToken cancellationToken)
    {
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
        => EnterAsyncCore(path, cancellationToken, metadata: false);

    internal static Task<IDisposable> EnterMetadataAsync(string path, CancellationToken cancellationToken)
        => EnterAsyncCore(path, cancellationToken, metadata: true);

    private static Task<IDisposable> EnterAsyncCore(string path, CancellationToken cancellationToken, bool metadata)
    {
        var acquired = new TaskCompletionSource<IDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Mutex の所有権はスレッドに属する。取得から解放まで専用スレッドで保持し、
        // 圧縮の await や UI スレッドへの復帰で所有者が変わらないようにする。
        _ = Task.Factory.StartNew(() =>
        {
            try
            {
                using var lease = metadata ? EnterMetadata(path, cancellationToken) : Enter(path, cancellationToken);
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

    private static void DisposeReverse(List<IDisposable> leases)
    {
        for (var index = leases.Count - 1; index >= 0; index--) leases[index].Dispose();
    }

    private sealed class CombinedLease(List<IDisposable> leases) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DisposeReverse(leases);
        }
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
