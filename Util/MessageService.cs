using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
namespace Lhamiel.Util;

/// <summary>
/// メッセージボックス表示を一元管理するサービスクラス（MessageBox.Avalonia 使用）
/// </summary>
public static class MessageService
{
    /// <summary>
    /// アクティブなウィンドウを取得する
    /// </summary>
    /// <returns>アクティブなウィンドウ、またはnull</returns>
    internal static async Task<Window?> GetActiveWindowAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        if (Dispatcher.UIThread.CheckAccess())
            return GetActiveWindowInternal(desktop);

        return await Dispatcher.UIThread.InvokeAsync(() => GetActiveWindowInternal(desktop));
    }

    /// <summary>
    /// UIスレッド上でアクティブなウィンドウを取得する
    /// </summary>
    private static Window? GetActiveWindowInternal(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var activeWindow = desktop.Windows.FirstOrDefault(w => w.IsActive && w.IsVisible);
        if (activeWindow != null) return activeWindow;
        var lastVisibleWindow = desktop.Windows.LastOrDefault(w => w.IsVisible);
        if (lastVisibleWindow != null) return lastVisibleWindow;
        return desktop.MainWindow;
    }

    /// <summary>
    /// エラーメッセージを表示
    /// </summary>
    /// <param name="message">メッセージ本文</param>
    /// <param name="title">タイトル（省略可）</param>
    public static async Task ShowError(string message, string title = "エラー")
    {
        Logger.Log($"エラーメッセージ表示: {title} - {message}", LogLevel.Error);
        var window = await GetActiveWindowAsync();
        var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok, Icon.Error);
        if (window != null)
            await box.ShowWindowDialogAsync(window);
        else
            await box.ShowAsync();
    }

    /// <summary>
    /// 情報メッセージを表示
    /// </summary>
    /// <param name="message">メッセージ本文</param>
    /// <param name="title">タイトル（省略可）</param>
    public static async Task ShowInfo(string message, string title = "情報")
    {
        Logger.Log($"情報メッセージ表示: {title} - {message}");
        var window = await GetActiveWindowAsync();
        var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok, Icon.Info);
        if (window != null)
            await box.ShowWindowDialogAsync(window);
        else
            await box.ShowAsync();
    }

    /// <summary>
    /// 警告メッセージを表示
    /// </summary>
    /// <param name="message">メッセージ本文</param>
    /// <param name="title">タイトル（省略可）</param>
    public static async Task ShowWarning(string message, string title = "警告")
    {
        Logger.Log($"警告メッセージ表示: {title} - {message}", LogLevel.Warning);
        var window = await GetActiveWindowAsync();
        var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok, Icon.Warning);
        if (window != null)
            await box.ShowWindowDialogAsync(window);
        else
            await box.ShowAsync();
    }

    /// <summary>
    /// 例外に基づいてエラーメッセージを表示
    /// </summary>
    /// <param name="context">エラーの文脈</param>
    /// <param name="ex">例外オブジェクト</param>
    /// <param name="title">タイトル（省略可）</param>
    public static async Task ShowException(string context, Exception ex, string title = "エラー")
    {
        Logger.LogException(context, ex);
        var message = $"{context}\n\n詳細: {ex.Message}";
        var window = await GetActiveWindowAsync();
        var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok, Icon.Error);
        if (window != null)
            await box.ShowWindowDialogAsync(window);
        else
            await box.ShowAsync();
    }

    /// <summary>
    /// 成功メッセージを表示
    /// </summary>
    /// <param name="message">メッセージ本文</param>
    /// <param name="title">タイトル（省略可）</param>
    public static async Task ShowSuccess(string message, string title = "完了")
    {
        Logger.Log($"成功メッセージ表示: {title} - {message}");
        var window = await GetActiveWindowAsync();
        var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok, Icon.Success);
        if (window != null)
            await box.ShowWindowDialogAsync(window);
        else
            await box.ShowAsync();
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
        var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.YesNo, Icon.Question);
        ButtonResult result;
        if (parentWindow != null)
            result = await box.ShowWindowDialogAsync(parentWindow);
        else
            result = await box.ShowAsync();
        return result == ButtonResult.Yes;
    }
}
