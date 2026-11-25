using System.Diagnostics;
using Xunit;

namespace Lhamiel.Tests.Unit;

/// <summary>
/// テスト用のディレクトリジャンクション作成（mklink /J。権限不要）。
/// </summary>
internal static class DirectoryJunction
{
    public static void Create(string junction, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "/d", "/c", "mklink", "/J", junction, target },
        });
        Assert.NotNull(process);
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }
}
