using System.Windows;

namespace Lhamiel.View;

/// <summary>
/// ProgressWindow.xaml の相互作用ロジック
/// </summary>
public partial class ProgressWindow : Window
{
    public ProgressWindow(string operationType)
    {
        InitializeComponent();
        Title = $"{operationType} - Lhamiel";
    }

    /// <summary>
    /// ファイル名を設定する
    /// </summary>
    /// <param name="fileName">ファイル名</param>
    public void SetFileName(string fileName)
    {
        FileNameTextBlock.Text = fileName;
    }

    /// <summary>
    /// 進捗を更新する
    /// </summary>
    /// <param name="percentage">進捗率（0-100）</param>
    /// <param name="status">ステータスメッセージ</param>
    public void UpdateProgress(int percentage, string status)
    {
        ProgressBar.Value = percentage;
        StatusTextBlock.Text = status;
        ProgressTextBlock.Text = $"{percentage}%";
    }

    /// <summary>
    /// 完了状態を設定する
    /// </summary>
    /// <param name="message">完了メッセージ</param>
    public void SetCompleted(string message)
    {
        ProgressBar.Value = 100;
        StatusTextBlock.Text = message;
        ProgressTextBlock.Text = "100%";
    }
}