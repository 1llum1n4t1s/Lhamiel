using System.Windows;
using System.Windows.Threading;

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

    /// <summary>
    /// 最後のプログレス更新時刻
    /// </summary>
    private DateTime _lastProgressUpdate = DateTime.MinValue;

    /// <summary>
    /// プログレス更新の最小間隔（ミリ秒）
    /// </summary>
    private const int ProgressUpdateIntervalMs = 50;

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
    /// 進捗を更新する（スロットリング付き）
    /// </summary>
    /// <param name="percentage">進捗率（0-100）</param>
    public void UpdateProgress(int percentage)
    {
        try
        {
            // すでに閉じているか閉じようとしている場合は無視
            if (!IsLoaded || Dispatcher.HasShutdownStarted)
                return;

            var now = DateTime.Now;
            
            // 重要な進捗（90%、100%）は必ず更新
            var isImportantUpdate = percentage >= 90;
            
            // スロットリング: 最小間隔より短い場合はスキップ（重要な更新は除く）
            if (!isImportantUpdate && (now - _lastProgressUpdate).TotalMilliseconds < ProgressUpdateIntervalMs)
                return;

            _lastProgressUpdate = now;

            // 非同期でUIを更新
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                try
                {
                    // ラムダ式実行時にまだウィンドウが生きているか再確認
                    if (!IsLoaded) return;

                    ProgressBar.Value = percentage;
                    ProgressTextBlock.Text = $"{percentage}%";
                }
                catch
                {
                    // 実行中のエラー（ウィンドウが閉じられた等）は無視
                }
            });
        }
        catch (Exception)
        {
            // ウィンドウの状態チェック中のエラーなどは無視
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
        catch (Exception)
        {
            // ウィンドウがクローズされた場合は無視
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
                catch (InvalidOperationException)
                {
                    // すでに閉じている場合などの例外を無視
                }
            });
        }
        catch (Exception)
        {
            // ディスパッチャー自体が終了している場合などの例外を無視
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
        catch (ObjectDisposedException)
        {
            // CTSが既に破棄されている場合は無視
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
