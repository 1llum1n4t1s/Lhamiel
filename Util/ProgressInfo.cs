namespace Lhamiel.Util;

/// <summary>
/// 進捗更新情報
/// </summary>
public class ProgressInfo
{
    public ProgressInfo(int percentage, string status)
    {
        Percentage = percentage;
        Status = status;
    }

    /// <summary>
    /// 進捗率（0-100）
    /// </summary>
    public int Percentage { get; }

    /// <summary>
    /// ステータスメッセージ（内部ログ等で使用）
    /// </summary>
    public string Status { get; }
}
