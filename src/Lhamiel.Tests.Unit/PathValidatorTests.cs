using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// PathValidator のユニットテスト（実装を信用しないエッジケース重視）
/// </summary>
public class PathValidatorTests
{
    // === IsProtectedDirectory ===

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void IsProtectedDirectory_WithNullOrEmpty_ReturnsTrue(string? path)
    {
        // null/空の場合は安全のために保護されているとみなすべき
        Assert.True(PathValidator.IsProtectedDirectory(path!));
    }

    [Theory]
    [InlineData(@"C:\")]
    public void IsProtectedDirectory_WithDriveRoot_ReturnsTrue(string path)
    {
        Assert.True(PathValidator.IsProtectedDirectory(path));
    }

    [Fact]
    public void IsProtectedDirectory_WithDesktop_ReturnsTrue()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!string.IsNullOrEmpty(desktop))
            Assert.True(PathValidator.IsProtectedDirectory(desktop));
    }

    [Fact]
    public void IsProtectedDirectory_WithUserProfile_ReturnsTrue()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
            Assert.True(PathValidator.IsProtectedDirectory(profile));
    }

    [Fact]
    public void IsProtectedDirectory_WithTempSubfolder_ReturnsFalse()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "test_subfolder_" + Guid.NewGuid());
        Assert.False(PathValidator.IsProtectedDirectory(tempPath));
    }

    [Fact]
    public void IsProtectedDirectory_WithTrailingSeparator_StillDetects()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!string.IsNullOrEmpty(desktop))
        {
            // 末尾にセパレータをつけても検出すること
            Assert.True(PathValidator.IsProtectedDirectory(desktop + @"\"));
        }
    }

    // === IsSystemCriticalDirectory（直接テスト） ===
    // 旧来は Settings.SanitizeAfterLoad 経由の間接テストしかなく、
    // ProgramFiles / System32 / プロファイル根の網羅性が確認できない構造だった。

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void IsSystemCriticalDirectory_WithNullOrEmpty_ReturnsTrue(string? path)
    {
        Assert.True(PathValidator.IsSystemCriticalDirectory(path!));
    }

    [Theory]
    [InlineData(@"C:\")]
    public void IsSystemCriticalDirectory_WithDriveRoot_ReturnsTrue(string path)
    {
        Assert.True(PathValidator.IsSystemCriticalDirectory(path));
    }

    [Fact]
    public void IsSystemCriticalDirectory_WithWindowsFolder_ReturnsTrue()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.SkipWhen(string.IsNullOrEmpty(windows), "Windows フォルダが解決できないためスキップ");
        Assert.True(PathValidator.IsSystemCriticalDirectory(windows));
    }

    [Fact]
    public void IsSystemCriticalDirectory_WithProgramFiles_ReturnsTrue()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Assert.SkipWhen(string.IsNullOrEmpty(pf), "ProgramFiles フォルダが解決できないためスキップ");
        Assert.True(PathValidator.IsSystemCriticalDirectory(pf));
    }

    [Fact]
    public void IsSystemCriticalDirectory_WithUserProfileRoot_ReturnsTrue()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.SkipWhen(string.IsNullOrEmpty(profile), "UserProfile フォルダが解決できないためスキップ");
        Assert.True(PathValidator.IsSystemCriticalDirectory(profile));
    }

    [Fact]
    public void IsSystemCriticalDirectory_WithSystemSubdirectory_ReturnsTrue()
    {
        // 回帰防止: Windows/System32/drivers 等のサブディレクトリも保護対象であること。
        // 旧実装では HashSet.Contains の完全一致のみで判定しており、
        // settings.json 改竄経由で `C:\Windows\System32\drivers` を出力先に設定する
        // 経路が通り抜けていた（PR #48 round 10 で StartsWith ベースに修正）。
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.SkipWhen(string.IsNullOrEmpty(windows), "Windows フォルダが解決できないためスキップ");
        var system32Drivers = Path.Combine(windows, "System32", "drivers");
        Assert.True(PathValidator.IsSystemCriticalDirectory(system32Drivers));
    }

    [Fact]
    public void IsSystemCriticalDirectory_WithTempSubfolder_ReturnsFalse()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "MyExtraction");
        Assert.False(PathValidator.IsSystemCriticalDirectory(tempPath));
    }
}
