using Lhamiel.View;
using Xunit;

namespace Lhamiel.Tests.Unit;

public sealed class SupportDialogTests
{
    [Fact]
    public void ProductId_MatchesSupportDatabaseSlug()
    {
        Assert.Equal("lhamiel", SupportDialog.ProductId);
    }

    [Fact]
    public void VerificationLayout_ReservesSpaceForCodeControls()
    {
        Assert.True(SupportDialog.CompactHeight >= SupportDialog.CompactMinHeight);
        Assert.True(SupportDialog.VerificationMinHeight >= SupportDialog.CompactMinHeight + 70);
        Assert.True(SupportDialog.VerificationHeight >= SupportDialog.VerificationMinHeight);
    }
}
