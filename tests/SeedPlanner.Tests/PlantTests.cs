using SeedPlanner.Domain;
using Xunit;

namespace SeedPlanner.Tests;

public class PlantTests
{
    private static Plant PlantWith(params SeasonWindow[] windows)
    {
        var plant = new Plant { Name = "Test" };
        plant.SeasonWindows.AddRange(windows);
        return plant;
    }

    private static SeasonWindow Sowing(int start, int end) =>
        new() { Kind = WindowKind.Sowing, StartMonth = start, EndMonth = end };

    private static SeasonWindow Harvest(int start, int end) =>
        new() { Kind = WindowKind.Harvest, StartMonth = start, EndMonth = end };

    [Fact]
    public void CanSowIn_MonthInsideSowingWindow()
    {
        var plant = PlantWith(Sowing(3, 5));
        Assert.True(plant.CanSowIn(4));
    }

    [Fact]
    public void CanSowIn_HarvestWindowDoesNotCount()
    {
        var plant = PlantWith(Sowing(3, 5), Harvest(7, 9));
        Assert.False(plant.CanSowIn(8));
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(9, true)]
    [InlineData(6, false)]
    public void CanSowIn_TwoSowingWindows(int month, bool expected)
    {
        var plant = PlantWith(Sowing(2, 3), Sowing(8, 9));
        Assert.Equal(expected, plant.CanSowIn(month));
    }

    [Fact]
    public void CanSowIn_WindowAcrossNewYear()
    {
        var plant = PlantWith(Sowing(11, 2));
        Assert.True(plant.CanSowIn(1));
        Assert.False(plant.CanSowIn(6));
    }

    [Fact]
    public void CanSowIn_NoWindows()
    {
        Assert.False(PlantWith().CanSowIn(5));
    }

    [Theory]
    [InlineData(StockStatus.OutOfStock, SeedType.OpenPollinated, true)]
    [InlineData(StockStatus.OutOfStock, SeedType.F1Hybrid, false)]
    [InlineData(StockStatus.InStock, SeedType.OpenPollinated, false)]
    [InlineData(StockStatus.Retired, SeedType.OpenPollinated, false)]
    public void ShouldBuy_OnlyOpenPollinatedOutOfStock(StockStatus stock, SeedType seedType, bool expected)
    {
        var plant = new Plant { Name = "Test", StockStatus = stock, SeedType = seedType };
        Assert.Equal(expected, plant.ShouldBuy);
    }
}
