using System.ComponentModel;

using VelopackUpdateDialog;

namespace Lhamiel.Models;

/// <summary>
/// VelopackUpdateDialog.Avalonia が要求する文字列セット (<see cref="IUpdateDialogStrings"/>) を、
/// Lhamiel の Locale ResourceDictionary (Text.SelfUpdate.* / Text.Close) 経由で動的に解決する実装。
/// <para>
/// Property getter で毎回 <see cref="App.Text(string)"/> を呼ぶため、
/// ユーザーが設定で言語を切り替えた時、<see cref="NotifyLocaleChanged"/> を呼べば
/// 開いている VelopackUpdateDialog の XAML バインディングも即時に再評価され翻訳が反映される。
/// 呼ばなくても次回ダイアログ表示時には最新の翻訳が反映される（getter ベースの動的解決）。
/// </para>
/// <para>
/// Locale キーは <c>src/Lhamiel/Resources/Locales/en_US.axaml</c> の <c>Text.SelfUpdate.*</c> と <c>Text.Close</c> に対応。
/// プレフィクスは <see cref="KeyPrefix"/> で一箇所に集約してあるため、リファクタ時の影響範囲が狭い。
/// サフィックスのタイポはビルド時に検出されず実行時に英語フォールバック表示になるため、
/// 編集時は Locale ファイルの key と並べて確認すること。
/// </para>
/// </summary>
public sealed class LhamielUpdateStrings : IUpdateDialogStrings, INotifyPropertyChanged
{
    /// <summary>SelfUpdate 系 Locale キーの共通プレフィクス。</summary>
    private const string KeyPrefix = "SelfUpdate.";

    /// <summary>シングルトン インスタンス。</summary>
    public static LhamielUpdateStrings Instance { get; } = new();

    private LhamielUpdateStrings()
    {
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 言語切替時にホスト側 (例: <see cref="App.SetLocale(string)"/>) から呼ぶことで、
    /// 全プロパティの再評価を XAML バインディングに通知する。
    /// PropertyName=null は INotifyPropertyChanged の「全プロパティ更新」シグナル。
    /// </summary>
    public void NotifyLocaleChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    /// <inheritdoc />
    public string Title => App.Text(KeyPrefix + "Title");

    /// <inheritdoc />
    public string AvailableHeader => App.Text(KeyPrefix + "Available");

    /// <inheritdoc />
    public string DownloadAndInstall => App.Text(KeyPrefix + "DownloadAndInstall");

    /// <inheritdoc />
    public string IgnoreThisVersion => App.Text(KeyPrefix + "IgnoreThisVersion");

    /// <inheritdoc />
    public string UpToDateMessage => App.Text(KeyPrefix + "UpToDate");

    /// <inheritdoc />
    public string ErrorHeader => App.Text(KeyPrefix + "Error");

    /// <inheritdoc />
    public string Close => App.Text("Close");

    /// <inheritdoc />
    public string CheckingMessage => App.Text(KeyPrefix + "Checking");
}
