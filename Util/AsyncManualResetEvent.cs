namespace Lhamiel.Util;

/// <summary>
/// 非同期で待機可能なマニュアルリセットイベントを提供します。
/// </summary>
public class AsyncManualResetEvent
{
    /// <summary>
    /// 現在のタスク完了を管理するための TaskCompletionSource
    /// </summary>
    private TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// AsyncManualResetEvent の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="initialState">初期状態でイベントがセットされている場合は true、それ以外の場合は false。</param>
    public AsyncManualResetEvent(bool initialState)
    {
        if (initialState)
        {
            _tcs.SetResult();
        }
    }

    /// <summary>
    /// イベントがセットされるのを非同期で待機します。
    /// </summary>
    /// <returns>待機状態を表すタスク</returns>
    public Task WaitAsync()
    {
        // 現在の TaskCompletionSource のタスクを返す
        return _tcs.Task;
    }

    /// <summary>
    /// イベントがセットされるのを非同期で待機します。
    /// </summary>
    /// <param name="cancellationToken">キャンセル トークン</param>
    /// <returns>待機状態を表すタスク</returns>
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        // 現在の TaskCompletionSource のタスクに対してキャンセル可能な待機を行う
        return _tcs.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// イベントをセットし、待機しているタスクを完了させます。
    /// </summary>
    public void Set()
    {
        // 成功したかどうかに関わらず完了状態にする
        _tcs.TrySetResult();
    }

    /// <summary>
    /// イベントをリセットし、以降の待機タスクが完了しないようにします。
    /// </summary>
    public void Reset()
    {
        while (true)
        {
            // 現在のインスタンスを取得
            var tcs = _tcs;
            
            // 未完了の状態で既にあれば何もしない、完了済みであれば新しいインスタンスと入れ替える
            if (!tcs.Task.IsCompleted ||
                Interlocked.CompareExchange(ref _tcs, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), tcs) == tcs)
            {
                return;
            }
        }
    }

    /// <summary>
    /// イベントがセットされているかどうか（タスクが完了しているかどうか）を取得します。
    /// </summary>
    public bool IsSet
    {
        get
        {
            // 現在のタスクの状態を返す
            return _tcs.Task.IsCompleted;
        }
    }
}
