namespace Lhamiel.Util;

/// <summary>
/// ドロップ・CLI・IPC から開始されるトップレベルのアーカイブ操作を、プロセス全体で直列化するゲート。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="NativeArchiveGate"/> は 7z.dll への接触だけを保護するため、展開後の最終移動や
/// 圧縮結果の atomic swap、進捗ウィンドウは並行し得る。本ゲートはユーザー操作全体を 1 単位として
/// キュー化し、第 2 インスタンスの IPC とメイン画面のドロップが重なることを防ぐ。
/// </para>
/// <para>
/// 取得箇所は <c>App.ProcessCommandLineFiles</c> と
/// <c>MainWindowViewModel.ProcessDroppedPathsAsync</c> のトップレベル 2 経路だけに限定する。
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
