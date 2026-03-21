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
        EnsureButtonText("YesButton", App.Text("Button.Yes"));
        EnsureButtonText("NoButton", App.Text("Button.No"));
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// ボタンテキストが空の場合にフォールバックテキストを設定する
    /// </summary>
    private void EnsureButtonText(string controlName, string fallbackText)
    {
        var btn = this.FindControl<Button>(controlName);
        if (btn != null && btn.Content is null or (string { Length: 0 }))
            btn.Content = fallbackText;
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
