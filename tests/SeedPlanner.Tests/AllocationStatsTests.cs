using SeedPlanner.Domain;
using Xunit;

namespace SeedPlanner.Tests;

public class AllocationStatsTests
{
    private static ContainerResult Box(double capacity, double used, int placements) =>
        new(ContainerKind.Bed, 1, "B", false, capacity, used,
            Enumerable.Range(0, placements).Select(i => new PlacementResult(i, i, PlantingRole.Main, 1)).ToList());

    [Fact]
    public void Nothing_placed_is_band_0() => Assert.Equal(0, AllocationStats.BandOf(Box(2, 0, 0)));

    [Theory]
    [InlineData(0.1, 1)]
    [InlineData(0.5, 1)]   // exactly 25 %
    [InlineData(0.6, 2)]
    [InlineData(1.0, 2)]   // exactly 50 %
    [InlineData(1.2, 3)]
    [InlineData(1.5, 3)]   // exactly 75 %
    [InlineData(1.6, 4)]
    [InlineData(2.0, 4)]   // full
    public void Share_of_the_capacity_gives_the_band(double used, int band) =>
        Assert.Equal(band, AllocationStats.BandOf(Box(2, used, 1)));
}
