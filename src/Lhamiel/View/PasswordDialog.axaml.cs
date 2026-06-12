using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Lhamiel.Util;
using System.Threading.Tasks;
namespace Lhamiel.View;

/// <summary>
/// パスワードダイアログの動作モード。
/// </summary>
public enum PasswordDialogMode
{
    /// <summary>既存パスワードを入力して既存のアーカイブを展開する（1 つの入力欄）。</summary>
    Extract,

    /// <summary>新規パスワードを設定する（2 つの入力欄で一致確認、空入力拒否）。</summary>
    CompressNew,
}

/// <summary>
/// パスワード入力ダイアログ。Extract と CompressNew の 2 モードを 1 つの実装で扱う。
/// 7z.dll の <see cref="Cube.FileSystem.SevenZip.AsyncPasswordQuery"/> ハンドラとしても、
/// 圧縮側のパスワード設定ダイアログとしても使う。
/// </summary>
/// <remarks>
/// <para>
/// プロパティはコンストラクタ初期化のみで動的変更がない（モードも作成時に固定）ため、
/// <see cref="INotifyPropertyChanged"/> を実装せず compiled binding に任せる。
/// 警告メッセージの表示切替だけ code-behind 直接操作で行う。
/// </para>
/// </remarks>
public partial class PasswordDialog : Window
{
    private TextBox? _passwordBox;
    private TextBox? _confirmBox;
    private TextBlock? _extractMessage;
    private TextBlock? _compressNewMessage;
    private TextBlock? _mismatchWarning;
    private TextBlock? _emptyWarning;
    private TextBlock? _tooShortWarning;

    /// <summary>
    /// 新規圧縮パスワードの最小文字数 (codex P2 #3384761804)。
    /// Logger.RegisterRedactionToken は 4 文字未満の token を登録しない契約
    /// ("on"/"to" 等の部分一致による全ログ過剰マスク防止) のため、それより短い
    /// パスワードを受理するとログ redaction の対象外になる。入力時点で 4 文字以上を
    /// 強制して「マスクされない圧縮パスワード」の存在自体をなくす (1〜3 文字の
    /// アーカイブパスワードは保護強度的にも無意味)。Extract モードは既存書庫との
    /// 互換のため制限しない。Logger.MinRedactionTokenLength を直接参照することで
    /// 連動を構造的に固定する (linkage テストは belt-and-suspenders として維持)。
    /// </summary>
    internal const int MinCompressPasswordLength = Logger.MinRedactionTokenLength;

    /// <summary>アーカイブ名（バインディング用、コンストラクタで決定し以後不変）</summary>
    public string ArchiveName { get; }

    /// <summary>リトライ時（前回のパスワードが間違っていた場合）に true（バインディング用、以後不変）</summary>
    public bool IsRetry { get; }

    /// <summary>ダイアログモード（Extract: 1 入力欄 / CompressNew: 2 入力欄 + 一致確認）。コンストラクタで決定し以後不変。</summary>
    public PasswordDialogMode Mode { get; }

    /// <summary>
    /// 入力されたパスワード。キャンセル時は null。
    /// <see cref="ShowFromBackgroundAsync(string, bool, Window?, CancellationToken)"/> で呼び出し側が取得した直後に
    /// <see cref="ClearPassword"/> でクリアされ、ダイアログオブジェクトが GC されるまで平文文字列参照を保持し続けない設計。
    /// </summary>
    /// <remarks>
    /// .NET 8+ の <see cref="System.Security.SecureString"/> は実装上保護が弱いため公式に非推奨。
    /// 代わりに「ダイアログ寿命を最小化 + 取得直後にダイアログ側参照をクリア」で平文露出時間を絞る方針。
    /// 7z.dll の callback は <see cref="string"/> を要求するため、最終境界での平文化は避けられない。
    /// </remarks>
    public string? Password { get; private set; }

    /// <summary>
    /// ダイアログ内の Password 参照と入力欄の Text を null/空 化する。
    /// 呼び出し側がパスワードを受け取った直後に呼ぶ。
    /// </summary>
    public void ClearPassword()
    {
        Password = null;
        if (_passwordBox != null) _passwordBox.Text = string.Empty;
        if (_confirmBox != null) _confirmBox.Text = string.Empty;
    }

    /// <summary>XAML プレビュー用のパラメータなしコンストラクタ。</summary>
    public PasswordDialog() : this(string.Empty, false, PasswordDialogMode.Extract) { }

    /// <summary>既存呼出と source-compatible な Extract 専用コンストラクタ。</summary>
    public PasswordDialog(string archiveName, bool isRetry) : this(archiveName, isRetry, PasswordDialogMode.Extract) { }

    /// <summary>モード指定可能なコンストラクタ。</summary>
    public PasswordDialog(string archiveName, bool isRetry, PasswordDialogMode mode)
    {
        ArchiveName = archiveName;
        IsRetry = isRetry;
        Mode = mode;
        InitializeComponent();
        Util.AcrylicFallbackHelper.Attach(this);

        Opened += (_, _) => _passwordBox?.Focus();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _passwordBox = this.FindControl<TextBox>("PasswordBox");
        _confirmBox = this.FindControl<TextBox>("ConfirmBox");
        _extractMessage = this.FindControl<TextBlock>("ExtractMessage");
        _compressNewMessage = this.FindControl<TextBlock>("CompressNewMessage");
        _mismatchWarning = this.FindControl<TextBlock>("MismatchWarning");
        _emptyWarning = this.FindControl<TextBlock>("EmptyWarning");
        _tooShortWarning = this.FindControl<TextBlock>("TooShortWarning");

        // モードに応じて要素の表示切替（locale 動的切替は短寿命モーダルなので追従不要）。
        var isCompressNew = Mode == PasswordDialogMode.CompressNew;
        if (_extractMessage != null) _extractMessage.IsVisible = !isCompressNew;
        if (_compressNewMessage != null) _compressNewMessage.IsVisible = isCompressNew;
        if (_confirmBox != null) _confirmBox.IsVisible = isCompressNew;

        // ウィンドウタイトルもモードで切り替え。DynamicResource を XAML で動的差し替えするより
        // App.Text() の現在値を 1 回読むほうが簡素（短寿命モーダルで locale 切替に追従不要のため）。
        // App.Text() は "Text." prefix を自動付加するので、引数からは prefix を抜く (CodeRabbit #3381138460)。
        Title = App.Text(isCompressNew ? "Password.SetTitle" : "Password.Title");

        DataContext = this;
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        var pwd = _passwordBox?.Text ?? string.Empty;

        if (Mode == PasswordDialogMode.CompressNew)
        {
            // 空入力拒否（CompressNew モードのみ。Extract はライブラリ仕様により空文字列も valid な「パスワードなし」入力）。
            if (string.IsNullOrEmpty(pwd))
            {
                ShowSingleWarning(_emptyWarning);
                _passwordBox?.Focus();
                return;
            }

            // 最小文字数 (理由は MinCompressPasswordLength の doc コメント参照)。
            if (pwd.Length < MinCompressPasswordLength)
            {
                ShowSingleWarning(_tooShortWarning);
                _passwordBox?.Focus();
                return;
            }

            // 一致確認。primary は保持して confirm 側だけクリア（ユーザーの再入力負担を減らす）。
            var confirm = _confirmBox?.Text ?? string.Empty;
            if (!string.Equals(pwd, confirm, System.StringComparison.Ordinal))
            {
                ShowSingleWarning(_mismatchWarning);
                if (_confirmBox != null)
                {
                    _confirmBox.Text = string.Empty;
                    _confirmBox.Focus();
                }
                return;
            }
        }

        Password = pwd;
        // 入力欄の内部参照を切り、ダイアログ閉じた後にコントロール側に平文が残り続けるのを防ぐ。
        if (_passwordBox != null) _passwordBox.Text = string.Empty;
        if (_confirmBox != null) _confirmBox.Text = string.Empty;
        Close(true);
    }

    /// <summary>検証警告 3 種のうち指定したものだけを表示する（他は非表示に揃える）。</summary>
    private void ShowSingleWarning(TextBlock? target)
    {
        if (_emptyWarning != null) _emptyWarning.IsVisible = ReferenceEquals(_emptyWarning, target);
        if (_tooShortWarning != null) _tooShortWarning.IsVisible = ReferenceEquals(_tooShortWarning, target);
        if (_mismatchWarning != null) _mismatchWarning.IsVisible = ReferenceEquals(_mismatchWarning, target);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Password = null;
        if (_passwordBox != null) _passwordBox.Text = string.Empty;
        if (_confirmBox != null) _confirmBox.Text = string.Empty;
        Close(false);
    }

    private void PasswordBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // CompressNew では Enter で ConfirmBox にフォーカスを移し、ConfirmBox の Enter で submit する。
            if (Mode == PasswordDialogMode.CompressNew && _confirmBox is { IsVisible: true })
            {
                _confirmBox.Focus();
                e.Handled = true;
                return;
            }
            OkButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private void ConfirmBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OkButton_Click(sender, e);
            e.Handled = true;
        }
    }

    // ── Static helpers ──

    /// <summary>
    /// バックグラウンドスレッドからパスワード入力ダイアログを表示する（Extract モード）。
    /// 既存呼出と source-compatible な extract 専用 overload。
    /// </summary>
    public static Task<string?> ShowFromBackgroundAsync(
        string archiveName,
        bool isRetry,
        Window? parentWindow,
        CancellationToken cancellationToken = default)
        => ShowFromBackgroundAsync(archiveName, isRetry, PasswordDialogMode.Extract, parentWindow, cancellationToken);

    /// <summary>
    /// バックグラウンドスレッドからパスワード入力ダイアログを表示する（モード指定可能）。
    /// 7-Zip はパスワード誤入力時に同じハンドラを複数回呼ぶため、呼ばれるたびに
    /// 新しいダイアログを開く設計にしている。
    /// </summary>
    /// <param name="archiveName">ユーザー表示用のアーカイブ名。</param>
    /// <param name="isRetry">直前の入力が間違っていて再試行する場合は true（Extract モードでのみ意味がある）。</param>
    /// <param name="mode">ダイアログモード（Extract or CompressNew）。</param>
    /// <param name="parentWindow">親ウィンドウ（null なら親なしで開く）。</param>
    /// <param name="cancellationToken">展開処理のキャンセル要求と連動。発火時にダイアログを自動クローズしてユーザーがダイアログ前で操作不能にならないようにする。</param>
    /// <returns>入力されたパスワード。キャンセル時は null。</returns>
    public static async Task<string?> ShowFromBackgroundAsync(
        string archiveName,
        bool isRetry,
        PasswordDialogMode mode,
        Window? parentWindow,
        CancellationToken cancellationToken = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // 早期キャンセル: ShowDialog 開始前に既に CT がキャンセル済みの場合、
            // Register コールバックが「dialog.IsVisible = false（まだ表示前）」のため Close をスキップ
            // → ShowDialog でダイアログが表示 → 永続的に閉じられず UI フリーズ、
            // という Race パスを塞ぐため、最初に IsCancellationRequested を見て即抜ける。
            if (cancellationToken.IsCancellationRequested) return null;

            var dialog = new PasswordDialog(archiveName, isRetry, mode);

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
            // アーカイブ名・再試行回数・モード・試行タイミングのいずれもログファイルに残さない。
            Logger.Log($"パスワード入力ダイアログ完了: mode={mode} retry={isRetry}", LogLevel.Debug);
            // ダイアログオブジェクトが GC されるまでパスワード参照を保持し続けないよう、
            // 取得直後に dialog 側参照をクリアする（呼び出し側の戻り値文字列とは別参照）。
            var result = dialog.Password;
            dialog.ClearPassword();
            return result;
        });
    }
}
