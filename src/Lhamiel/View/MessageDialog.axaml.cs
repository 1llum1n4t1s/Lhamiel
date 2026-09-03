using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Lhamiel.View;

internal enum MessageDialogButtons
{
    Ok,
    YesNo,
}

public partial class MessageDialog : Window
{
    private readonly TextBlock _messageText;
    private readonly Button _okButton;
    private readonly Button _yesButton;
    private readonly Button _noButton;
    private TaskCompletionSource<bool>? _standaloneCompletion;

    public MessageDialog()
    {
        AvaloniaXamlLoader.Load(this);
        Util.AcrylicFallbackHelper.Attach(this);
        Util.AppIconManager.Apply(this);
        _messageText = GetRequiredControl<TextBlock>("MessageText");
        _okButton = GetRequiredControl<Button>("OkButton");
        _yesButton = GetRequiredControl<Button>("YesButton");
        _noButton = GetRequiredControl<Button>("NoButton");
    }

    internal MessageDialog(string message, string title, MessageDialogButtons buttons = MessageDialogButtons.Ok)
        : this()
    {
        Title = title;
        _messageText.Text = message;

        var isQuestion = buttons == MessageDialogButtons.YesNo;
        _okButton.IsVisible = !isQuestion;
        _yesButton.IsVisible = isQuestion;
        _noButton.IsVisible = isQuestion;
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

    private T GetRequiredControl<T>(string name) where T : Control =>
        this.FindControl<T>(name)
        ?? throw new InvalidOperationException($"ダイアログのコントロールが見つかりません: {name}");
}
