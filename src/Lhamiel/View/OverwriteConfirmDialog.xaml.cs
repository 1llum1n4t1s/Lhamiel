using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Lhamiel.Util;
namespace Lhamiel.View;

/// <summary>
/// ファイル/フォルダ上書き確認ダイアログ（XAML で View 定義）
/// </summary>
public class OverwriteConfirmDialog : Window
{
    /// <summary>
    /// パラメータなしコンストラクタ（デザイナー・XAML プレビュー用）
    /// </summary>
    public OverwriteConfirmDialog() : this(App.Text("Overwrite.FileTitle"), App.Text("Overwrite.DefaultMessage")) { }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="title">タイトル</param>
    /// <param name="message">メッセージ本文</param>
    public OverwriteConfirmDialog(string title, string message)
    {
        InitializeComponent();
        // XAML の DynamicResource より後に設定して確実に上書きする
        Title = title;
        var messageText = this.FindControl<TextBlock>("MessageTextBlock");
        if (messageText != null)
            messageText.Text = message;

        // ボタンテキストを確実に設定（DynamicResource のフォールバック）
        var yesBtn = this.FindControl<Button>("YesButton");
        var noBtn = this.FindControl<Button>("NoButton");
        if (yesBtn != null && (yesBtn.Content == null || yesBtn.Content is string s1 && string.IsNullOrEmpty(s1)))
            yesBtn.Content = App.Text("Button.Yes");
        if (noBtn != null && (noBtn.Content == null || noBtn.Content is string s2 && string.IsNullOrEmpty(s2)))
            noBtn.Content = App.Text("Button.No");
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void YesButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(OverwriteResult.Yes);
    }

    private void NoButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(OverwriteResult.No);
    }
}
