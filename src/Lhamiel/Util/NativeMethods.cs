using System.Runtime.InteropServices;
using System.Runtime.Versioning;
namespace Lhamiel.Util;

/// <summary>
/// Win32 API のネイティブメソッドをまとめるクラス
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class NativeMethods
{
    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONERROR = 0x00000010;
    private const uint MB_SETFOREGROUND = 0x00010000;

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint hWnd, string text, string caption, uint type);

    internal static void ShowErrorMessageBox(string message, string title)
    {
        if (!OperatingSystem.IsWindows())
            return;
        _ = MessageBox(0, message, title, MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
    }

    /// <summary>
    /// ウィンドウを前面に表示し、フォーカスを当てる
    /// </summary>
    /// <param name="hWnd">ウィンドウハンドル</param>
    /// <returns>成功した場合は true</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// 指定プロセスに「フォアグラウンドウィンドウを設定する権利」を付与する。
    /// 単一インスタンス起動時、ユーザー操作（ダブルクリック）直後でフォアグラウンド権を持つ
    /// 第 2 インスタンスが既存インスタンスの PID にこの権利を渡すことで、既存インスタンス側の
    /// <see cref="SetForegroundWindow"/> / Avalonia の Activate が Win32 フォアグラウンドロック
    /// （タスクバー点滅止まりで実際には前面化しない）で空振りするのを防ぐ。
    /// </summary>
    /// <param name="dwProcessId">前面化を許可するプロセス ID</param>
    /// <returns>成功した場合は true</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllowSetForegroundWindow(uint dwProcessId);

    /// <summary>
    /// シェルに関連する変更を通知する
    /// </summary>
    /// <param name="eventId">通知するイベント ID</param>
    /// <param name="flags">通知の形式を指定するフラグ</param>
    /// <param name="item1">イベント固有のデータ1</param>
    /// <param name="item2">イベント固有のデータ2</param>
    [LibraryImport("shell32.dll")]
    internal static partial void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

    /// <summary>
    /// ファイルの関連付けが変更されたことを示す定数
    /// </summary>
    internal const int SHCNE_ASSOCCHANGED = 0x08000000;

    /// <summary>
    /// 項目が ID リストであることを示す定数
    /// </summary>
    internal const int SHCNF_IDLIST = 0x0000;

    /// <summary>
    /// プロセスに明示的な AppUserModelID (AUMID) を設定する。
    /// Velopack がショートカット（タスクバーピン含む）に書き込む AUMID と一致させることで、
    /// タスクバーが exe パスではなく AUMID でピンとウィンドウを対応付け、
    /// アップデートで exe が差し替わってもアイコン解決が安定する。
    /// ウィンドウ生成前（タスクバーに現れる前）に呼ぶこと。
    /// </summary>
    /// <param name="appId">設定する AppUserModelID</param>
    /// <returns>HRESULT（S_OK = 0 で成功）</returns>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SetCurrentProcessExplicitAppUserModelID(string appId);
}
