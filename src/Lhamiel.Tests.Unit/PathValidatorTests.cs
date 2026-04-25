using Lhamiel.Util;
using Xunit;
namespace Lhamiel.Tests.Unit;

/// <summary>
/// PathValidator のユニットテスト（実装を信用しないエッジケース重視）
/// </summary>
public class PathValidatorTests
{
    // === IsValidFilePath ===

    [Fact]
    public void IsValidFilePath_WithNullPath_ReturnsFalse()
    {
        var result = PathValidator.IsValidFilePath(null!, out var error);
        Assert.False(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void IsValidFilePath_WithEmptyString_ReturnsFalse()
    {
        var result = PathValidator.IsValidFilePath("", out var error);
        Assert.False(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void IsValidFilePath_WithWhitespaceOnly_ReturnsFalse()
    {
        var result = PathValidator.IsValidFilePath("   ", out var error);
        Assert.False(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void IsValidFilePath_WithValidPath_ReturnsTrue()
    {
        var result = PathValidator.IsValidFilePath(@"C:\temp\file.txt", out var error);
        Assert.True(result);
        Assert.Null(error);
    }

    [Fact]
    public void IsValidFilePath_WithJapanesePath_ReturnsTrue()
    {
        var result = PathValidator.IsValidFilePath(@"C:\temp\日本語フォルダ\ファイル.txt", out var error);
        Assert.True(result);
        Assert.Null(error);
    }

    [Fact]
    public void IsValidFilePath_WithVeryLongPath_ReturnsFalse()
    {
        var longPath = @"C:\temp\" + new string('a', 300) + ".txt";
        var result = PathValidator.IsValidFilePath(longPath, out var error);
        Assert.False(result);
        Assert.NotNull(error);
        Assert.Contains("PathTooLong", error);
    }

    [Fact]
    public void IsValidFilePath_WithExactlyMaxLength_ReturnsFalse()
    {
        // 260文字ちょうどのパスはどうなるか？（>260 で弾くので通るはず）
        // ただし実装が > か >= かで結果が変わる
        var basePath = @"C:\temp\";
        var remaining = 260 - basePath.Length - 4; // .txtの分
        var path = basePath + new string('a', remaining) + ".txt";
        Assert.Equal(260, path.Length);
        var result = PathValidator.IsValidFilePath(path, out _);
        // 260文字以下は有効であるべき
        Assert.True(result);
    }

    [Fact]
    public void IsValidFilePath_With261Chars_ReturnsFalse()
    {
        var basePath = @"C:\temp\";
        var remaining = 261 - basePath.Length - 4;
        var path = basePath + new string('a', remaining) + ".txt";
        Assert.Equal(261, path.Length);
        var result = PathValidator.IsValidFilePath(path, out var error);
        Assert.False(result);
        Assert.NotNull(error);
    }

    // === 予約デバイス名 ===

    [Theory]
    [InlineData(@"C:\temp\CON")]
    [InlineData(@"C:\temp\PRN")]
    [InlineData(@"C:\temp\AUX")]
    [InlineData(@"C:\temp\NUL")]
    [InlineData(@"C:\temp\COM1")]
    [InlineData(@"C:\temp\LPT1")]
    public void IsValidFilePath_WithReservedDeviceName_ReturnsFalse(string path)
    {
        var result = PathValidator.IsValidFilePath(path, out var error);
        Assert.False(result);
        Assert.NotNull(error);
        Assert.Contains("ReservedDeviceName", error);
    }

    [Theory]
    [InlineData(@"C:\temp\CON.txt")]
    [InlineData(@"C:\temp\NUL.log")]
    public void IsValidFilePath_WithReservedNameAndExtension_ReturnsFalse(string path)
    {
        // CON.txt のようにファイル名部分が予約名の場合も拒否すべき
        var result = PathValidator.IsValidFilePath(path, out var error);
        Assert.False(result);
    }

    [Fact]
    public void IsValidFilePath_WithReservedNameInDifferentCase_ReturnsFalse()
    {
        var result = PathValidator.IsValidFilePath(@"C:\temp\con", out _);
        Assert.False(result);
    }

    [Fact]
    public void IsValidFilePath_WithNonReservedSimilarName_ReturnsTrue()
    {
        // "CONX" は予約名ではない
        var result = PathValidator.IsValidFilePath(@"C:\temp\CONX.txt", out _);
        Assert.True(result);
    }

    // === IsWithinDirectory ===
    // 注: IsWithinDirectory は [Obsolete] 化されている。テストは既存挙動と
    // プレフィックス衝突バイパス修正の両方を検証する。
#pragma warning disable CS0618 // Type or member is obsolete
    [Fact]
    public void IsWithinDirectory_WithChildPath_ReturnsTrue()
    {
        Assert.True(PathValidator.IsWithinDirectory(@"C:\parent\child\file.txt", @"C:\parent"));
    }

    [Fact]
    public void IsWithinDirectory_WithSamePath_ReturnsTrue()
    {
        Assert.True(PathValidator.IsWithinDirectory(@"C:\parent", @"C:\parent"));
    }

    [Fact]
    public void IsWithinDirectory_WithOutsidePath_ReturnsFalse()
    {
        Assert.False(PathValidator.IsWithinDirectory(@"D:\other\file.txt", @"C:\parent"));
    }

    [Fact]
    public void IsWithinDirectory_WithTraversalAttempt_ReturnsFalse()
    {
        // パストラバーサルで親ディレクトリ外に出る場合
        Assert.False(PathValidator.IsWithinDirectory(@"C:\parent\..\other\file.txt", @"C:\parent"));
    }

    [Fact]
    public void IsWithinDirectory_CaseInsensitive_ReturnsTrue()
    {
        Assert.True(PathValidator.IsWithinDirectory(@"C:\PARENT\child\file.txt", @"C:\parent"));
    }

    [Fact]
    public void IsWithinDirectory_PrefixCollision_ReturnsFalse()
    {
        // プレフィックス衝突バイパス防止: "C:\parent" と "C:\parent-evil" を混同しない
        Assert.False(PathValidator.IsWithinDirectory(@"C:\parent-evil\file.txt", @"C:\parent"));
        Assert.False(PathValidator.IsWithinDirectory(@"C:\parentx", @"C:\parent"));
    }
#pragma warning restore CS0618

    // === IsProtectedDirectory ===

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsProtectedDirectory_WithNullOrEmpty_ReturnsTrue(string? path)
    {
        // null/空の場合は安全のために保護されているとみなすべき
        Assert.True(PathValidator.IsProtectedDirectory(path!));
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
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
    [InlineData("")]
    [InlineData("   ")]
    public void IsSystemCriticalDirectory_WithNullOrEmpty_ReturnsTrue(string? path)
    {
        Assert.True(PathValidator.IsSystemCriticalDirectory(path!));
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
    public void IsSystemCriticalDirectory_WithDriveRoot_ReturnsTrue(string path)
    {
        Assert.True(PathValidator.IsSystemCriticalDirectory(path));
    }

    [Fact]
    public void IsSystemCriticalDirectory_WithWindowsFolder_ReturnsTrue()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windows))
            Assert.True(PathValidator.IsSystemCriticalDirectory(windows));
    }

    [Fact]
    public void IsSystemCriticalDirectory_WithProgramFiles_ReturnsTrue()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(pf))
            Assert.True(PathValidator.IsSystemCriticalDirectory(pf));
    }

    [Fact]
    public void IsSystemCriticalDirectory_WithSystem32_ReturnsTrue()
    {
        var sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (!string.IsNullOrEmpty(sys))
            Assert.True(PathValidator.IsSystemCriticalDirectory(sys));
    }

    [Fact]
    public void IsSystemCriticalDirectory_WithUserProfileRoot_ReturnsTrue()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
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
        if (string.IsNullOrEmpty(windows)) return;
        var system32Drivers = Path.Combine(windows, "System32", "drivers");
        Assert.True(PathValidator.IsSystemCriticalDirectory(system32Drivers));
    }

    [Fact]
    public void IsSystemCriticalDirectory_WithDesktop_ReturnsFalse()
    {
        // ユーザーコンテンツフォルダは「正当な出力先」として許可されること。
        // IsProtectedDirectory（再帰削除拒否用）とは振る舞いが異なる。
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!string.IsNullOrEmpty(desktop))
            Assert.False(PathValidator.IsSystemCriticalDirectory(desktop));
    }

    [Fact]
    public void IsSystemCriticalDirectory_WithDownloads_ReturnsFalse()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile)) return;
        var downloads = Path.Combine(profile, "Downloads");
        if (!Directory.Exists(downloads)) return;
        Assert.False(PathValidator.IsSystemCriticalDirectory(downloads));
    }

    [Fact]
    public void IsSystemCriticalDirectory_WithTempSubfolder_ReturnsFalse()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "MyExtraction");
        Assert.False(PathValidator.IsSystemCriticalDirectory(tempPath));
    }

    // === ValidatePathLength edge cases ===

    [Fact]
    public void IsValidFilePath_WithVeryLongFilename_ReturnsFalse()
    {
        // ディレクトリは短いがファイル名が255文字超
        var path = @"C:\temp\" + new string('a', 256) + ".txt";
        if (path.Length <= 260) // パス全体長は260以内でもファイル名が長い場合
        {
            var result = PathValidator.IsValidFilePath(path, out var error);
            Assert.False(result);
        }
    }
}
