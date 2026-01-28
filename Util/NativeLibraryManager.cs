using System.Runtime.InteropServices;
using System.IO;

namespace Lhamiel.Util;

/// <summary>
/// ネイティブライブラリのロードと生存期間を管理するクラス
/// </summary>
public static class NativeLibraryManager
{
    private static IntPtr _hModule = IntPtr.Zero;

    /// <summary>
    /// 7z.dll をプロセスにロードして固定します。
    /// これにより、ライブラリの Dispose 時に DLL がアンロードされるのを防ぎ、
    /// 後から実行されるファイナライザによるアクセス違反を回避します。
    /// </summary>
    public static void Initialize()
    {
        if (_hModule != IntPtr.Zero) return;

        // 7z.dll のパスを取得（実行ファイルと同じディレクトリを想定）
        var dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7z.dll");
        
        if (!File.Exists(dllPath))
        {
            Logger.Log($"7z.dll が見つかりません: {dllPath}", LogLevel.Warning);
            return;
        }

        // LoadLibrary を呼び出して参照カウントを増やす
        _hModule = LoadLibrary(dllPath);
        
        if (_hModule == IntPtr.Zero)
        {
            var errorCode = Marshal.GetLastWin32Error();
            Logger.Log($"7z.dll のロードに失敗しました。エラーコード: {errorCode}", LogLevel.Error);
        }
        else
        {
            Logger.Log($"7z.dll をプロセスに固定しました: {dllPath}");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);
}
