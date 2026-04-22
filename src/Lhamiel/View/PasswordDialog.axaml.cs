using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Lhamiel.Util;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
namespace Lhamiel.View;

/// <summary>
/// パスワード保護されたアーカイブのパスワード入力ダイアログ。
/// 7z.dll の ICryptoGetTextPassword コールバックから呼ばれる
/// <see cref="Cube.FileSystem.SevenZip.AsyncPasswordQuery"/> のハンドラとして使う。
/// </summary>
public partial class PasswordDialog : Window, INotifyPropertyChanged
{
    private TextBox? _passwordBox;
    private string _archiveName = string.Empty;
    private bool _isRetry;

    /// <summary>アーカイブ名（バインディング用）</summary>
    public string ArchiveName
    {
        get => _archiveName;
        set { _archiveName = value; OnPropertyChanged(); }
    }

    /// <summary>リトライ時（前回のパスワードが間違っていた場合）に true（バインディング用）</summary>
    public bool IsRetry
    {
        get => _isRetry;
        set { _isRetry = value; OnPropertyChanged(); }
    }

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
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Password = null;
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

    public new event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ── Static helper ──

    /// <summary>
    /// バックグラウンドスレッドからパスワード入力ダイアログを表示する。
    /// 7-Zip はパスワード誤入力時に同じハンドラを複数回呼ぶため、呼ばれるたびに
    /// 新しいダイアログを開く設計にしている。
    /// </summary>
    /// <param name="archiveName">ユーザー表示用のアーカイブ名。</param>
    /// <param name="isRetry">直前の入力が間違っていて再試行する場合は true。</param>
    /// <param name="parentWindow">親ウィンドウ（null なら親なしで開く）。</param>
    /// <returns>入力されたパスワード。キャンセル時は null（AsyncPasswordQuery 側でキャンセル扱いになる）。</returns>
    public static async Task<string?> ShowFromBackgroundAsync(string archiveName, bool isRetry, Window? parentWindow)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new PasswordDialog(archiveName, isRetry);
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
            Logger.Log($"パスワード入力ダイアログ完了: archive={archiveName}, retry={isRetry}");
            return dialog.Password;
        });
    }
}
