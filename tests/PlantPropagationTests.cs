using SeedPlanner.Domain;
using Xunit;

namespace SeedPlanner.Tests;

public class PlantPropagationTests
{
    [Fact]
    public void Open_pollinated_seed_that_is_out_of_stock_should_be_bought()
    {
        var plant = new Plant
        {
            Name = "Test",
            Propagation = Propagation.Seed,
            SeedType = SeedType.OpenPollinated,
            StockStatus = StockStatus.OutOfStock
        };

        Assert.True(plant.ShouldBuy);
    }

    [Theory]
    [InlineData(Propagation.Plant)]
    [InlineData(Propagation.Cutting)]
    public void Plants_and_cuttings_are_never_marked_to_buy(Propagation propagation)
    {
        var plant = new Plant { Name = "Test", Propagation = propagation, StockStatus = StockStatus.OutOfStock };

        Assert.False(plant.ShouldBuy);
    }

    [Fact]
    public void Seed_without_a_seed_type_is_not_marked_to_buy()
    {
        var plant = new Plant { Name = "Test", StockStatus = StockStatus.OutOfStock };

        Assert.Equal(Propagation.Seed, plant.Propagation);
        Assert.False(plant.ShouldBuy);
    }
}
