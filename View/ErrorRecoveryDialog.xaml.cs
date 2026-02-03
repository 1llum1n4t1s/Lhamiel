using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Lhamiel.Util;
using System.Text;
namespace Lhamiel.View;

/// <summary>
/// エラー回復オプション選択ダイアログ
/// </summary>
public class ErrorRecoveryDialog : Window
{
    private RadioButton? _skipFileRadio;
    private RadioButton? _retryRadio;
    private RadioButton? _stopRadio;
    private RadioButton? _skipAllRadio;
    private CheckBox? _rememberChoiceCheck;
    /// <summary>
    /// 選択された回復オプション
    /// </summary>
    public PartialExtractionHandler.ErrorHandlingOption SelectedOption { get; private set; }

    /// <summary>
    /// 選択を記憶するかどうか
    /// </summary>
    public bool RememberChoice { get; private set; }

    /// <summary>
    /// エラー情報
    /// </summary>
    public ArchiveErrorInfo ErrorInfo { get; }

    /// <summary>エラーメッセージ（バインディング用）</summary>
    public string ErrorMessage { get; set; } = string.Empty;
    /// <summary>エラー詳細（バインディング用）</summary>
    public string ErrorDetails { get; set; } = string.Empty;
    /// <summary>推奨アクション（バインディング用）</summary>
    public string RecommendedAction { get; set; } = string.Empty;
    /// <summary>問題のファイルパス（バインディング用）</summary>
    public string ProblematicFilePath { get; set; } = string.Empty;
    /// <summary>ファイル詳細（バインディング用）</summary>
    public string FileDetails { get; set; } = string.Empty;

    /// <summary>
    /// パラメータなしコンストラクタ（デザイナー・XAML プレビュー用。実行時は ErrorRecoveryDialog(ArchiveErrorInfo) を推奨）
    /// </summary>
    public ErrorRecoveryDialog() : this(new ArchiveErrorInfo { ErrorType = ArchiveErrorType.Unknown, Message = "N/A", Details = "N/A", IsRecoverable = false }) { }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="errorInfo">エラー情報</param>
    public ErrorRecoveryDialog(ArchiveErrorInfo errorInfo)
    {
        ErrorInfo = errorInfo;
        InitializeComponent();
        SetErrorInfo();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _skipFileRadio = this.FindControl<RadioButton>("SkipFileRadio");
        _retryRadio = this.FindControl<RadioButton>("RetryRadio");
        _stopRadio = this.FindControl<RadioButton>("StopRadio");
        _skipAllRadio = this.FindControl<RadioButton>("SkipAllRadio");
        _rememberChoiceCheck = this.FindControl<CheckBox>("RememberChoiceCheck");
    }

    /// <summary>
    /// エラー情報を設定
    /// </summary>
    private void SetErrorInfo()
    {
        // ファイル詳細情報を生成
        var fileDetails = GenerateFileDetails();

        // データバインディング用のプロパティを設定
        ErrorMessage = ErrorInfo.Message;
        ErrorDetails = ErrorInfo.Details;
        RecommendedAction = ErrorInfo.RecommendedAction;
        ProblematicFilePath = ErrorInfo.ProblematicFilePath ?? "";
        FileDetails = fileDetails;

        // DataContextを設定してバインディングを有効化
        DataContext = this;
    }

    /// <summary>
    /// ファイル詳細情報を生成
    /// </summary>
    private string GenerateFileDetails()
    {
        var details = new StringBuilder();

        if (!string.IsNullOrEmpty(ErrorInfo.ProblematicFilePath))
        {
            try
            {
                var fileInfo = new FileInfo(ErrorInfo.ProblematicFilePath);
                details.AppendLine($"ファイル名: {fileInfo.Name}");
                details.AppendLine($"サイズ: {fileInfo.Length:N0} バイト");
                details.AppendLine($"作成日時: {fileInfo.CreationTime}");
                details.AppendLine($"更新日時: {fileInfo.LastWriteTime}");
                details.AppendLine($"属性: {fileInfo.Attributes}");

                // ディスク容量情報
                var drive = new DriveInfo(Path.GetPathRoot(fileInfo.FullName) ?? "");
                details.AppendLine("\nディスク情報:");
                details.AppendLine($"利用可能容量: {drive.AvailableFreeSpace:N0} バイト");
                details.AppendLine($"総容量: {drive.TotalSize:N0} バイト");
            }
            catch (Exception ex)
            {
                details.AppendLine($"ファイル情報の取得に失敗: {ex.Message}");
            }
        }

        if (ErrorInfo.OriginalException != null)
        {
            details.AppendLine("\n例外詳細:");
            details.AppendLine($"種類: {ErrorInfo.OriginalException.GetType().Name}");
            details.AppendLine($"メッセージ: {ErrorInfo.OriginalException.Message}");
            if (ErrorInfo.OriginalException.InnerException != null)
            {
                details.AppendLine($"内部例外: {ErrorInfo.OriginalException.InnerException.Message}");
            }
        }

        return details.ToString();
    }

    /// <summary>
    /// OKボタンクリック
    /// </summary>
    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        // 選択されたオプションを決定
        if (_skipFileRadio?.IsChecked == true)
        {
            SelectedOption = PartialExtractionHandler.ErrorHandlingOption.SkipOnError;
        }
        else if (_retryRadio?.IsChecked == true)
        {
            SelectedOption = PartialExtractionHandler.ErrorHandlingOption.AutoRetry;
        }
        else if (_stopRadio?.IsChecked == true)
        {
            SelectedOption = PartialExtractionHandler.ErrorHandlingOption.StopOnError;
        }
        else if (_skipAllRadio?.IsChecked == true)
        {
            SelectedOption = PartialExtractionHandler.ErrorHandlingOption.SkipOnError;
        }

        RememberChoice = _rememberChoiceCheck?.IsChecked == true;

        Close(SelectedOption);
    }

    /// <summary>
    /// キャンセルボタンクリック
    /// </summary>
    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        SelectedOption = PartialExtractionHandler.ErrorHandlingOption.StopOnError;
        Close(null);
    }

}
