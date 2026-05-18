using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// AppPathResolver のユニットテスト。
/// Resolve() のフォールバック 4 段 (Environment.ProcessPath → Process.MainModule.FileName →
/// BaseDirectory + *.exe glob → AppContext.BaseDirectory + Lhamiel.exe) の挙動を保証する。
/// </summary>
public class AppPathResolverTests
{
    /// <summary>
    /// ExecutablePath は non-null かつ非空文字列を返す。
    /// 空文字列が返るのは Resolve() の最終段フォールバック失敗時のみ (FileAssociation 等で機能不全を起こす経路)。
    /// </summary>
    [Fact]
    public void ExecutablePath_ReturnsNonEmptyString()
    {
        var path = AppPathResolver.ExecutablePath;
        Assert.NotNull(path);
        Assert.False(string.IsNullOrEmpty(path), $"AppPathResolver は空文字列を返した (Resolve のフォールバック全段失敗)");
    }

    /// <summary>
    /// ExecutablePath は遅延キャッシュ済み (Lazy&lt;string&gt;) で、複数回呼び出しても同じ参照を返す。
    /// </summary>
    [Fact]
    public void ExecutablePath_IsCached_ReturnsSameReferenceOnMultipleCalls()
    {
        var first = AppPathResolver.ExecutablePath;
        var second = AppPathResolver.ExecutablePath;
        Assert.Same(first, second);
    }

    /// <summary>
    /// ExecutablePath が解決するパスは .exe 拡張子で終わる (Windows プラットフォーム前提)。
    /// Native AOT publish 後の Lhamiel.exe / dotnet test ホストの testhost.exe / .NET CLI の dotnet.exe いずれも .exe で終わる。
    /// </summary>
    [Fact]
    public void ExecutablePath_EndsWithExeExtension()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "AppPathResolver は Windows 専用");
        var path = AppPathResolver.ExecutablePath;
        Assert.EndsWith(".exe", path, StringComparison.OrdinalIgnoreCase);
    }
}
