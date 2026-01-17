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
        if (Application.Current == null) return null;

        // UIスレッド以外から呼ばれた場合はInvokeして安全に取得
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(GetActiveWindow);
        }

        // 1. アクティブなウィンドウがあればそれを優先（ProgressWindowなど）
        var activeWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible);
        if (activeWindow != null) return activeWindow;

        // 2. アクティブなウィンドウがない場合は、最後に表示された表示中のウィンドウを探す
        var lastVisibleWindow = Application.Current.Windows.OfType<Window>().LastOrDefault(w => w.IsVisible);
        if (lastVisibleWindow != null) return lastVisibleWindow;

        // 3. 最後にMainWindow
        return Application.Current.MainWindow;
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
        Logger.Log($"情報メッセージ表示: {title} - {message}");
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
        Logger.Log($"成功メッセージ表示: {title} - {message}");
        MessageBox.Show(GetActiveWindow(), message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
