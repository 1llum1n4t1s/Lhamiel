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

}
