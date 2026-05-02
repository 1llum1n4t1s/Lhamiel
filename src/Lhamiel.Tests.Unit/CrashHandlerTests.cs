using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

[Collection("CrashHandler")]
public class CrashHandlerTests
{
    [Fact]
    public void WriteMiniDump_ProducesDumpFile()
    {
        var dumpPath = CrashHandler.WriteMiniDump();
        Assert.NotNull(dumpPath);
        Assert.True(File.Exists(dumpPath), $"ダンプファイルが存在しない: {dumpPath}");
        Assert.True(new FileInfo(dumpPath!).Length > 0, "ダンプファイルが空");

        // クリーンアップ
        File.Delete(dumpPath!);
    }

    [Fact]
    public void WriteMiniDump_WithException_CreatesCompanionTextFile()
    {
        var ex = new InvalidOperationException("テスト用例外");
        var dumpPath = CrashHandler.WriteMiniDump(ex);
        Assert.NotNull(dumpPath);

        var txtPath = Path.ChangeExtension(dumpPath, ".txt");
        Assert.True(File.Exists(txtPath), "例外情報テキストが作成されていない");
        var content = File.ReadAllText(txtPath!);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("テスト用例外", content);

        // クリーンアップ
        File.Delete(dumpPath!);
        File.Delete(txtPath!);
    }

    [Fact]
    public void RotateOldDumps_RemovesExcessFiles()
    {
        var originalDumpDir = CrashHandler.DumpDirectory;
        var dumpDir = Path.Combine(Path.GetTempPath(), $"lhamiel_dump_test_{Guid.NewGuid():N}");
        CrashHandler.DumpDirectory = dumpDir;
        Directory.CreateDirectory(dumpDir);

        try
        {
            var originalMax = CrashHandler.MaxDumpFiles;
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

            var remaining = Directory.GetFiles(dumpDir, "*.dmp");
            Assert.Equal(3, remaining.Length);

            var remainingNames = remaining.Select(Path.GetFileName).Order().ToArray();
            Assert.Contains("Lhamiel_test_03.dmp", remainingNames);
            Assert.Contains("Lhamiel_test_04.dmp", remainingNames);
            Assert.Contains("Lhamiel_test_05.dmp", remainingNames);

            CrashHandler.MaxDumpFiles = originalMax;
        }
        finally
        {
            CrashHandler.DumpDirectory = originalDumpDir;
            if (Directory.Exists(dumpDir))
                Directory.Delete(dumpDir, true);
        }
    }

    [Fact]
    public void WriteMiniDump_DumpDirectoryCreatedAutomatically()
    {
        // WriteMiniDump が dumps/ ディレクトリを自動作成することを確認
        var dumpPath = CrashHandler.WriteMiniDump();
        Assert.NotNull(dumpPath);

        var dir = Path.GetDirectoryName(dumpPath)!;
        Assert.True(Directory.Exists(dir));

        // クリーンアップ
        File.Delete(dumpPath!);
    }
}
