using System.Diagnostics;
using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

[Collection("CrashHandler")]
public class CrashHandlerTests
{
    /// <summary>
    /// ダンプ対象の子プロセスを起動する。
    /// 自プロセスへの MiniDumpWriteDump は全スレッドをサスペンドするため、
    /// xUnit の並列実行中に他テストのスレッドがヒープロック/ローダーロックを
    /// 握ったままサスペンドされると DbgHelp がデッドロックし、テストプロセス全体が
    /// 無期限ハングする（実機 dump で確認済み）。テストでは必ず子プロセスを対象にする。
    /// </summary>
    private static Process StartDumpTargetProcess()
    {
        var psi = new ProcessStartInfo("ping.exe", "-n 60 127.0.0.1")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        var process = Process.Start(psi)!;

        // Process.Start 直後はプロセス初期化 (ntdll/モジュールロード) 完了前のことがあり、
        // その瞬間の MiniDumpWriteDump は ERROR_PARTIAL_COPY で失敗する (CI で再現)。
        // モジュール列挙が通る = 初期化完了とみなして待つ。
        for (var i = 0; i < 100; i++)
        {
            try
            {
                _ = process.Modules;
                break;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Thread.Sleep(20);
            }
            catch (InvalidOperationException)
            {
                break; // 既に終了している場合はダンプ側の失敗として顕在化させる
            }
        }
        return process;
    }

    /// <summary>
    /// 初期化タイミング起因の一過性失敗 (ERROR_PARTIAL_COPY 等) を吸収するリトライ付きダンプ。
    /// 本物の失敗 (アクセス拒否・対象消滅など) は全試行が失敗して null が返る。
    /// </summary>
    private static string? WriteDumpWithRetry(Process target, Exception? triggerException = null)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var path = CrashHandler.WriteMiniDump(target, triggerException);
            if (path is not null)
                return path;
            Thread.Sleep(100);
        }
        return null;
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ベストエフォート（既に終了済みなど）
        }
        process.Dispose();
    }

    [Fact]
    public void WriteMiniDump_ProducesDumpFile()
    {
        var originalDumpDir = CrashHandler.DumpDirectory;
        var dumpDir = Path.Combine(Path.GetTempPath(), $"lhamiel_dump_test_{Guid.NewGuid():N}");
        CrashHandler.DumpDirectory = dumpDir;
        var target = StartDumpTargetProcess();
        try
        {
            var dumpPath = WriteDumpWithRetry(target);
            Assert.NotNull(dumpPath);
            Assert.True(File.Exists(dumpPath), $"ダンプファイルが存在しない: {dumpPath}");
            Assert.True(new FileInfo(dumpPath!).Length > 0, "ダンプファイルが空");
        }
        finally
        {
            KillQuietly(target);
            CrashHandler.DumpDirectory = originalDumpDir;
            if (Directory.Exists(dumpDir))
                Directory.Delete(dumpDir, true);
        }
    }

    [Fact]
    public void WriteMiniDump_WithException_CreatesCompanionTextFile()
    {
        var originalDumpDir = CrashHandler.DumpDirectory;
        var dumpDir = Path.Combine(Path.GetTempPath(), $"lhamiel_dump_test_{Guid.NewGuid():N}");
        CrashHandler.DumpDirectory = dumpDir;
        var target = StartDumpTargetProcess();
        try
        {
            var ex = new InvalidOperationException("テスト用例外");
            var dumpPath = WriteDumpWithRetry(target, ex);
            Assert.NotNull(dumpPath);

            var txtPath = Path.ChangeExtension(dumpPath, ".txt");
            Assert.True(File.Exists(txtPath), "例外情報テキストが作成されていない");
            var content = File.ReadAllText(txtPath!);
            Assert.Contains("InvalidOperationException", content);
            Assert.Contains("テスト用例外", content);
        }
        finally
        {
            KillQuietly(target);
            CrashHandler.DumpDirectory = originalDumpDir;
            if (Directory.Exists(dumpDir))
                Directory.Delete(dumpDir, true);
        }
    }

    [Fact]
    public void RotateOldDumps_RemovesExcessFiles()
    {
        var originalDumpDir = CrashHandler.DumpDirectory;
        var originalMax = CrashHandler.MaxDumpFiles;
        var dumpDir = Path.Combine(Path.GetTempPath(), $"lhamiel_dump_test_{Guid.NewGuid():N}");
        CrashHandler.DumpDirectory = dumpDir;
        Directory.CreateDirectory(dumpDir);

        try
        {
            CrashHandler.MaxDumpFiles = 3;

            for (var i = 0; i < 6; i++)
            {
                var path = Path.Combine(dumpDir, $"Lhamiel_test_{i:D2}.dmp");
                File.WriteAllText(path, $"dummy dump {i}");
                File.SetLastWriteTime(path, DateTime.Now.AddMinutes(-60 + i * 10));

                var txtPath = Path.ChangeExtension(path, ".txt");
                File.WriteAllText(txtPath, $"exception info {i}");
                File.SetLastWriteTime(txtPath, DateTime.Now.AddMinutes(-60 + i * 10));
            }

            CrashHandler.RotateOldDumps();

            // First Crash Preservation (RTK レビュー #F-004): 最古 1 + 最新 (MaxDumpFiles - 1) 個を保持。
            // 起動失敗ループ時に根本原因である "最初のクラッシュ" が rotation 削除されないように
            // 設計を変更。よって test_00 (最古) と test_05, test_04 (最新 2 個) が残る。
            var remaining = Directory.GetFiles(dumpDir, "*.dmp");
            Assert.Equal(3, remaining.Length);

            var remainingNames = remaining.Select(Path.GetFileName).Order().ToArray();
            Assert.Contains("Lhamiel_test_00.dmp", remainingNames); // first crash 保護
            Assert.Contains("Lhamiel_test_04.dmp", remainingNames); // 最新 2 個
            Assert.Contains("Lhamiel_test_05.dmp", remainingNames);
        }
        finally
        {
            CrashHandler.MaxDumpFiles = originalMax;
            CrashHandler.DumpDirectory = originalDumpDir;
            if (Directory.Exists(dumpDir))
                Directory.Delete(dumpDir, true);
        }
    }

    [Fact]
    public void WriteMiniDump_DumpDirectoryCreatedAutomatically()
    {
        var originalDumpDir = CrashHandler.DumpDirectory;
        var dumpDir = Path.Combine(Path.GetTempPath(), $"lhamiel_dump_test_{Guid.NewGuid():N}");
        CrashHandler.DumpDirectory = dumpDir;
        var target = StartDumpTargetProcess();
        try
        {
            var dumpPath = WriteDumpWithRetry(target);
            Assert.NotNull(dumpPath);

            var dir = Path.GetDirectoryName(dumpPath)!;
            Assert.True(Directory.Exists(dir));
        }
        finally
        {
            KillQuietly(target);
            CrashHandler.DumpDirectory = originalDumpDir;
            if (Directory.Exists(dumpDir))
                Directory.Delete(dumpDir, true);
        }
    }
}
