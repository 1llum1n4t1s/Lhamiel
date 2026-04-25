using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Lhamiel.Util;
using System.Threading.Tasks;
namespace Lhamiel.View;

/// <summary>
/// パスワード保護されたアーカイブのパスワード入力ダイアログ。
/// 7z.dll の ICryptoGetTextPassword コールバックから呼ばれる
/// <see cref="Cube.FileSystem.SevenZip.AsyncPasswordQuery"/> のハンドラとして使う。
/// </summary>
/// <remarks>
/// <para>
/// ダイアログは 1 回表示するだけでプロパティが変化しないため、<see cref="INotifyPropertyChanged"/> を
/// 明示実装せずコンストラクタ初期化 → <c>DataContext = this</c> の compiled binding に任せる。
/// Avalonia 12 の <see cref="Window"/> 既定の INPC 機能と二重にイベントを持つ構造を避ける。
/// </para>
/// </remarks>
public partial class PasswordDialog : Window
{
    private TextBox? _passwordBox;

    /// <summary>アーカイブ名（バインディング用、コンストラクタで決定し以後不変）</summary>
    public string ArchiveName { get; }

    /// <summary>リトライ時（前回のパスワードが間違っていた場合）に true（バインディング用、以後不変）</summary>
    public bool IsRetry { get; }

    /// <summary>入力されたパスワード。キャンセル時は null。</summary>
    public string? Password { get; private set; }

    /// <summary>XAML プレビュー用のパラメータなしコンストラクタ。</summary>
    public PasswordDialog() : this(string.Empty, false) { }

    public PasswordDialog(string archiveName, bool isRetry)
    {
        ArchiveName = archiveName;
        IsRetry = isRetry;
        InitializeComponent();

        Opened += (_, _) => _passwordBox?.Focus();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _passwordBox = this.FindControl<TextBox>("PasswordBox");
        DataContext = this;
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        Password = _passwordBox?.Text ?? string.Empty;
        // TextBox の内部参照を切り、ダイアログ閉じた後にコントロール側に平文が残り続けるのを防ぐ。
        if (_passwordBox != null)
            _passwordBox.Text = string.Empty;
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Password = null;
        if (_passwordBox != null)
            _passwordBox.Text = string.Empty;
        Close(false);
    }

    private void PasswordBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OkButton_Click(sender, e);
            e.Handled = true;
        }
    }

    // ── Static helper ──

    /// <summary>
    /// バックグラウンドスレッドからパスワード入力ダイアログを表示する。
    /// 7-Zip はパスワード誤入力時に同じハンドラを複数回呼ぶため、呼ばれるたびに
    /// 新しいダイアログを開く設計にしている。
    /// </summary>
    /// <param name="archiveName">ユーザー表示用のアーカイブ名。</param>
    /// <param name="isRetry">直前の入力が間違っていて再試行する場合は true。</param>
    /// <param name="parentWindow">親ウィンドウ（null なら親なしで開く）。</param>
    /// <param name="cancellationToken">展開処理のキャンセル要求と連動。発火時にダイアログを自動クローズしてユーザーがダイアログ前で操作不能にならないようにする。</param>
    /// <returns>入力されたパスワード。キャンセル時は null（AsyncPasswordQuery 側でキャンセル扱いになる）。</returns>
    public static async Task<string?> ShowFromBackgroundAsync(string archiveName, bool isRetry, Window? parentWindow, CancellationToken cancellationToken = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // 早期キャンセル: ShowDialog 開始前に既に CT がキャンセル済みの場合、
            // Register コールバックが「dialog.IsVisible = false（まだ表示前）」のため Close をスキップ
            // → ShowDialog でダイアログが表示 → 永続的に閉じられず UI フリーズ、
            // という Race パスを塞ぐため、最初に IsCancellationRequested を見て即抜ける。
            if (cancellationToken.IsCancellationRequested) return null;

            var dialog = new PasswordDialog(archiveName, isRetry);

            // 展開キャンセル（DiskSpaceChecker / ユーザー Stop / タイムアウト等）が発生した場合に
            // パスワード入力ダイアログが画面に残り続けないよう、CT 発火で UI スレッドへ Post して Close する。
            using var ctReg = cancellationToken.Register(() =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (dialog.IsVisible) dialog.Close(false);
                });
            });

            // Register 直後にもう一度確認: Register が同期コールバックを発火する仕様により、
            // 上の Register 中に CT が発火した場合の Close Post は走るが、
            // dialog がまだ表示前なら IsVisible=false で空振りする。
            // ShowDialog を呼ぶ前にここで再度ガード。
            if (cancellationToken.IsCancellationRequested) return null;

            bool ok;
            if (parentWindow != null)
            {
                ok = await dialog.ShowDialog<bool>(parentWindow);
            }
            else
            {
                var tcs = new TaskCompletionSource<bool>();
                dialog.Closed += (_, _) => tcs.TrySetResult(dialog.Password != null);
                dialog.Show();
                ok = await tcs.Task;
            }
            if (!ok) return null;
            // パスワード関連のログは Debug レベルに固定し、リリースビルドでは
            // アーカイブ名・再試行回数・試行タイミングのいずれもログファイルに残さない。
            Logger.Log($"パスワード入力ダイアログ完了: retry={isRetry}", LogLevel.Debug);
            return dialog.Password;
        });
    }
}
