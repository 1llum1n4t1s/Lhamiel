using System.IO;
using System.Windows;
using Lhamiel.Util;

namespace Lhamiel.View;

/// <summary>
/// エラー回復オプション選択ダイアログ
/// </summary>
public partial class ErrorRecoveryDialog : Window
{
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

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="errorInfo">エラー情報</param>
    public ErrorRecoveryDialog(ArchiveErrorInfo errorInfo)
    {
        InitializeComponent();
        ErrorInfo = errorInfo;
        DataContext = this;
        
        // エラー情報を設定
        SetErrorInfo();
    }

    /// <summary>
    /// エラー情報を設定
    /// </summary>
    private void SetErrorInfo()
    {
        // ファイル詳細情報を生成
        var fileDetails = GenerateFileDetails();
        
        // データバインディング用のプロパティを設定
        SetValue(ErrorMessageProperty, ErrorInfo.Message);
        SetValue(ErrorDetailsProperty, ErrorInfo.Details);
        SetValue(RecommendedActionProperty, ErrorInfo.RecommendedAction);
        SetValue(ProblematicFilePathProperty, ErrorInfo.ProblematicFilePath ?? "");
        SetValue(FileDetailsProperty, fileDetails);
    }

    /// <summary>
    /// ファイル詳細情報を生成
    /// </summary>
    private string GenerateFileDetails()
    {
        var details = new System.Text.StringBuilder();
        
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
                details.AppendLine($"\nディスク情報:");
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
            details.AppendLine($"\n例外詳細:");
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
    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        // 選択されたオプションを決定
        if (SkipFileRadio.IsChecked == true)
        {
            SelectedOption = PartialExtractionHandler.ErrorHandlingOption.SkipOnError;
        }
        else if (RetryRadio.IsChecked == true)
        {
            SelectedOption = PartialExtractionHandler.ErrorHandlingOption.AutoRetry;
        }
        else if (StopRadio.IsChecked == true)
        {
            SelectedOption = PartialExtractionHandler.ErrorHandlingOption.StopOnError;
        }
        else if (SkipAllRadio.IsChecked == true)
        {
            SelectedOption = PartialExtractionHandler.ErrorHandlingOption.SkipOnError;
        }
        
        RememberChoice = RememberChoiceCheck.IsChecked == true;
        
        DialogResult = true;
        Close();
    }

    /// <summary>
    /// キャンセルボタンクリック
    /// </summary>
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedOption = PartialExtractionHandler.ErrorHandlingOption.StopOnError;
        DialogResult = false;
        Close();
    }

    // データバインディング用の依存プロパティ
    public static readonly DependencyProperty ErrorMessageProperty =
        DependencyProperty.Register("ErrorMessage", typeof(string), typeof(ErrorRecoveryDialog));

    public static readonly DependencyProperty ErrorDetailsProperty =
        DependencyProperty.Register("ErrorDetails", typeof(string), typeof(ErrorRecoveryDialog));

    public static readonly DependencyProperty RecommendedActionProperty =
        DependencyProperty.Register("RecommendedAction", typeof(string), typeof(ErrorRecoveryDialog));

    public static readonly DependencyProperty ProblematicFilePathProperty =
        DependencyProperty.Register("ProblematicFilePath", typeof(string), typeof(ErrorRecoveryDialog));

    public static readonly DependencyProperty FileDetailsProperty =
        DependencyProperty.Register("FileDetails", typeof(string), typeof(ErrorRecoveryDialog));
}
