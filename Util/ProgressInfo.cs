namespace Lhamiel.Util;

/// <summary>
/// 進捗更新情報
/// </summary>
public class ProgressInfo
{
    public ProgressInfo(int percentage, string status, string? currentFileName = null)
    {
        Percentage = percentage;
        Status = status;
        CurrentFileName = currentFileName;
    }

    /// <summary>
    /// 進捗率（0-100）
    /// </summary>
    public int Percentage { get; }

    /// <summary>
    /// ステータスメッセージ
    /// </summary>
    public string Status { get; }

    /// <summary>
    /// 現在処理中のファイル名
    /// </summary>
    public string? CurrentFileName { get; }
}
