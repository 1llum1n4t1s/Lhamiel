using System.Runtime.InteropServices;
using System.Runtime.Versioning;
namespace Lhamiel.Util;

/// <summary>
/// Win32 API のネイティブメソッドをまとめるクラス
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class NativeMethods
{
    /// <summary>
    /// ウィンドウを前面に表示し、フォーカスを当てる
    /// </summary>
    /// <param name="hWnd">ウィンドウハンドル</param>
    /// <returns>成功した場合は true</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(IntPtr hWnd);

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
}
