using System.Windows;

namespace Lhamiel.Util;

/// <summary>
/// メッセージボックス表示を一元管理するサービスクラス
/// </summary>
public static class MessageService
{
    /// <summary>
    /// アクティブなウィンドウを取得する
    /// </summary>
    /// <returns>アクティブなウィンドウ、またはnull</returns>
    private static Window? GetActiveWindow()
    {
        return Application.Current?.MainWindow ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
    }

    /// <summary>
    /// エラーメッセージを表示
    /// </summary>
    /// <param name="message">メッセージ本文</param>
    /// <param name="title">タイトル（省略可）</param>
    public static void ShowError(string message, string title = "エラー")
    {
        Logger.Log($"エラーメッセージ表示: {title} - {message}", LogLevel.Error);
        MessageBox.Show(GetActiveWindow(), message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>
    /// 情報メッセージを表示
    /// </summary>
    /// <param name="message">メッセージ本文</param>
    /// <param name="title">タイトル（省略可）</param>
    public static void ShowInfo(string message, string title = "情報")
    {
        Logger.Log($"情報メッセージ表示: {title} - {message}", LogLevel.Info);
        MessageBox.Show(GetActiveWindow(), message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// 警告メッセージを表示
    /// </summary>
    /// <param name="message">メッセージ本文</param>
    /// <param name="title">タイトル（省略可）</param>
    public static void ShowWarning(string message, string title = "警告")
    {
        Logger.Log($"警告メッセージ表示: {title} - {message}", LogLevel.Warning);
        MessageBox.Show(GetActiveWindow(), message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>
    /// 確認メッセージを表示
    /// </summary>
    /// <param name="message">メッセージ本文</param>
    /// <param name="title">タイトル（省略可）</param>
    /// <returns>ユーザーが「はい」を選択した場合はtrue</returns>
    public static bool ShowConfirmation(string message, string title = "確認")
    {
        Logger.Log($"確認メッセージ表示: {title} - {message}", LogLevel.Info);
        var result = MessageBox.Show(GetActiveWindow(), message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    /// <summary>
    /// 例外に基づいてエラーメッセージを表示
    /// </summary>
    /// <param name="context">エラーの文脈</param>
    /// <param name="ex">例外オブジェクト</param>
    /// <param name="title">タイトル（省略可）</param>
    public static void ShowException(string context, Exception ex, string title = "エラー")
    {
        Logger.LogException(context, ex);
        var message = $"{context}\n\n詳細: {ex.Message}";
        MessageBox.Show(GetActiveWindow(), message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>
    /// 成功メッセージを表示
    /// </summary>
    /// <param name="message">メッセージ本文</param>
    /// <param name="title">タイトル（省略可）</param>
    public static void ShowSuccess(string message, string title = "完了")
    {
        Logger.Log($"成功メッセージ表示: {title} - {message}", LogLevel.Info);
        MessageBox.Show(GetActiveWindow(), message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
