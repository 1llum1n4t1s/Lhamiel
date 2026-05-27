using System.Diagnostics;

namespace Lhamiel.Util;

/// <summary>
/// 外部プロセス (ブラウザ / explorer.exe / 関連付けエディタ等) を起動するためのユーティリティ。
/// <para>
/// すべての公開メソッドは <see cref="Task.Run(System.Action)"/> 経由で別スレッドから
/// <see cref="Process.Start(ProcessStartInfo)"/> を呼び出す。これは <c>UseShellExecute=true</c>
/// 経由の <c>ShellExecuteEx</c> Win32 API が、UI スレッドで synchronous に走ると
/// 環境次第で SmartScreen / アンチウイルス / シェル拡張の初期化により秒〜数十秒
/// blocking する経路があり、メッセージポンプが停止して「アプリが操作不能」に見える
/// 既知の問題への対策（Issue #54 対応）。
/// </para>
/// <para>
/// 例外はメソッド内で握りつぶさない。呼び出し側で <c>try/catch</c> し、必要なログ文言を
/// 付けてユーザーへフィードバックする責務を持たせる（経路ごとに「フィードバックリンクを
/// 開けませんでした」「フォルダを開けませんでした」等、文脈に合った文言が異なるため）。
/// </para>
/// </summary>
public static class ShellOpener
{
    /// <summary>
    /// テスト時に <see cref="Process.Start(ProcessStartInfo)"/> をスキップするためのフラグ。
    /// <c>InternalsVisibleTo</c> 経由でテストプロジェクトから設定する。
    /// </summary>
    internal static bool DryRun { get; set; }

    /// <summary>
    /// 関連付けで指定パス / URL を開く。
    /// <para>
    /// 内部で <c>UseShellExecute=true</c> を指定し、Windows シェルの既定ハンドラ
    /// （ブラウザ / 関連付けエディタ等）に処理を委ねる。
    /// </para>
    /// </summary>
    /// <param name="fileNameOrUrl">URL またはファイルパス。シェルの関連付けが解決する。</param>
    public static Task OpenWithDefaultHandlerAsync(string fileNameOrUrl) =>
        Task.Run(() =>
        {
            if (DryRun) return;
            using var _ = Process.Start(new ProcessStartInfo
            {
                FileName = fileNameOrUrl,
                UseShellExecute = true,
            });
        });

    /// <summary>
    /// explorer.exe で指定フォルダを開く。
    /// <para>
    /// 引数は <see cref="ProcessStartInfo.ArgumentList"/> で渡すため、<c>CommandLineToArgvW</c>
    /// の規則通り安全にエスケープされ、スイッチ注入 (<c>/select,...</c> 等) を防ぐ。
    /// </para>
    /// </summary>
    /// <param name="folderPath">開くフォルダの絶対パス。</param>
    public static Task OpenInExplorerAsync(string folderPath) =>
        Task.Run(() =>
        {
            if (DryRun) return;
            var psi = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true,
                CreateNoWindow = false,
            };
            psi.ArgumentList.Add(folderPath);
            using var _ = Process.Start(psi);
        });
}
