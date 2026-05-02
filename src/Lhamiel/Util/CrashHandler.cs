using System.Diagnostics;
using System.Runtime.InteropServices;
namespace Lhamiel.Util;

/// <summary>
/// 未処理例外時にミニダンプを書き出し、ローテーション管理する。
/// Program.Main の冒頭で <see cref="Register"/> を呼ぶこと。
/// </summary>
internal static partial class CrashHandler
{
    internal static readonly string DumpDirectory =
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
    internal static string? WriteMiniDump(Exception? triggerException = null)
    {
        try
        {
            Directory.CreateDirectory(DumpDirectory);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dumpPath = Path.Combine(DumpDirectory, $"Lhamiel_{timestamp}.dmp");

            using var process = Process.GetCurrentProcess();
            using var fs = new FileStream(dumpPath, FileMode.Create, FileAccess.Write, FileShare.None);

            // MiniDumpWithDataSegs (0x01) + MiniDumpWithHandleData (0x04) = 0x05
            // フルヒープは巨大になるため、データセグメント + ハンドル情報に絞る
            const int dumpType = 0x01 | 0x04;

            var success = MiniDumpWriteDump(
                process.Handle,
                (uint)process.Id,
                fs.SafeFileHandle.DangerousGetHandle(),
                dumpType,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            if (!success)
            {
                Logger.Log($"MiniDumpWriteDump 失敗: Marshal.GetLastWin32Error={Marshal.GetLastWin32Error()}", LogLevel.Error);
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
    /// </summary>
    internal static void RotateOldDumps()
    {
        try
        {
            var dumpFiles = Directory.GetFiles(DumpDirectory, "*.dmp")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .ToList();

            foreach (var old in dumpFiles.Skip(MaxDumpFiles))
            {
                old.Delete();
                // 付随する .txt も削除
                var companion = Path.ChangeExtension(old.FullName, ".txt");
                if (File.Exists(companion))
                    File.Delete(companion);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"ダンプローテーション失敗: {ex.Message}", LogLevel.Warning);
        }
    }

    [LibraryImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MiniDumpWriteDump(
        IntPtr hProcess,
        uint processId,
        IntPtr hFile,
        int dumpType,
        IntPtr exceptionParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);
}
