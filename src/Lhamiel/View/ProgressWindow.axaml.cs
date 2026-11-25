using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
    /// パラメータなしコンストラクタ（デザイナー・XAML プレビュー用。実行時は ProgressWindow(string) を推奨）
    /// </summary>
    public ProgressWindow() : this(App.Text("Progress.Processing")) { }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="operationType">操作タイプ（タイトル表示用）</param>
    /// <param name="appIconVariant">表示するアプリアイコン。null の場合は保存済み設定から解決する。</param>
    public ProgressWindow(string operationType, string? appIconVariant = null)
    {
        // コンポーネントの初期化
        InitializeComponent();
        // アクリル代替背景 → 共通枠 → アイコンの順で初期化する（順序は DialogChrome.InitializeDialog が固定）。
        DialogChrome.InitializeDialog(this, actionsName: "CancelButton", appIconVariant: appIconVariant);

        // 操作タイプに応じたタイトルとラベルを設定
        Title = $"{operationType} - Lhamiel";
        OperationLabel.Text = operationType;
        // 進行状況バーの読み上げ名は操作種別（展開/圧縮など）に合わせる。
        AutomationProperties.SetName(ProgressBar, operationType);

        // キャンセル処理用のトークンソースを初期化
        _cancellationTokenSource = new CancellationTokenSource();
    }

    /// <summary>受付時の対象と取消単位を、進捗更新から独立して表示する。</summary>
    public void SetOperationTarget(IReadOnlyList<string> paths, bool cancelAll = false)
    {
        var targets = paths.Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))).ToArray();
        if (targets.Length == 0) return;
        var displayName = Path.GetFileName(targets[0]);
        if (string.IsNullOrEmpty(displayName)) displayName = targets[0];
        var summary = targets.Length == 1 ? displayName
            : $"{App.Text("Progress.TargetCount", targets.Length)}: {displayName}";
        var parent = Path.GetDirectoryName(targets[0]);
        var identity = string.IsNullOrEmpty(parent) ? summary : $"{summary} — {parent}";
        Title = $"{OperationLabel.Text} — {identity} - Lhamiel";
        AutomationProperties.SetName(this, Title + "\n" + string.Join("\n", targets));
        TargetTextBlock.Text = string.Join("\n", targets);
        TargetTextBlock.IsVisible = true;
        ToolTip.SetTip(TargetTextBlock, TargetTextBlock.Text);
        AutomationProperties.SetName(TargetTextBlock, TargetTextBlock.Text);

        var label = App.Text(cancelAll ? "Progress.CancelAll" : "Button.Cancel");
        CancelButton.Content = label;
        AutomationProperties.SetName(CancelButton, $"{label}: {identity}");
        ToolTip.SetTip(CancelButton, string.Join("\n", targets));
    }

    /// <summary>
    /// 注意書きテキストを表示する
    /// </summary>
    public void SetNotice(string text)
    {
        if (!IsInitialized) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsInitialized) return;
            NoticeTextBlock.Text = text;
            NoticeTextBlock.IsVisible = !string.IsNullOrEmpty(text);
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
    /// <remarks>
    /// <see cref="OnClosed"/> で <c>Dispose()</c> → <c>= null</c> の順で解放する微小な窓で、
    /// 他スレッドが <c>cts.Token</c> を触ると <see cref="ObjectDisposedException"/> が出る可能性があるため、
    /// ここでも try/catch で吸収して「キャンセル済みトークン」にフォールバックする。
    /// </remarks>
    /// <returns>キャンセルトークン</returns>
    public CancellationToken GetCancellationToken()
    {
        var cts = _cancellationTokenSource;
        if (cts is null)
            return new CancellationToken(canceled: true);
        try
        {
            return cts.Token;
        }
        catch (ObjectDisposedException)
        {
            return new CancellationToken(canceled: true);
        }
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
                if (ProgressBar.IsIndeterminate) ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = percentage;
                ProgressTextBlock.Text = $"{percentage}%";
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
                ProgressBar.IsIndeterminate = true;
                ProgressTextBlock.Text = message;
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
                ProgressBar.Value = 100;
                ProgressTextBlock.Text = string.IsNullOrEmpty(message) ? "100%" : message;
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
    public void CloseSafe() => Dispatcher.UIThread.Post(CloseSafelyOnUiThread);

    /// <summary>
    /// ウィンドウが閉じるまで待機できる形で安全に閉じます。
    /// 後続のダイアログがこのウィンドウを所有者として選ばないようにする場合に使用します。
    /// </summary>
    public async Task CloseSafeAsync()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            CloseSafelyOnUiThread();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(CloseSafelyOnUiThread);
    }

    private void CloseSafelyOnUiThread()
    {
        try
        {
            if (IsInitialized) Close();
        }
        catch (InvalidOperationException ex)
        {
            Logger.Log($"ウィンドウクローズ時のエラー: {ex.Message}");
        }
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
        CancelButton.IsEnabled = false;
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

        // バックグラウンド処理が完了しているので、CTS を安全に破棄する。
        // 他スレッドの GetCancellationToken() が「Dispose 後・null 代入前」の隙間で
        // 解放済みトークンに触れないよう、先にフィールドを null に差し替えてから Dispose する。
        var cts = _cancellationTokenSource;
        _cancellationTokenSource = null;
        cts?.Dispose();
    }
}
