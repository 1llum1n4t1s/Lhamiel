using Cube.FileSystem.SevenZip;
namespace Lhamiel.Util;

/// <summary>
/// キャンセル可能な進捗報告クラス
/// </summary>
/// <typeparam name="T">進捗情報の型</typeparam>
/// <param name="handler">進捗を処理するデリゲート</param>
/// <param name="token">キャンセルトークン</param>
internal class CancellableProgress<T>(Action<T> handler, CancellationToken token) : IProgress<T>, IDisposable
{
    /// <summary>
    /// 進捗を報告する
    /// </summary>
    /// <param name="value">報告する値</param>
    /// <remarks>
    /// キャンセル時はコールバック内では例外をスローしない。Report.Cancel = true のみ設定し、
    /// ネイティブコード（7z.dll）が処理を止めた後に呼び出し元で OperationCanceledException をスローする。
    /// コールバック内でスローするとライブラリが Cancel を返せず、ネイティブがコールバックを繰り返し呼び例外が連発する。
    ///
    /// また、handler（利用側のデリゲート）がスローした例外も P/Invoke 境界を越えると未定義動作になるため、
    /// ここで確実に吸収する。Native AOT 環境ではとくに致命的になりうる。
    /// </remarks>
    public void Report(T value)
    {
        if (token.IsCancellationRequested && value is Report report)
        {
            report.Cancel = true;
            return;
        }

        try
        {
            handler(value);
        }
        catch (Exception ex)
        {
            // handler 内の例外がネイティブコールバック（7z.dll）に伝搬すると P/Invoke 境界を越える未定義動作になる。
            // ここで必ず吸収してログに残す。ただしキャンセル時は Report.Cancel で伝える。
            Logger.Log($"進捗コールバック内で例外が発生しました（吸収）: {ex.Message}", LogLevel.Warning);
            if (token.IsCancellationRequested && value is Report report2)
                report2.Cancel = true;
        }
    }

    /// <summary>
    /// リソースを解放する
    /// </summary>
    public void Dispose()
    {
        // 現時点では特別な解放処理は不要だが、IDisposableを実装することで
        // 利用側での明示的なクリーンアップを可能にする
    }
}
