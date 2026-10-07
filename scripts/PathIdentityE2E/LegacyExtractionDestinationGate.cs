using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Lhamiel.Util;

/// <summary>
/// 最終展開先と圧縮出力の同一・親子パスをプロセス間でも直列化する非同期ゲート。
/// </summary>
/// <remarks>
/// 異なる展開先の後処理は従来どおり並行できる。同じ展開先の後続処理は先行処理の完了後に
/// 既存ファイルを再検査するため、衝突ダイアログを経ずに並行上書きする競合を防げる。
/// エントリは参照数が 0 になった時点で辞書から除去し、処理したパス数に比例する常駐メモリ増加を避ける。
/// Windows ではルートから親まで共有読み取り、最終パスだけ排他ハンドルを保持する。
/// 圧縮は出力ファイルを最終パスに指定するため、親フォルダーへの展開と協調しつつ、
/// 同じフォルダー内の別ファイルへの圧縮は並行できる。圧縮側の mutex より先に取得する。
/// ロックファイルは安定した名前で残す。DeleteOnClose や削除で名前を再利用すると、
/// 待機側と取得側が別のファイル実体をロックして排他を失うため、解放時も削除しない。
/// プロセス終了時は OS がハンドルを解放する。非 Windows は従来のプロセス内契約を維持する。
/// </remarks>
internal static class LegacyExtractionDestinationGate
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly ConcurrentDictionary<string, Entry> Entries = new(PathComparer);

    public static async Task<IDisposable> EnterAsync(
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationPath));

        Entry entry;
        while (true)
        {
            entry = Entries.GetOrAdd(key, static _ => new Entry());
            lock (entry.SyncRoot)
            {
                // 参照数 0 の解放処理が辞書から削除する直前のエントリを取得した場合は、
                // 削除完了後に新しいエントリを取り直す。
                if (entry.Removed)
                    continue;

                entry.ReferenceCount++;
                break;
            }
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReleaseReference(key, entry, releaseSemaphore: false);
            throw;
        }

        var handles = new List<FileStream>();
        try
        {
            if (OperatingSystem.IsWindows())
                await AcquireProcessLocksAsync(key, handles, cancellationToken).ConfigureAwait(false);
            return new Releaser(key, entry, handles);
        }
        catch
        {
            DisposeHandles(handles);
            ReleaseReference(key, entry, releaseSemaphore: true);
            throw;
        }
    }

    private static async Task AcquireProcessLocksAsync(
        string destinationPath,
        List<FileStream> handles,
        CancellationToken cancellationToken)
    {
        // TEMP の上書きに左右されず、同じユーザーの全 worker が同じ場所を使う。
        var lockDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp", "Lhamiel-extraction-locks");
        Directory.CreateDirectory(lockDirectory);

        var hierarchy = new Stack<string>();
        for (string? path = destinationPath; path is not null; path = Path.GetDirectoryName(path))
            hierarchy.Push(Path.TrimEndingDirectorySeparator(path));

        // 全取得をルート→末端に統一して、親子間の循環待ちを防ぐ。
        while (hierarchy.TryPop(out var path))
        {
            var isDestination = hierarchy.Count == 0;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
            var lockPath = Path.Combine(lockDirectory, hash + ".lock");
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    handles.Add(new FileStream(
                        lockPath,
                        FileMode.OpenOrCreate,
                        isDestination ? FileAccess.ReadWrite : FileAccess.Read,
                        isDestination ? FileShare.None : FileShare.Read));
                    break;
                }
                catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
                {
                    // 共有/ロック違反だけを待つ。アクセス拒否や容量不足は隠さない。
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private static void DisposeHandles(List<FileStream> handles)
    {
        for (var index = handles.Count - 1; index >= 0; index--)
            handles[index].Dispose();
    }

    private static void ReleaseReference(string key, Entry entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
            entry.Semaphore.Release();

        lock (entry.SyncRoot)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount != 0)
                return;

            entry.Removed = true;
            // Removed=true を見た取得側はこのエントリを使用しないため、キーだけの TryRemove でも
            // 新しいエントリを誤って消す競合は起きない（新規追加は本エントリの削除後のみ）。
            Entries.TryRemove(key, out _);
        }
    }

    private sealed class Entry
    {
        public object SyncRoot { get; } = new();
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int ReferenceCount { get; set; }
        public bool Removed { get; set; }
    }

    private sealed class Releaser(string key, Entry entry, List<FileStream> handles) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                DisposeHandles(handles);
                ReleaseReference(key, entry, releaseSemaphore: true);
            }
        }
    }
}
