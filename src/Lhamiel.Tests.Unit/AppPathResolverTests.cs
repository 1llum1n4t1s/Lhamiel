using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// AppPathResolver のユニットテスト
/// </summary>
public class AppPathResolverTests
{
    [Fact]
    public void ExecutablePath_IsNotNull()
    {
        Assert.NotNull(AppPathResolver.ExecutablePath);
    }

    [Fact]
    public void ExecutablePath_IsConsistentAcrossMultipleCalls()
    {
        // Lazy<T> でキャッシュされているので、複数回呼んでも同じ値
        var path1 = AppPathResolver.ExecutablePath;
        var path2 = AppPathResolver.ExecutablePath;
        Assert.Equal(path1, path2);
    }

    [Fact]
    public void ExecutablePath_IsNotWhitespace()
    {
        // 空文字列は許容（テスト環境で見つからない場合）だが、空白のみは不可
        var path = AppPathResolver.ExecutablePath;
        if (!string.IsNullOrEmpty(path))
            Assert.False(string.IsNullOrWhiteSpace(path));
    }

    [Fact]
    public void ExecutablePath_IfNotEmpty_HasExeExtension()
    {
        var path = AppPathResolver.ExecutablePath;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            Assert.EndsWith(".exe", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExecutablePath_IfNotEmpty_IsAbsolutePath()
    {
        var path = AppPathResolver.ExecutablePath;
        if (!string.IsNullOrEmpty(path))
            Assert.True(Path.IsPathRooted(path), $"パスが絶対パスではありません: {path}");
    }
}
