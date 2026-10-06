using System.Text;
using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

public sealed class ShellSelectionFileTests
{
    [Theory]
    [InlineData("--extract")]
    [InlineData("--compress")]
    public void Parse_LargeSelection_PreservesOrderAndConsumesFile(string operation)
    {
        var token = Guid.NewGuid().ToString("N");
        var path = ShellSelectionFile.GetPath(token);
        var paths = Enumerable.Range(0, 2000).Select(i => $@"C:\日本語のフォルダー\選択 {i}.txt").ToArray();
        try
        {
            File.WriteAllBytes(path, Encoding.Unicode.GetBytes(string.Join('\0', paths) + '\0'));
            var request = App.ParseCommandLineArgs([operation, ShellSelectionFile.Argument, token]);
            Assert.Equal(paths, request.FilePaths);
            Assert.Equal(operation == "--extract" ? CommandLineOperation.Extract : CommandLineOperation.Compress,
                request.Operation);
            Assert.False(File.Exists(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("../anything")]
    [InlineData("C:\\anything")]
    [InlineData("")]
    public void Read_RejectsNonTokenInput(string token) =>
        Assert.Throws<InvalidDataException>(() => ShellSelectionFile.Read(token));

    [Theory]
    [InlineData("relative.txt\0")]
    [InlineData("--compress\0")]
    [InlineData("C:\\good.txt\0\0")]
    [InlineData("C:\\truncated.txt")]
    public void Read_RejectsIncompleteOrNonPathListAndDeletesFile(string content)
    {
        var token = Guid.NewGuid().ToString("N");
        var path = ShellSelectionFile.GetPath(token);
        try
        {
            File.WriteAllBytes(path, Encoding.Unicode.GetBytes(content));
            Assert.Throws<InvalidDataException>(() => ShellSelectionFile.Read(token));
            Assert.False(File.Exists(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_RejectsOversizedListAndDeletesFile()
    {
        var token = Guid.NewGuid().ToString("N");
        var path = ShellSelectionFile.GetPath(token);
        try
        {
            using (var stream = File.Create(path))
                stream.SetLength(ShellSelectionFile.MaxBytes + 2);
            Assert.Throws<InvalidDataException>(() => ShellSelectionFile.Read(token));
            Assert.False(File.Exists(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_RejectsReparsePointAndDeletesOnlyLink()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "シェル選択リストは Windows 専用");
        var token = Guid.NewGuid().ToString("N");
        var path = ShellSelectionFile.GetPath(token);
        var target = Path.Combine(Path.GetTempPath(), $"Lhamiel-selection-target-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(target, System.Text.Encoding.Unicode.GetBytes(@"C:\source.txt" + '\0'));
            try { File.CreateSymbolicLink(path, target); }
            catch (IOException ex) when (ex.HResult == unchecked((int)0x80070522))
            {
                Assert.Skip("この実行ユーザーにはシンボリックリンク作成権限がありません");
            }

            Assert.Throws<InvalidDataException>(() => ShellSelectionFile.Read(token));
            Assert.False(File.Exists(path));
            Assert.True(File.Exists(target));
        }
        finally
        {
            File.Delete(path);
            File.Delete(target);
        }
    }
}
