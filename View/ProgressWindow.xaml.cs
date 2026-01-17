using System.IO;
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

    /// <summary>
    /// 処理中の出力ファイルパスリスト
    /// </summary>
    private readonly List<string> _processingFiles = new();

    /// <summary>
    /// 最新のファイル名（UI 更新用）
    /// </summary>
    private string? _pendingFileName;

    /// <summary>
    /// ファイル名 UI 更新用の lock オブジェクト
    /// </summary>
    private readonly object _fileNameUpdateLock = new object();

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
        InitializeComponent();
        Title = $"{operationType} - Lhamiel";
        _cancellationTokenSource = new CancellationTokenSource();
    }

    /// <summary>
    /// キャンセルトークンを取得する
    /// </summary>
    /// <returns>キャンセルトークン</returns>
    public CancellationToken GetCancellationToken()
    {
        if (_cancellationTokenSource == null || _cancellationTokenSource.IsCancellationRequested)
        {
            return CancellationToken.None;
        }

        return _cancellationTokenSource.Token;
    }

    /// <summary>
    /// ファイル名を設定する
    /// </summary>
    /// <param name="fileName">ファイル名（パスまたはファイル名）</param>
    public void SetFileName(string fileName)
    {
        try
        {
            // パス区切り文字が含まれている場合のみPath.GetFileNameを呼ぶ
            var shortName = fileName.Contains(Path.DirectorySeparatorChar) || fileName.Contains(Path.AltDirectorySeparatorChar)
                ? Path.GetFileName(fileName)
                : fileName;
            
            Logger.Log($"ProgressWindow.SetFileName 呼び出し: {shortName}");

            lock (_fileNameUpdateLock)
            {
                _pendingFileName = shortName;
            }

            // UI 更新を実行（最新の値のみが表示される）
            Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
            {
                lock (_fileNameUpdateLock)
                {
                    if (_pendingFileName != null)
                    {
                        Logger.Log($"ProgressWindow.FileNameTextBlock 更新: {_pendingFileName}");
                        FileNameTextBlock.Text = _pendingFileName;
                        _pendingFileName = null;
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"ProgressWindow.SetFileName エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 処理中のファイルを記録する
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    public void AddProcessingFile(string filePath)
    {
        try
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                if (!_processingFiles.Contains(filePath))
                {
                    _processingFiles.Add(filePath);
                }
            });
        }
        catch (Exception)
        {
            // ウィンドウがクローズされた場合は無視
        }
    }

    /// <summary>
    /// 処理中のファイルを取得する
    /// </summary>
    /// <returns>処理中のファイルパスのリスト</returns>
    public List<string> GetProcessingFiles()
    {
        lock (_processingFiles)
        {
            return new List<string>(_processingFiles);
        }
    }

    /// <summary>
    /// 進捗を更新する（スロットリング付き）
    /// </summary>
    /// <param name="percentage">進捗率（0-100）</param>
    /// <param name="status">ステータスメッセージ</param>
    public void UpdateProgress(int percentage, string status)
    {
        try
        {
            var now = DateTime.Now;
            
            // 重要な進捗（90%、100%）は必ず更新
            var isImportantUpdate = percentage >= 90;
            
            // スロットリング: 最小間隔より短い場合はスキップ（重要な更新は除く）
            if (!isImportantUpdate && (now - _lastProgressUpdate).TotalMilliseconds < ProgressUpdateIntervalMs)
                return;

            _lastProgressUpdate = now;

            Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                ProgressBar.Value = percentage;
                StatusTextBlock.Text = status;
                ProgressTextBlock.Text = $"{percentage}%";
            });
        }
        catch (Exception)
        {
            // ウィンドウがクローズされた場合は無視
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
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Value = 100;
                StatusTextBlock.Text = message;
                ProgressTextBlock.Text = "100%";
                Topmost = false;
            });
        }
        catch (Exception)
        {
            // ウィンドウがクローズされた場合は無視
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        StatusTextBlock.Text = "キャンセル中...";
        _cancellationTokenSource?.Cancel();
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// ウィンドウをクローズする際、保留中のDispatcher作業を完了させてからクローズする
    /// </summary>
    /// <remarks>
    /// Dispatcher.BeginInvoke で登録されたアクションが実行中にウィンドウがクローズされると、
    /// ExecutionEngineException が発生する可能性があるため、ウィンドウをクローズする前に
    /// Dispatcher の作業完了を待機する必要がある。
    /// </remarks>
    public new void Close()
    {
        try
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                // 保留中のDispatcher作業を完了させる（バックグラウンド優先度）
                try
                {
                    Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                }
                catch
                {
                    // Invokeに失敗した場合（既にシャットダウン中など）は無視
                }

                base.Close();
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"ProgressWindow.Close() でエラー: {ex.Message}", LogLevel.Warning);
        }
    }

    /// <summary>
    /// リソースをクリーンアップする
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }
}
