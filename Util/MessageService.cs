using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;

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
    private static async Task<Window?> GetActiveWindowAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            return GetActiveWindowInternal(desktop);

        return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => GetActiveWindowInternal(desktop));
    }

    /// <summary>
    /// UIスレッド上でアクティブなウィンドウを取得する
    /// </summary>
    private static Window? GetActiveWindowInternal(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // 1. アクティブなウィンドウがあればそれを優先（ProgressWindowなど）
        var activeWindow = desktop.Windows.FirstOrDefault(w => w.IsActive && w.IsVisible);
        if (activeWindow != null) return activeWindow;

        // 2. アクティブなウィンドウがない場合は、最後に表示された表示中のウィンドウを探す
        var lastVisibleWindow = desktop.Windows.LastOrDefault(w => w.IsVisible);
        if (lastVisibleWindow != null) return lastVisibleWindow;

        // 3. 最後にMainWindow
        return desktop.MainWindow;
    }

    /// <summary>
    /// エラーメッセージを表示
    /// </summary>
    /// <param name="message">メッセージ本文</param>
    /// <param name="title">タイトル（省略可）</param>
    public static async void ShowError(string message, string title = "エラー")
    {
        Logger.Log($"エラーメッセージ表示: {title} - {message}", LogLevel.Error);
        var window = await GetActiveWindowAsync();
        var dialog = CreateMessageWindow(title, message);
        if (window != null)
        {
            await dialog.ShowDialog(window);
        }
    }

    private static Window CreateMessageWindow(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 450,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var okButton = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Avalonia.Thickness(0, 0, 0, 20) };
        okButton.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = message, Margin = new Avalonia.Thickness(20), MaxWidth = 400, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                okButton
            }
        };
        return dialog;
    }

    /// <summary>
    /// 情報メッセージを表示
    /// </summary>
    /// <param name="message">メッセージ本文</param>
    /// <param name="title">タイトル（省略可）</param>
    public static async void ShowInfo(string message, string title = "情報")
    {
        Logger.Log($"情報メッセージ表示: {title} - {message}");
        var window = await GetActiveWindowAsync();
        var dialog = CreateMessageWindow(title, message);
        if (window != null)
        {
            await dialog.ShowDialog(window);
        }
    }

    /// <summary>
    /// 警告メッセージを表示
    /// </summary>
    /// <param name="message">メッセージ本文</param>
    /// <param name="title">タイトル（省略可）</param>
    public static async void ShowWarning(string message, string title = "警告")
    {
        Logger.Log($"警告メッセージ表示: {title} - {message}", LogLevel.Warning);
        var window = await GetActiveWindowAsync();
        var dialog = CreateMessageWindow(title, message);
        if (window != null)
        {
            await dialog.ShowDialog(window);
        }
    }

    /// <summary>
    /// 例外に基づいてエラーメッセージを表示
    /// </summary>
    /// <param name="context">エラーの文脈</param>
    /// <param name="ex">例外オブジェクト</param>
    /// <param name="title">タイトル（省略可）</param>
    public static async void ShowException(string context, Exception ex, string title = "エラー")
    {
        Logger.LogException(context, ex);
        var message = $"{context}\n\n詳細: {ex.Message}";
        var window = await GetActiveWindowAsync();
        var dialog = CreateMessageWindow(title, message);
        if (window != null)
        {
            await dialog.ShowDialog(window);
        }
    }

    /// <summary>
    /// 成功メッセージを表示
    /// </summary>
    /// <param name="message">メッセージ本文</param>
    /// <param name="title">タイトル（省略可）</param>
    public static async void ShowSuccess(string message, string title = "完了")
    {
        Logger.Log($"成功メッセージ表示: {title} - {message}");
        var window = await GetActiveWindowAsync();
        var dialog = CreateMessageWindow(title, message);
        if (window != null)
        {
            await dialog.ShowDialog(window);
        }
    }

    /// <summary>
    /// はい/いいえの確認ダイアログを表示する
    /// </summary>
    /// <param name="message">メッセージ本文</param>
    /// <param name="title">タイトル</param>
    /// <param name="parentWindow">親ウィンドウ（nullの場合は自動検索）</param>
    /// <returns>「はい」が選ばれた場合true</returns>
    public static async Task<bool> ShowYesNoQuestionAsync(string message, string title, Window? parentWindow = null)
    {
        parentWindow ??= await GetActiveWindowAsync();
        var tcs = new TaskCompletionSource<bool>();
        var dialog = new Window
        {
            Title = title,
            Width = 450,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var yesButton = new Button { Content = "はい", Margin = new Avalonia.Thickness(0, 0, 8, 0) };
        var noButton = new Button { Content = "いいえ", Margin = new Avalonia.Thickness(0, 0, 8, 0) };
        yesButton.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        noButton.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 0, 0, 20) },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { yesButton, noButton }
                }
            }
        };
        if (parentWindow != null)
        {
            dialog.Closed += (_, _) => tcs.TrySetResult(false);
            _ = dialog.ShowDialog(parentWindow);
            return await tcs.Task;
        }
        return false;
    }
}
