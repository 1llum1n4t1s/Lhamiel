using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Lhamiel.Util;
namespace Lhamiel.View;

/// <summary>
/// ProgressWindow.xaml の相互作用ロジック
/// </summary>
public partial class ProgressWindow : Window
{
    private TextBlock? _operationLabel;
    private ProgressBar? _progressBar;
    private TextBlock? _progressTextBlock;
    private TextBlock? _noticeTextBlock;
    private Button? _cancelButton;

    /// <summary>
    /// キャンセルが要求されたときのイベント
    /// </summary>
    public event EventHandler? CancelRequested;

    /// <summary>
    /// キャンセルトークンソース（キャンセル処理に使用）
    /// </summary>
    private CancellationTokenSource? _cancellationTokenSource;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _operationLabel = this.FindControl<TextBlock>("OperationLabel");
        _progressBar = this.FindControl<ProgressBar>("ProgressBar");
        _progressTextBlock = this.FindControl<TextBlock>("ProgressTextBlock");
        _noticeTextBlock = this.FindControl<TextBlock>("NoticeTextBlock");
        _cancelButton = this.FindControl<Button>("CancelButton");
    }

    /// <summary>
    /// パラメータなしコンストラクタ（デザイナー・XAML プレビュー用。実行時は ProgressWindow(string) を推奨）
    /// </summary>
    public ProgressWindow() : this(App.Text("Progress.Processing")) { }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="operationType">操作タイプ（タイトル表示用）</param>
    public ProgressWindow(string operationType)
    {
        // コンポーネントの初期化
        InitializeComponent();

        // 操作タイプに応じたタイトルとラベルを設定
        Title = $"{operationType} - Lhamiel";
        if (_operationLabel != null) _operationLabel.Text = operationType;

        // キャンセル処理用のトークンソースを初期化
        _cancellationTokenSource = new CancellationTokenSource();
    }

    /// <summary>
    /// 注意書きテキストを表示する
    /// </summary>
    public void SetNotice(string text)
    {
        if (!IsInitialized) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsInitialized || _noticeTextBlock is null) return;
            _noticeTextBlock.Text = text;
            _noticeTextBlock.IsVisible = !string.IsNullOrEmpty(text);
        });
    }

    /// <summary>
    /// 注意書きを非表示にする
    /// </summary>
    public void ClearNotice()
    {
        SetNotice("");
    }

    /// <summary>
    /// キャンセルトークンを取得する。
    /// ウィンドウが閉じられて CTS が破棄された後は CancellationToken.None を返すと
    /// 後続処理がキャンセル不能になるため、代わりに「既にキャンセル済み」のトークンを返す。
    /// </summary>
    /// <returns>キャンセルトークン</returns>
    public CancellationToken GetCancellationToken()
    {
        var cts = _cancellationTokenSource;
        if (cts is null)
            return new CancellationToken(canceled: true);
        return cts.Token;
    }

    /// <summary>
    /// 進捗を更新する
    /// </summary>
    /// <param name="percentage">進捗率（0-100）</param>
    public void UpdateProgress(int percentage)
    {
        if (!IsInitialized) return;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (!IsInitialized) return;
                if (_progressBar != null)
                {
                    if (_progressBar.IsIndeterminate) _progressBar.IsIndeterminate = false;
                    _progressBar.Value = percentage;
                }
                if (_progressTextBlock != null) _progressTextBlock.Text = $"{percentage}%";
            }
            catch (Exception ex)
            {
                Logger.Log($"進捗更新時のエラー: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 不確定進捗（マーキー表示）に切り替え、メッセージを表示する
    /// </summary>
    /// <param name="message">表示するメッセージ</param>
    public void SetIndeterminate(string message)
    {
        if (!IsInitialized) return;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (!IsInitialized) return;
                if (_progressBar != null) _progressBar.IsIndeterminate = true;
                if (_progressTextBlock != null) _progressTextBlock.Text = message;
            }
            catch (Exception ex)
            {
                Logger.Log($"不確定進捗更新時のエラー: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// 完了状態を設定する
    /// </summary>
    /// <param name="message">完了メッセージ</param>
    public void SetCompleted(string message)
    {
        if (!IsInitialized) return;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (!IsInitialized) return;
                if (_progressBar != null) _progressBar.Value = 100;
                if (_progressTextBlock != null) _progressTextBlock.Text = string.IsNullOrEmpty(message) ? "100%" : message;
            }
            catch (Exception ex)
            {
                Logger.Log($"完了表示更新時のエラー: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// ウィンドウを安全に閉じます。
    /// すでに閉じている場合や、閉じようとしている場合は何もしません。
    /// </summary>
    public void CloseSafe()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (IsInitialized) Close();
            }
            catch (InvalidOperationException ex)
            {
                Logger.Log($"ウィンドウクローズ時のエラー: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// CancellationTokenSource を安全にキャンセルする。既に破棄済みの場合は無視する。
    /// </summary>
    /// <returns>キャンセルが実行された場合は true</returns>
    private bool TryCancel()
    {
        try
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
                return true;
            }
        }
        catch (ObjectDisposedException) { }
        return false;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_cancelButton != null) _cancelButton.IsEnabled = false;
        TryCancel();
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// ウィンドウが閉じられる時の処理
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // まだキャンセルされていない場合は、ウィンドウを閉じることをキャンセル指示とみなす
        if (TryCancel())
            CancelRequested?.Invoke(this, EventArgs.Empty);

        base.OnClosing(e);
    }

    /// <summary>
    /// リソースをクリーンアップする
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        // 基本クラスの処理を実行
        base.OnClosed(e);

        // バックグラウンド処理が完了しているので、CTSを安全に破棄する
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }
}
