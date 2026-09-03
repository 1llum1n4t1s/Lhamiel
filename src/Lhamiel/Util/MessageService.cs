using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Lhamiel.View;
namespace Lhamiel.Util;

/// <summary>
/// Lhamiel 共通デザインのメッセージダイアログ表示を一元管理するサービスクラス。
/// </summary>
public static class MessageService
{
    private enum MessageKind
    {
        Error,
        Info,
        Warning,
        Success,
    }

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
    /// メッセージボックスを表示する共通処理
    /// </summary>
    private static Task ShowMessageAsync(
        string message,
        string title,
        MessageKind kind,
        LogLevel logLevel = LogLevel.Info,
        bool writeDisplayLog = true)
    {
        if (writeDisplayLog)
            Logger.Log($"{kind}メッセージ表示: {title} - {message}", logLevel);

        return RunOnUiThreadAsync(async () =>
        {
            var owner = await GetActiveWindowAsync();
            var dialog = new MessageDialog(message, title);
            _ = await dialog.ShowAsync(owner);
        });
    }

    /// <summary>
    /// エラーメッセージを表示
    /// </summary>
    public static Task ShowError(string message, string? title = null)
        => ShowMessageAsync(message, title ?? App.Text("Dialog.Error"), MessageKind.Error, LogLevel.Error);

    /// <summary>
    /// 情報メッセージを表示
    /// </summary>
    public static Task ShowInfo(string message, string? title = null)
        => ShowMessageAsync(message, title ?? App.Text("Dialog.Info"), MessageKind.Info);

    /// <summary>
    /// 警告メッセージを表示
    /// </summary>
    public static Task ShowWarning(string message, string? title = null)
        => ShowMessageAsync(message, title ?? App.Text("Dialog.Warning"), MessageKind.Warning, LogLevel.Warning);

    /// <summary>
    /// 例外に基づいてエラーメッセージを表示（LogException で詳細ログを出力済みのため ShowMessageAsync のログは省略）
    /// </summary>
    public static Task ShowException(string context, Exception ex, string? title = null)
    {
        Logger.LogException(context, ex);
        var message = $"{context}\n\n{App.Text("Dialog.Details")}{ex.Message}";
        title ??= App.Text("Dialog.Error");
        return ShowMessageAsync(
            message, title, MessageKind.Error, LogLevel.Error, writeDisplayLog: false);
    }

    /// <summary>
    /// 成功メッセージを表示
    /// </summary>
    public static Task ShowSuccess(string message, string? title = null)
        => ShowMessageAsync(message, title ?? App.Text("Dialog.Completed"), MessageKind.Success);

    /// <summary>
    /// 一時ウィンドウを閉じ終えてからメッセージを表示し、ダイアログが閉じるまで待機する。
    /// </summary>
    internal static async Task ShowAfterClosingAsync(
        Func<Task>? closeTransientWindow,
        Func<Task> showMessage)
    {
        ArgumentNullException.ThrowIfNull(showMessage);

        if (closeTransientWindow != null)
            await closeTransientWindow();

        await showMessage();
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
        return await RunOnUiThreadAsync(async () =>
        {
            parentWindow ??= await GetActiveWindowAsync();
            var dialog = new MessageDialog(message, title, MessageDialogButtons.YesNo);
            return await dialog.ShowAsync(parentWindow);
        });
    }

    private static Task RunOnUiThreadAsync(Func<Task> action) =>
        RunOnUiThreadAsync(async () =>
        {
            await action();
            return true;
        });

    private static Task<T> RunOnUiThreadAsync<T>(Func<Task<T>> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return action();

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.TrySetResult(await action());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        return completion.Task;
    }
}
