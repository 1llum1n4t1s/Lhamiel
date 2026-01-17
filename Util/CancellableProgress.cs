namespace Lhamiel.Util;

/// <summary>
/// キャンセル可能な進捗報告クラス
/// </summary>
/// <typeparam name="T">進捗情報の型</typeparam>
/// <param name="handler">進捗を処理するデリゲート</param>
/// <param name="token">キャンセルトークン</param>
internal class CancellableProgress<T>(Action<T> handler, CancellationToken token) : IProgress<T>
{
    /// <summary>
    /// 進捗を報告する
    /// </summary>
    /// <param name="value">報告する値</param>
    public void Report(T value)
    {
        token.ThrowIfCancellationRequested();
        handler(value);
    }
}
