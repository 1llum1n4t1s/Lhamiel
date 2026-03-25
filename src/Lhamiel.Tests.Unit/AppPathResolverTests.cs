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
}
