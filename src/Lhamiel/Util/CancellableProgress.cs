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
    /// </remarks>
    public void Report(T value)
    {
        if (token.IsCancellationRequested && value is Report report)
        {
            report.Cancel = true;
            return;
        }

        handler(value);
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
