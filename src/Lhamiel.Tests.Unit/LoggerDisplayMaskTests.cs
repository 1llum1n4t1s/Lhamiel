using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

public sealed class LoggerDisplayMaskTests
{
    private static string Mask(string input, params string[] names) =>
        Logger.MaskUserNameElements(input, names);

    [Theory]
    [InlineData(@"C:\Users\alice\a.zip", @"C:\Users\<USER>\a.zip")]
    [InlineData(@"'C:\Users\alice' を開けません", @"'C:\Users\<USER>' を開けません")]
    [InlineData("\"C:\\Users\\alice\"", "\"C:\\Users\\<USER>\"")]
    [InlineData(@"「C:\Users\alice」", @"「C:\Users\<USER>」")]
    [InlineData(@"C:\Users\alice", @"C:\Users\<USER>")]
    [InlineData(@"C:\Users\ALICE\x", @"C:\Users\<USER>\x")]
    [InlineData("C:/Users/alice/x", "C:/Users/<USER>/x")]
    public void MaskUserNameElements_FullPathElement_IsMasked(string input, string expected) =>
        Assert.Equal(expected, Mask(input, "alice"));

    [Fact]
    public void MaskUserNameElements_ThreeCharName_IsMasked() =>
        Assert.Equal(@"C:\Users\<USER>\a.zip", Mask(@"C:\Users\bob\a.zip", "bob"));

    [Theory]
    [InlineData(@"C:\Users\alice2\a.zip")]
    [InlineData(@"C:\Users\alice.txt")]
    [InlineData(@"C:\Users\malice\a.zip")]
    [InlineData(@"alice を開けません")]
    public void MaskUserNameElements_OtherElements_AreUnchanged(string input) =>
        Assert.Equal(input, Mask(input, "alice"));

    [Fact]
    public void MaskUserNameElements_UserNamedUser_KeepsUsersFolder() =>
        Assert.Equal(@"C:\Users\", Mask(@"C:\Users\", "User"));

    [Fact]
    public void MaskForDisplay_RegisteredSecret_BecomesStars()
    {
        using var scope = Logger.RegisterRedactionToken("s3cret-token");
        Assert.Equal("failed: *** here", Logger.MaskForDisplay("failed: s3cret-token here"));
    }
}
