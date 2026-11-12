using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Lhamiel.View;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        AvaloniaXamlLoader.Load(this);
        // アクリル代替背景 → 共通枠 → アイコンの順で初期化する（順序は DialogChrome.InitializeDialog が固定）。
        DialogChrome.InitializeDialog(this);
    }

    public ConfirmDialog(string message, string title) : this()
    {
        Title = title;
        var messageText = this.FindControl<TextBlock>("MessageText");
        if (messageText != null)
            messageText.Text = message;
    }

    private void YesButton_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void NoButton_Click(object? sender, RoutedEventArgs e) => Close(false);
}
