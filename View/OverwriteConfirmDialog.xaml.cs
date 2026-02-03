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
    public OverwriteConfirmDialog() : this("ファイルの置き換え", "ファイルは既に存在します。\n\n置き換えますか？") { }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="title">タイトル</param>
    /// <param name="message">メッセージ本文</param>
    public OverwriteConfirmDialog(string title, string message)
    {
        Title = title;
        InitializeComponent();
        var messageText = this.FindControl<TextBlock>("MessageTextBlock");
        if (messageText != null)
            messageText.Text = message;
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
