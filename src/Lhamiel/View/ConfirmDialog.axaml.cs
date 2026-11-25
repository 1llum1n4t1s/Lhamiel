using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lhamiel.View;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
        // アクリル代替背景 → 共通枠 → アイコンの順で初期化する（順序は DialogChrome.InitializeDialog が固定）。
        DialogChrome.InitializeDialog(this);
    }

    public ConfirmDialog(string message, string title) : this()
    {
        Title = title;
        MessageText.Text = message;
    }

    private void YesButton_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void NoButton_Click(object? sender, RoutedEventArgs e) => Close(false);
}
