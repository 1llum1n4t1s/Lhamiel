using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lhamiel.View;

internal enum MessageDialogButtons
{
    Ok,
    YesNo,
}

public partial class MessageDialog : Window
{
    private TaskCompletionSource<bool>? _standaloneCompletion;

    public MessageDialog()
    {
        InitializeComponent();
        // アクリル代替背景 → 共通枠 → アイコンの順で初期化する（順序は DialogChrome.InitializeDialog が固定）。
        DialogChrome.InitializeDialog(this);
    }

    internal MessageDialog(string message, string title, MessageDialogButtons buttons = MessageDialogButtons.Ok)
        : this()
    {
        Title = title;
        MessageText.Text = message;

        var isQuestion = buttons == MessageDialogButtons.YesNo;
        OkButton.IsVisible = !isQuestion;
        YesButton.IsVisible = isQuestion;
        NoButton.IsVisible = isQuestion;
    }

    internal async Task<bool> ShowAsync(Window? owner)
    {
        if (owner != null)
            return await ShowDialog<bool>(owner);

        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _standaloneCompletion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Closed += OnStandaloneClosed;
        Show();
        return await _standaloneCompletion.Task;
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e) => Complete(true);

    private void YesButton_Click(object? sender, RoutedEventArgs e) => Complete(true);

    private void NoButton_Click(object? sender, RoutedEventArgs e) => Complete(false);

    private void Complete(bool result)
    {
        if (_standaloneCompletion != null)
        {
            _standaloneCompletion.TrySetResult(result);
            Close();
            return;
        }

        Close(result);
    }

    private void OnStandaloneClosed(object? sender, EventArgs e)
    {
        Closed -= OnStandaloneClosed;
        _standaloneCompletion?.TrySetResult(false);
    }
}
