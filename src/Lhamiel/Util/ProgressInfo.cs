namespace Lhamiel.Util;

/// <summary>
/// 進捗更新情報（イミュータブルな値オブジェクト）
/// </summary>
/// <param name="Percentage">進捗率（0-100、-1は不確定状態）</param>
/// <param name="Status">SetIndeterminate 表示用メッセージ（確定進捗時は空文字）</param>
/// <param name="IsIndeterminate">不確定進捗（マーキー表示）かどうか</param>
public record ProgressInfo(int Percentage, string Status, bool IsIndeterminate = false)
{
    /// <summary>
    /// 不確定進捗（マーキー表示）用コンストラクタ
    /// </summary>
    public ProgressInfo(string status) : this(-1, status, true) { }
}
