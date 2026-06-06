using System.Runtime.InteropServices;
namespace Lhamiel.Util;

/// <summary>
/// ネイティブライブラリのロードと生存期間を管理するクラス
/// </summary>
public static partial class NativeLibraryManager
{
    private static IntPtr _hModule = IntPtr.Zero;
    private static readonly object _initLock = new();

    /// <summary>
    /// 7z.dll をプロセスにロードして固定します。
    /// これにより、ライブラリの Dispose 時に DLL がアンロードされるのを防ぎ、
    /// 後から実行されるファイナライザによるアクセス違反を回避します。
    /// <para>
    /// 現状は <see cref="App"/> コンストラクタから 1 回だけ呼ばれるが、将来の経路追加で
    /// 並列呼び出しが発生した場合に LoadLibrary が二重実行されてハンドル leak しないよう
    /// double-checked locking で thread-safe 化している。RTK レビュー #C2-011 対応。
    /// </para>
    /// </summary>
    public static void Initialize()
    {
        if (_hModule != IntPtr.Zero) return;

        lock (_initLock)
        {
            // ダブルチェック: 他スレッドが先に初期化を完了した場合を防止
            if (_hModule != IntPtr.Zero) return;

            // DLL ハイジャック対策 (RTK レビュー #10): プロセス全体の既定 DLL 探索パスから
            // 「カレントディレクトリ」を外し、System32・アプリディレクトリ・明示登録ディレクトリのみを
            // 探索対象にする。攻撃者が CWD に悪意ある依存 DLL を置く planting 攻撃を防ぐ。
            if (!SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS))
            {
                var setDirErr = Marshal.GetLastWin32Error();
                Logger.Log($"SetDefaultDllDirectories に失敗（既定探索のまま継続）。エラーコード: {setDirErr}", LogLevel.Warning);
            }

            // 7z.dll のパスを取得（実行ファイルと同じディレクトリを想定）
            // 単一ファイル公開時、ネイティブDLLは展開されず実行ファイルと同じ場所に配置される
            var dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7z.dll");

            if (!File.Exists(dllPath))
            {
                Logger.Log($"7z.dll が見つかりません: {dllPath}", LogLevel.Warning);
                return;
            }

            // フルパス指定 + 安全な探索フラグでロードする。7z.dll 本体はフルパスで確定し、
            // 7z.dll が依存する DLL は「7z.dll 自身のディレクトリ + 既定の安全ディレクトリ」からのみ
            // 解決する（CWD・PATH を探索対象から外す）。
            _hModule = LoadLibraryEx(dllPath, IntPtr.Zero,
                LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);

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
    }

    // DLL 探索フラグ (winnt.h): ロードする DLL とその依存 DLL の解決先を限定する。
    private const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100;
    private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;

    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDefaultDllDirectories(uint directoryFlags);
}
