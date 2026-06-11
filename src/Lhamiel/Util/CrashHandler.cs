using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.Win32.SafeHandles;
namespace Lhamiel.Util;

/// <summary>
/// 未処理例外時にミニダンプを書き出し、ローテーション管理する。
/// Program.Main の冒頭で <see cref="Register"/> を呼ぶこと。
/// </summary>
internal static partial class CrashHandler
{
    internal static string DumpDirectory { get; set; } =
        Path.Combine(Settings.AppDataDirectory, "dumps");

    internal static int MaxDumpFiles { get; set; } = 5;

    /// <summary>
    /// AppDomain.UnhandledException と TaskScheduler.UnobservedTaskException を登録する。
    /// Avalonia 起動前（Program.Main 冒頭）に呼び出すことで、フレームワーク初期化のクラッシュも捕捉する。
    /// </summary>
    public static void Register()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                WriteMiniDump(ex);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteMiniDump(e.Exception);
        };
    }

    /// <summary>
    /// 現在のプロセスのミニダンプを %LocalAppData%\Lhamiel\dumps\ に出力する。
    /// </summary>
    /// <remarks>
    /// 自己プロセスへの MiniDumpWriteDump は全スレッドをサスペンドするため、
    /// 他スレッドがヒープロック/ローダーロックを握ったままだと DbgHelp がデッドロックする窓がある
    /// （MS ドキュメントも自己ダンプは別プロセスからの実行を推奨）。
    /// クラッシュハンドラ経路は「既にプロセスが死んでいる」前提でこのリスクを受容するが、
    /// テストからは必ず子プロセスを対象にするオーバーロードを使うこと。
    /// </remarks>
    internal static string? WriteMiniDump(Exception? triggerException = null)
    {
        using var process = Process.GetCurrentProcess();
        return WriteMiniDump(process, triggerException);
    }

    /// <summary>
    /// 指定プロセスのミニダンプを %LocalAppData%\Lhamiel\dumps\ に出力する（テスト用差し替え点）。
    /// </summary>
    internal static string? WriteMiniDump(Process process, Exception? triggerException = null)
    {
        try
        {
            Directory.CreateDirectory(DumpDirectory);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var dumpPath = Path.Combine(DumpDirectory, $"Lhamiel_{timestamp}.dmp");

            using var fs = new FileStream(dumpPath, FileMode.Create, FileAccess.Write, FileShare.None);

            // MiniDumpNormal (0x00): スタック・レジスタ・実行コンテキストのみ。
            // データセグメント (0x01) はグローバル変数 (Settings 含む) を含むため、
            // 診断 ZIP でサポート担当に共有される可能性を考えるとプライバシー上不適切。
            // ハンドルデータ (0x04) もファイルパス情報を含むため除外。
            // 通常の例外解析にはスタック情報だけで十分。
            const int dumpType = 0x00;

            // 例外ポインタは呼び出しスレッドのものなので、自プロセス対象のときだけ添付する
            var exceptionPointers = process.Id == Environment.ProcessId
                ? Marshal.GetExceptionPointers()
                : IntPtr.Zero;
            var exceptionParam = IntPtr.Zero;
            var exceptionInfo = default(MinidumpExceptionInformation);
            if (exceptionPointers != IntPtr.Zero)
            {
                exceptionInfo.ThreadId = GetCurrentThreadId();
                exceptionInfo.ExceptionPointers = exceptionPointers;
                exceptionInfo.ClientPointers = 0;
                unsafe { exceptionParam = (IntPtr)(&exceptionInfo); }
            }

            var success = MiniDumpWriteDump(
                process.SafeHandle,
                (uint)process.Id,
                fs.SafeFileHandle,
                dumpType,
                exceptionParam,
                IntPtr.Zero,
                IntPtr.Zero);

            if (!success)
            {
                Logger.Log($"MiniDumpWriteDump 失敗: Marshal.GetLastWin32Error={Marshal.GetLastWin32Error()}", LogLevel.Error);
                fs.Dispose();
                try { File.Delete(dumpPath); } catch { /* ベストエフォート */ }
                return null;
            }

            Logger.Log($"ミニダンプを出力: {dumpPath}");

            if (triggerException != null)
            {
                var infoPath = Path.ChangeExtension(dumpPath, ".txt");
                File.WriteAllText(infoPath,
                    $"Timestamp: {DateTime.Now:O}\n" +
                    $"Exception: {triggerException.GetType().FullName}\n" +
                    $"Message: {triggerException.Message}\n" +
                    $"StackTrace:\n{triggerException}");
            }

            RotateOldDumps();
            return dumpPath;
        }
        catch (Exception ex)
        {
            Logger.Log($"ミニダンプ出力に失敗: {ex.Message}", LogLevel.Error);
            return null;
        }
    }

    /// <summary>
    /// dumps/ フォルダ内の .dmp が MaxDumpFiles を超えた分を古い順に削除する。
    /// <para>
    /// ⚠️ First Crash Preservation (RTK レビュー #F-004 対応): 起動失敗ループ等で
    /// 連鎖クラッシュが発生した場合、根本原因の "最初のクラッシュ" が古いタイムスタンプで
    /// 容易に rotation 削除されてしまう問題への対策。
    /// </para>
    /// <para>
    /// 最古の dump 1 つ (= 真の root cause である可能性が最も高い) は LastWriteTime 順 rotation の
    /// 対象から除外して保持する。<see cref="MaxDumpFiles"/> 個までは追加で「最新」を保持。
    /// 結果として保持されるのは「最古 1 + 最新 (MaxDumpFiles - 1)」になる。
    /// </para>
    /// </summary>
    internal static void RotateOldDumps()
    {
        try
        {
            if (!Directory.Exists(DumpDirectory))
                return;

            // LastWriteTime 昇順 (古い順) で並べ、最古を first crash として保護対象に隔離
            var dumpFiles = Directory.GetFiles(DumpDirectory, "*.dmp")
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.LastWriteTime)
                .ToList();

            if (dumpFiles.Count <= MaxDumpFiles)
                return;

            // 保持する dump: [最古 (first crash)] + [最新 MaxDumpFiles-1 個]
            var keepSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                dumpFiles[0].FullName  // first crash
            };
            // 最新 MaxDumpFiles-1 個を保持セットに追加
            foreach (var recent in dumpFiles.OrderByDescending(f => f.LastWriteTime).Take(MaxDumpFiles - 1))
            {
                keepSet.Add(recent.FullName);
            }

            // 保持セットに含まれない dump を削除
            foreach (var old in dumpFiles)
            {
                if (keepSet.Contains(old.FullName)) continue;
                try
                {
                    old.Delete();
                    var companion = Path.ChangeExtension(old.FullName, ".txt");
                    if (File.Exists(companion))
                        File.Delete(companion);
                }
                catch (Exception ex)
                {
                    Logger.Log($"ダンプローテーション中のファイル削除失敗: {old.Name} - {ex.Message}", LogLevel.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"ダンプローテーション失敗: {ex.Message}", LogLevel.Warning);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinidumpExceptionInformation
    {
        public uint ThreadId;
        public IntPtr ExceptionPointers;
        public int ClientPointers;
    }

    [LibraryImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MiniDumpWriteDump(
        SafeProcessHandle hProcess,
        uint processId,
        SafeFileHandle hFile,
        int dumpType,
        IntPtr exceptionParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
