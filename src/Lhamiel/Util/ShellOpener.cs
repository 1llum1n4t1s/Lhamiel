using System.ComponentModel;
using System.Diagnostics;

namespace Lhamiel.Util;

/// <summary>
/// 外部プロセス (ブラウザ / ファイルマネージャー / 関連付けエディタ等) を起動するためのユーティリティ。
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
    /// ユーザーが既定に設定したファイルマネージャーで指定フォルダを開く。
    /// <para>
    /// フォルダパスを <c>UseShellExecute=true</c> でそのままシェルへ渡し、<c>ShellExecuteEx</c> に
    /// 既定の verb を解決させる。<c>explorer.exe</c> を直接起動すると、ユーザーが
    /// <c>Directory\shell</c> の既定 verb を Kiriha / Files 等のサードパーティ製ファイルマネージャーへ
    /// 変更していても常に Windows 標準エクスプローラーが開いてしまうため
    /// (ユーザー報告)、シェルに委ねる形にしている。標準構成 (既定 verb = <c>open</c>) では
    /// <c>Directory\shell\open\command</c> が explorer を指すため、従来どおりエクスプローラーが開く。
    /// </para>
    /// <para>
    /// 既定ハンドラの起動に失敗した場合 (登録されたファイルマネージャーが削除済み等) は
    /// <c>explorer.exe</c> にフォールバックし、「フォルダが一切開かない」状態を避ける。
    /// フォールバック側の引数は <see cref="ProcessStartInfo.ArgumentList"/> で渡すため、
    /// <c>CommandLineToArgvW</c> の規則通り安全にエスケープされ、スイッチ注入
    /// (<c>/select,...</c> 等) を防ぐ。
    /// </para>
    /// </summary>
    /// <param name="folderPath">開くフォルダの絶対パス。</param>
    public static Task OpenFolderWithDefaultHandlerAsync(string folderPath) =>
        Task.Run(() =>
        {
            if (DryRun) return;
            try
            {
                using var _ = Process.Start(new ProcessStartInfo
                {
                    FileName = folderPath,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                Logger.Log(
                    $"既定のファイルマネージャーでフォルダを開けなかったため explorer.exe で開きます: {folderPath} ({ex.Message})",
                    LogLevel.Warning);

                var psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true,
                    CreateNoWindow = false,
                };
                psi.ArgumentList.Add(folderPath);
                using var _ = Process.Start(psi);
            }
        });
}
