using System.Collections.Concurrent;

namespace Lhamiel.Util;

/// <summary>
/// 同じ最終展開先を使う処理だけを直列化する、パス単位の非同期ゲート。
/// </summary>
/// <remarks>
/// 異なる展開先の後処理は従来どおり並行できる。同じ展開先の後続処理は先行処理の完了後に
/// 既存ファイルを再検査するため、衝突ダイアログを経ずに並行上書きする競合を防げる。
/// エントリは参照数が 0 になった時点で辞書から除去し、処理したパス数に比例する常駐メモリ増加を避ける。
/// </remarks>
internal static class ExtractionDestinationGate
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
            return new Releaser(key, entry);
        }
        catch
        {
            ReleaseReference(key, entry, releaseSemaphore: false);
            throw;
        }
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

    private sealed class Releaser(string key, Entry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                ReleaseReference(key, entry, releaseSemaphore: true);
        }
    }
}
