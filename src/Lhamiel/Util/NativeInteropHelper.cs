namespace Lhamiel.Util;

/// <summary>
/// ネイティブ相互運用時の参照保持用ヘルパー
/// </summary>
public static class NativeInteropHelper
{
    /// <summary>
    /// コールバック等の参照がGCで回収されないよう保持する
    /// </summary>
    /// <param name="values">保持するオブジェクト</param>
    public static void KeepAliveCallbacks(params object?[] values)
    {
        foreach (var v in values)
        {
            GC.KeepAlive(v);
        }
    }
}
