using System.Text;

namespace Lhamiel.Util;

/// <summary>
/// 設定ファイル類（settings.json / .lhaignore）を「一時ファイル → 上書き Move」で原子的に書き込む共通ヘルパー。
/// <para>
/// 旧実装は <c>File.WriteAllText</c> の直後に <c>File.Move</c> していたため、一時ファイルの内容が
/// OS のキャッシュに残ったまま rename だけが先にディスクへ反映され得た。その状態で電源断・OS クラッシュが
/// 起きると、置き換え後のファイルが 0 バイトや途中までの内容になる（rename のメタデータだけ永続化された状態）。
/// ここでは一時ファイルを <see cref="FileStream"/> で書いて <see cref="FileStream.Flush(bool)"/>(<c>true</c>)
/// （= FlushFileBuffers）で実データをディスクへ確定させてから Move する。
/// </para>
/// <para>
/// 一時ファイル名は従来どおり <c>{destination}.{GUID:N}.tmp</c>。並行書込み（AutoSave と UI 操作、
/// 外部エディタの保存など）でも一時ファイル同士が衝突しない。失敗時は一時ファイルを best-effort で削除して
/// 例外を再スローする（削除できなかった残骸 「名前.guid.tmp」 は TempCleanup の走査対象外のため残り得るが、本体ファイルには影響しない）。
/// </para>
/// <para>
/// 上書き Move は、AV / 検索インデクサ / バックアップが対象ファイルを一瞬掴むと SHARING_VIOLATION や
/// 一時的な AccessDenied で散発失敗するため、<see cref="LockedFileRetryPolicy"/>（指数バックオフ）でリトライする。
/// ディスクフル・パス不正などの永続エラーは同ポリシーが即時打ち切る。
/// </para>
/// </summary>
internal static class AtomicFileWriter
{
    /// <summary>
    /// <paramref name="content"/> を <paramref name="encoding"/> で一時ファイルへ書き、ディスクへフラッシュしてから
    /// <paramref name="destinationPath"/> へ上書き Move する。
    /// </summary>
    /// <param name="destinationPath">書込み先の絶対パス。親ディレクトリは呼び出し側で用意する。</param>
    /// <param name="content">書き込む文字列。</param>
    /// <param name="encoding">
    /// 文字エンコーディング。プリアンブル（BOM）を持つエンコーディングなら先頭に BOM を書く。
    /// <c>File.WriteAllText(path, content)</c> 相当にしたい場合は BOM なし UTF-8、
    /// <c>File.WriteAllText(path, content, Encoding.UTF8)</c> 相当にしたい場合は <see cref="Encoding.UTF8"/>（BOM あり）を渡す。
    /// </param>
    internal static void WriteAllText(string destinationPath, string content, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(destinationPath);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(encoding);

        // GUID 付き一時ファイル名で並列書込時の衝突も回避する（従来の命名を維持）。
        var tmpPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            WriteAndFlush(tmpPath, content, encoding);
            LockedFileRetryPolicy.Execute(
                () => File.Move(tmpPath, destinationPath, overwrite: true),
                destinationPath);
        }
        catch
        {
            // tmp の後始末（best-effort）。削除失敗は無視し、元の例外を優先して再スローする。
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); }
            catch { /* best-effort: 残骸は本体に影響しないため無視する */ }
            throw;
        }
    }

    /// <summary>
    /// <paramref name="destinationPath"/> が存在しない場合に限り、<paramref name="content"/> を原子的に作成する。
    /// 同じディレクトリの GUID 付き一時ファイルへ書いてディスクへフラッシュしてから、
    /// 上書きしない Move（<c>overwrite: false</c>）で確定させる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 初回作成（.lhaignore の既定内容・旧設定からの移行）用。既存ファイルを上書きしないため、
    /// 「存在確認 → 作成」の間に外部エディタや別プロセスが先に作成した場合でも、その内容を壊さない。
    /// 既存ファイルは置き換え対象ではないので <see cref="LockedFileRetryPolicy"/> による再試行は行わない。
    /// </para>
    /// <para>
    /// Move が失敗した時点で <paramref name="destinationPath"/> が存在すれば「他者が先に作成した（競合負け）」
    /// として false を返す。それ以外の失敗は例外のまま呼び出し側へ返す。
    /// どの経路でも一時ファイルは best-effort で削除する。
    /// </para>
    /// </remarks>
    /// <returns>このメソッドがファイルを作成した場合 true、既に存在していた場合 false。</returns>
    internal static bool TryCreateNew(string destinationPath, string content, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(destinationPath);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(encoding);

        var tmpPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            WriteAndFlush(tmpPath, content, encoding);
            try
            {
                File.Move(tmpPath, destinationPath, overwrite: false);
                return true;
            }
            catch (IOException) when (File.Exists(destinationPath))
            {
                // 既存ファイルがある（作成競合に負けた）: 相手の内容を尊重し、自分の一時ファイルだけ片付ける。
                TryDeleteTemp(tmpPath);
                return false;
            }
        }
        catch
        {
            TryDeleteTemp(tmpPath);
            throw;
        }
    }

    /// <summary>
    /// 一時ファイルを best-effort で削除する。削除失敗は無視する（残骸は TempCleanup の対象外で残り得るが、本体ファイルには影響しない）。
    /// </summary>
    private static void TryDeleteTemp(string tmpPath)
    {
        try { if (File.Exists(tmpPath)) File.Delete(tmpPath); }
        catch { /* best-effort: 残骸は本体に影響しないため無視する */ }
    }

    /// <summary>
    /// 新規の一時ファイルへ書き込み、OS キャッシュではなくディスクまでフラッシュする。
    /// </summary>
    private static void WriteAndFlush(string tmpPath, string content, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(content);
        // CreateNew: GUID 名なので通常衝突しないが、万一既存ファイルがあっても他者のファイルを上書きしない。
        // FileShare.None: フラッシュ完了まで他プロセスに中途半端な内容を読ませない。
        using var stream = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 1, FileOptions.None);
        stream.Write(preamble);
        stream.Write(body);
        // flushToDisk: true で FlushFileBuffers を呼び、Move（rename）より先に実データを永続化する。
        stream.Flush(flushToDisk: true);
    }
}
