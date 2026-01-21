using System.Windows;
using System.Windows.Threading;
using Lhamiel.Util;

namespace Lhamiel.View;

/// <summary>
/// ProgressWindow.xaml の相互作用ロジック
/// </summary>
public partial class ProgressWindow : Window
{
    /// <summary>
    /// キャンセルが要求されたときのイベント
    /// </summary>
    public event EventHandler? CancelRequested;

    /// <summary>
    /// キャンセルトークンソース（キャンセル処理に使用）
    /// </summary>
    private CancellationTokenSource? _cancellationTokenSource;

    public ProgressWindow(string operationType)
    {
        // コンポーネントの初期化
        InitializeComponent();
        
        // 操作タイプに応じたタイトルを設定
        Title = $"{operationType} - Lhamiel";
        
        // キャンセル処理用のトークンソースを初期化
        _cancellationTokenSource = new CancellationTokenSource();

        // 処理開始をアプリケーション全体に通知（更新待機などの同期に使用）
        App.NotifyProgressStarted();

        // 所有者が設定されている場合はその中央に、そうでなければ画面中央に表示
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    /// <summary>
    /// キャンセルトークンを取得する
    /// </summary>
    /// <returns>キャンセルトークン</returns>
    public CancellationToken GetCancellationToken()
    {
        return _cancellationTokenSource?.Token ?? CancellationToken.None;
    }

    /// <summary>
    /// 進捗を更新する
    /// </summary>
    /// <param name="percentage">進捗率（0-100）</param>
    public void UpdateProgress(int percentage)
    {
        try
        {
            if (!IsLoaded || Dispatcher.HasShutdownStarted)
                return;

            Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                try
                {
                    if (!IsLoaded) return;

                    ProgressBar.Value = percentage;
                    ProgressTextBlock.Text = $"{percentage}%";
                }
                catch (Exception ex)
                {
                    Logger.Log($"進捗更新時のエラー: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"進捗更新処理のエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 完了状態を設定する
    /// </summary>
    /// <param name="message">完了メッセージ</param>
    public void SetCompleted(string message)
    {
        try
        {
            if (!IsLoaded || Dispatcher.HasShutdownStarted)
                return;

            Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                try
                {
                    if (!IsLoaded) return;
                    ProgressBar.Value = 100;
                    ProgressTextBlock.Text = "100%";
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"完了状態設定時のエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// ウィンドウを安全に閉じます。
    /// すでに閉じている場合や、閉じようとしている場合は何もしません。
    /// </summary>
    public void CloseSafe()
    {
        try
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                try
                {
                    if (IsLoaded && !Dispatcher.HasShutdownStarted)
                    {
                        Close();
                    }
                }
                catch (InvalidOperationException ex)
                {
                    Logger.Log($"ウィンドウクローズ時のエラー: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"ウィンドウクローズ処理のエラー: {ex.Message}");
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        try
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
            }
        }
        catch (ObjectDisposedException) { }
        
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// ウィンドウが閉じられる時の処理
    /// </summary>
    /// <param name="e">イベント引数</param>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // まだキャンセルされていない場合は、ウィンドウを閉じることをキャンセル指示とみなす
        try
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
                CancelRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Log($"OnClosing内のCTS例外: {ex.Message}");
        }
        
        base.OnClosing(e);
    }

    /// <summary>
    /// リソースをクリーンアップする
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        // 基本クラスの処理を実行
        base.OnClosed(e);

        // 処理終了をアプリケーション全体に通知（処理待ちなどの同期に使用）
        App.NotifyProgressFinished();
        
        // バックグラウンド処理が完了しているので、CTSを安全に破棄する
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }
}
