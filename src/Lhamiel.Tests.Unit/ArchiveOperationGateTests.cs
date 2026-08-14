using Lhamiel.Util;
using Xunit;

namespace Lhamiel.Tests.Unit;

[Collection("Sequential")]
public class ArchiveOperationGateTests
{
    [Fact]
    public async Task ArchiveOperationGate_SecondOperationWaitsForFirst()
    {
        var first = await ArchiveOperationGate.EnterAsync(TestContext.Current.CancellationToken);
        try
        {
            var secondTask = ArchiveOperationGate.EnterAsync(TestContext.Current.CancellationToken);

            Assert.False(secondTask.IsCompleted);

            first.Dispose();
            using var second = await secondTask.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        }
        finally
        {
            first.Dispose();
        }
    }

    [Fact]
    public async Task ExtractionDestinationGate_SamePathWaitsForFirst()
    {
        var destination = Path.Combine(Path.GetTempPath(), $"lhamiel_destination_{Guid.NewGuid():N}");
        var first = await ExtractionDestinationGate.EnterAsync(
            destination,
            TestContext.Current.CancellationToken);
        try
        {
            var secondTask = ExtractionDestinationGate.EnterAsync(
                destination,
                TestContext.Current.CancellationToken);

            Assert.False(secondTask.IsCompleted);

            first.Dispose();
            using var second = await secondTask.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        }
        finally
        {
            first.Dispose();
        }
    }

    [Fact]
    public async Task ExtractionDestinationGate_DifferentPathsCanProceedConcurrently()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"lhamiel_destinations_{Guid.NewGuid():N}");
        using var first = await ExtractionDestinationGate.EnterAsync(
            Path.Combine(testRoot, "first"),
            TestContext.Current.CancellationToken);

        using var second = await ExtractionDestinationGate.EnterAsync(
            Path.Combine(testRoot, "second"),
            TestContext.Current.CancellationToken).WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
    }
}
