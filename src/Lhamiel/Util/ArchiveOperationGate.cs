namespace Lhamiel.Util;

/// <summary>
/// 各圧縮・展開 worker 内の操作をプロセス単位で直列化するゲート。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="NativeArchiveGate"/> は 7z.dll への接触だけを保護するため、展開後の最終移動や
/// 圧縮結果の atomic swap、進捗ウィンドウは並行し得る。全アーカイブ受付は本ゲートの前で
/// 独立 worker に渡すため、追加要求をメインプロセスの待ち行列へ入れない。
/// </para>
/// <para>
/// 取得箇所は <c>App.ProcessCommandLineFilesCore</c> に限定する。
/// 非リエントラントなので、配下の ArchiveProcessor から再取得してはならない。
/// </para>
/// </remarks>
internal static class ArchiveOperationGate
{
    private static readonly SemaphoreSlim s_gate = new(1, 1);

    public static async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await s_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser();
    }

    private sealed class Releaser : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                s_gate.Release();
        }
    }
}
