using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lhamiel.Util;

/// <summary>
/// ネイティブ 7z.dll (1llum1n4t1s.Sevenzip) への接触をプロセス全体で 1 スロットに直列化するゲート。
/// </summary>
/// <remarks>
/// <para>
/// ライブラリの共有シングルトン <c>SevenZipLibrary</c>（参照カウント + COM オブジェクト追跡）は
/// <b>直列化を前提</b>に設計されており、複数の <c>ArchiveReader</c> / <c>ArchiveWriter</c> を
/// 並行動作させることをサポートしていない（ライブラリ側 <c>SevenZipLibrary.cs</c> のドキュメント参照）。
/// ネイティブ 7z.dll 自体は内部状態を持たず thread-safe だが、ラッパーの COM ライフタイム管理
/// （Acquire/Dispose による参照カウント・FinalRelease・DLL アンロード）が直列実行を前提としているため、
/// 呼び出し側で 1 スロットに直列化することでライブラリの契約を満たす。
/// </para>
/// <para>
/// Lhamiel はバッチ展開・圧縮で <see cref="ArchiveProgressHelper.IoBoundParallelism"/>（2〜4）の
/// 並列度を使うため、各 <c>ArchiveReader</c> / <c>ArchiveWriter</c> の
/// 「生成 → 使用 → Dispose」ライフサイクル全体を本ゲートで囲み、ネイティブ接触を直列化する。
/// 展開後の最終移動・MotW 伝播などの純 I/O 後処理はゲート外で並行のまま残る。
/// </para>
/// <para>
/// <b>再入（リエントラント）非対応:</b> 本ゲートは非リエントラントな <see cref="SemaphoreSlim"/>(1,1)。
/// 同一フローでゲート保持中にさらにゲートを取得するとデッドロックする。Lhamiel の全ネイティブ
/// 接触点は逐次の兄弟関係（容量推定・構造解析・衝突検出・展開・CRC 検証は順番に実行され、
/// あるネイティブスコープ内で別のネイティブスコープを開かない）であることを確認済みのため、
/// 入れ子は発生しない。新たにネイティブ接触点を追加するときは、既存ゲートスコープの内側で
/// 取得しないよう逐次に配置すること。
/// </para>
/// </remarks>
internal static class NativeArchiveGate
{
    // プロセス全体で 1 つの直列化スロット。
    private static readonly SemaphoreSlim s_gate = new(1, 1);

    /// <summary>
    /// ゲートを同期的に取得する。解放は戻り値の <see cref="IDisposable.Dispose"/> で行う。
    /// </summary>
    /// <remarks>
    /// 呼び出しスレッドをブロックするため、ネイティブ読み取りが短時間で完了するリーフ用途
    /// （構造解析・容量推定など。多くは <c>Task.Run</c> 配下のスレッドプールスレッドで実行される）
    /// に使う。長時間ゲートを保持する展開・圧縮本体は <see cref="EnterAsync"/> を使う。
    /// </remarks>
    public static IDisposable Enter(CancellationToken cancellationToken = default)
    {
        s_gate.Wait(cancellationToken);
        return new Releaser();
    }

    /// <summary>
    /// ゲートを非同期に取得する。解放は戻り値の <see cref="IDisposable.Dispose"/> で行う。
    /// </summary>
    /// <remarks>
    /// ゲート待機中にスレッドをブロックしないため、展開・圧縮の本体（reader/writer を長時間保持する
    /// スコープ）に使う。
    /// </remarks>
    public static async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await s_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser();
    }

    /// <summary>
    /// ゲートの解放用ハンドル。<see cref="Dispose"/> は冪等で、二重解放（SemaphoreFullException）を防ぐ。
    /// </summary>
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
