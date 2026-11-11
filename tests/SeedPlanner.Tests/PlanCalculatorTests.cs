using SeedPlanner.Domain;
using Xunit;

namespace SeedPlanner.Tests;

public class PlanCalculatorTests
{
    private static DemandRange Range(decimal? min, decimal? max, DemandUnit? unit = DemandUnit.Plants) => new(min, max, unit);

    // ---------------------------------------------------------------- demand per person

    [Theory]
    [InlineData(DemandLevel.Min, 50, 2)]
    [InlineData(DemandLevel.Middle, 50, 3)]
    [InlineData(DemandLevel.Max, 50, 4)]
    [InlineData(DemandLevel.Custom, 0, 2)]
    [InlineData(DemandLevel.Custom, 25, 2.5)]
    [InlineData(DemandLevel.Custom, 100, 4)]
    public void Level_picks_a_value_between_min_and_max(DemandLevel level, int percent, double expected)
    {
        Assert.Equal((decimal)expected, PlanCalculator.AmountPerPerson(Range(2, 4), level, percent));
    }

    [Fact]
    public void Custom_percent_is_limited_to_0_to_100()
    {
        Assert.Equal(4m, PlanCalculator.AmountPerPerson(Range(2, 4), DemandLevel.Custom, 250));
        Assert.Equal(2m, PlanCalculator.AmountPerPerson(Range(2, 4), DemandLevel.Custom, -10));
    }

    [Fact]
    public void With_only_one_bound_that_bound_is_used()
    {
        Assert.Equal(3m, PlanCalculator.AmountPerPerson(Range(3, null), DemandLevel.Max, 50));
        Assert.Equal(5m, PlanCalculator.AmountPerPerson(Range(null, 5), DemandLevel.Min, 50));
    }

    [Fact]
    public void Without_demand_there_is_no_amount()
    {
        Assert.Null(PlanCalculator.AmountPerPerson(Range(null, null), DemandLevel.Middle, 50));
    }

    [Fact]
    public void Swapped_bounds_are_tolerated()
    {
        Assert.Equal(2m, PlanCalculator.AmountPerPerson(Range(4, 2), DemandLevel.Min, 50));
    }

    // ---------------------------------------------------------------- unit conversion

    [Fact]
    public void Plants_are_rounded_up()
    {
        Assert.Equal(5, PlanCalculator.PlantsFor(4.2m, DemandUnit.Plants, null, null));
    }

    [Fact]
    public void Row_meters_use_the_plant_spacing()
    {
        // 3 m of row with a plant every 25 cm: 12 plants
        Assert.Equal(12, PlanCalculator.PlantsFor(3, DemandUnit.RowMeters, 40, 25));
    }

    [Fact]
    public void Row_meters_without_plant_spacing_cannot_be_converted()
    {
        Assert.Null(PlanCalculator.PlantsFor(3, DemandUnit.RowMeters, 40, null));
    }

    [Fact]
    public void Square_meters_use_row_and_plant_spacing()
    {
        // 1 m² with 40 x 25 cm per plant: 10 plants
        Assert.Equal(10, PlanCalculator.PlantsFor(1, DemandUnit.SquareMeters, 40, 25));
    }

    [Fact]
    public void Square_meters_with_one_spacing_assume_a_square_grid()
    {
        // 25 x 25 cm = 0.0625 m² per plant: 16 plants per m²
        Assert.Equal(16, PlanCalculator.PlantsFor(1, DemandUnit.SquareMeters, null, 25));
        Assert.Equal(16, PlanCalculator.PlantsFor(1, DemandUnit.SquareMeters, 25, null));
    }

    [Fact]
    public void Square_meters_without_any_spacing_cannot_be_converted()
    {
        Assert.Null(PlanCalculator.PlantsFor(1, DemandUnit.SquareMeters, null, null));
    }

    [Fact]
    public void No_demand_means_no_plants_even_without_spacing()
    {
        Assert.Equal(0, PlanCalculator.PlantsFor(0, DemandUnit.SquareMeters, null, null));
    }

    [Fact]
    public void Average_spacing_ignores_missing_values()
    {
        Assert.Equal(30, PlanCalculator.AverageSpacing(new int?[] { 25, null, 35 }));
        Assert.Null(PlanCalculator.AverageSpacing(new int?[] { null, null }));
    }

    // ---------------------------------------------------------------- spreading

    [Theory]
    [InlineData(10, 3, new[] { 4, 3, 3 })]
    [InlineData(6, 3, new[] { 2, 2, 2 })]
    [InlineData(2, 3, new[] { 1, 1, 0 })]
    [InlineData(0, 2, new[] { 0, 0 })]
    public void Plants_are_spread_evenly(int total, int parts, int[] expected)
    {
        var shares = PlanCalculator.DistributeEvenly(total, parts);
        Assert.Equal(expected, shares);
        Assert.Equal(total, shares.Sum());
    }

    [Fact]
    public void Spreading_over_no_varieties_gives_nothing()
    {
        Assert.Empty(PlanCalculator.DistributeEvenly(5, 0));
    }

    // ---------------------------------------------------------------- the whole plan

    private static Plant Variety(int id, StockStatus stock = StockStatus.InStock, int? row = 40, int? plant = 25) =>
        new() { Id = id, Name = "V" + id, StockStatus = stock, RowSpacingCm = row, PlantSpacingCm = plant };

    private static PlantType Crop(int id, decimal? min, decimal? max, DemandUnit? unit, params Plant[] varieties)
    {
        var type = new PlantType { Id = id, Name = "Crop" + id, DemandPerPersonMin = min, DemandPerPersonMax = max, DemandUnit = unit, Category = PlantCategory.FruitVegetable };
        foreach (var v in varieties)
        {
            v.PlantTypeId = id;
            type.Plants.Add(v);
        }
        return type;
    }

    private static readonly Dictionary<int, UserDemand> NoOwnDemand = new();

    [Fact]
    public void Plan_spreads_the_plants_of_a_crop_over_the_varieties_in_stock()
    {
        var plan = new Plan { Persons = 2, Level = DemandLevel.Middle };
        var tomatoes = Crop(1, 2, 4, DemandUnit.Plants, Variety(1), Variety(2), Variety(3));

        PlanCalculator.Rebuild(plan, new[] { tomatoes }, NoOwnDemand);

        var item = Assert.Single(plan.Items);
        Assert.Equal(6m, item.DemandAmount);          // 3 per person x 2 persons
        Assert.Equal(6, item.Plants);
        Assert.Equal(new[] { 2, 2, 2 }, item.Lines.Select(l => l.Plants));
    }

    [Fact]
    public void Out_of_stock_varieties_are_left_out_unless_the_setting_includes_them()
    {
        var crop = Crop(1, 4, 4, DemandUnit.Plants, Variety(1), Variety(2, StockStatus.OutOfStock));

        var without = new Plan { Persons = 1 };
        PlanCalculator.Rebuild(without, new[] { crop }, NoOwnDemand);
        Assert.Equal(new[] { 4, 0 }, without.Items[0].Lines.Select(l => l.Plants));
        Assert.Empty(without.ShoppingList);

        var with = new Plan { Persons = 1, IncludeOutOfStock = true };
        PlanCalculator.Rebuild(with, new[] { crop }, NoOwnDemand);
        Assert.Equal(new[] { 2, 2 }, with.Items[0].Lines.Select(l => l.Plants));
        Assert.Equal(2, Assert.Single(with.ShoppingList).PlantId);
    }

    [Fact]
    public void Own_demand_replaces_the_default_including_the_unit()
    {
        // Default 2 plants per person; own value: 1 m² of a crop with 25 x 25 cm spacing
        var crop = Crop(1, 2, 2, DemandUnit.Plants, Variety(1, row: 25, plant: 25));
        var own = new Dictionary<int, UserDemand>
        {
            [1] = new() { PlantTypeId = 1, Min = 1, Max = 1, Unit = DemandUnit.SquareMeters }
        };

        var plan = new Plan { Persons = 1 };
        PlanCalculator.Rebuild(plan, new[] { crop }, own);

        Assert.Equal(DemandUnit.SquareMeters, plan.Items[0].Unit);
        Assert.Equal(16, plan.Items[0].Plants);
    }

    [Fact]
    public void Missing_spacing_leaves_the_number_open_but_keeps_the_amount()
    {
        var crop = Crop(1, 1, 1, DemandUnit.SquareMeters, Variety(1, row: null, plant: null));

        var plan = new Plan { Persons = 3 };
        PlanCalculator.Rebuild(plan, new[] { crop }, NoOwnDemand);

        var item = Assert.Single(plan.Items);
        Assert.Equal(3m, item.DemandAmount);
        Assert.Null(item.CalculatedPlants);
        Assert.True(item.NeedsNumber);
        Assert.Equal(0, item.Plants);
    }

    [Fact]
    public void Own_number_of_a_crop_is_spread_over_the_varieties()
    {
        var crop = Crop(1, 2, 2, DemandUnit.Plants, Variety(1), Variety(2));
        var plan = new Plan { Persons = 1 };
        PlanCalculator.Rebuild(plan, new[] { crop }, NoOwnDemand);

        plan.Items[0].PlantsOverride = 9;
        plan.Items[0].Spread();

        Assert.Equal(new[] { 5, 4 }, plan.Items[0].Lines.Select(l => l.Plants));
    }

    [Fact]
    public void Own_number_of_a_variety_is_kept_and_the_rest_goes_to_the_others()
    {
        var crop = Crop(1, 10, 10, DemandUnit.Plants, Variety(1), Variety(2), Variety(3));
        var plan = new Plan { Persons = 1 };
        PlanCalculator.Rebuild(plan, new[] { crop }, NoOwnDemand);

        plan.Items[0].Lines[0].PlantsOverride = 6;
        plan.Items[0].Spread();

        Assert.Equal(new[] { 6, 2, 2 }, plan.Items[0].Lines.Select(l => l.Plants));
    }

    [Fact]
    public void Rebuilding_keeps_the_choices_of_the_user()
    {
        var crop = Crop(1, 4, 4, DemandUnit.Plants, Variety(1), Variety(2));
        var plan = new Plan { Persons = 1 };
        PlanCalculator.Rebuild(plan, new[] { crop }, NoOwnDemand);
        plan.Items[0].Lines[1].Include = false;
        plan.Items[0].Lines[0].PlantsOverride = 3;

        plan.Persons = 2;   // settings changed, results are calculated again
        PlanCalculator.Rebuild(plan, new[] { crop }, NoOwnDemand);

        var item = Assert.Single(plan.Items);
        Assert.False(item.Lines[1].Include);
        Assert.Equal(3, item.Lines[0].PlantsOverride);
        Assert.Equal(8m, item.DemandAmount);
    }

    [Fact]
    public void Rebuilding_adds_new_varieties_and_drops_retired_ones()
    {
        var first = Variety(1);
        var crop = Crop(1, 4, 4, DemandUnit.Plants, first);
        var plan = new Plan { Persons = 1 };
        PlanCalculator.Rebuild(plan, new[] { crop }, NoOwnDemand);

        first.StockStatus = StockStatus.Retired;
        var added = Variety(2);
        added.PlantTypeId = 1;
        crop.Plants.Add(added);
        PlanCalculator.Rebuild(plan, new[] { crop }, NoOwnDemand);

        Assert.Equal(2, Assert.Single(plan.Items[0].Lines).PlantId);
    }

    [Fact]
    public void Crops_without_demand_flowers_and_crops_without_varieties_are_not_planned()
    {
        var noDemand = Crop(1, null, null, null, Variety(1));
        var noVariety = Crop(2, 2, 2, DemandUnit.Plants);
        var flower = Crop(3, 2, 2, DemandUnit.Plants, Variety(3));
        flower.Category = PlantCategory.Flower;
        var noUnit = Crop(4, 2, 2, null, Variety(4));

        var plan = new Plan { Persons = 1 };
        PlanCalculator.Rebuild(plan, new[] { noDemand, noVariety, flower, noUnit }, NoOwnDemand);

        Assert.Empty(plan.Items);
    }

    [Fact]
    public void Spacing_is_averaged_over_the_varieties_that_are_used()
    {
        // The variety in stock has 20 cm, the one out of stock 40 cm; only the first one counts
        var crop = Crop(1, 3, 3, DemandUnit.RowMeters, Variety(1, plant: 20), Variety(2, StockStatus.OutOfStock, plant: 40));

        var plan = new Plan { Persons = 1 };
        PlanCalculator.Rebuild(plan, new[] { crop }, NoOwnDemand);

        Assert.Equal(15, plan.Items[0].CalculatedPlants);   // 3 m / 0.20 m
    }

    // ---------------------------------------------------------------- selection of crops and varieties

    [Fact]
    public void A_crop_is_fully_partly_or_not_included_depending_on_its_varieties()
    {
        var item = new PlanItem();
        item.Lines.Add(new PlanLine { Include = true });
        item.Lines.Add(new PlanLine { Include = false });

        Assert.True(item.IsIncluded);
        Assert.True(item.IsPartlyIncluded);
        Assert.False(item.IsFullyIncluded);

        item.Lines[1].Include = true;
        Assert.True(item.IsFullyIncluded);
        Assert.False(item.IsPartlyIncluded);

        item.Lines[0].Include = false;
        item.Lines[1].Include = false;
        Assert.False(item.IsIncluded);
        Assert.False(item.IsPartlyIncluded);
        Assert.False(item.IsFullyIncluded);
    }

    [Fact]
    public void Only_the_selected_varieties_get_plants()
    {
        var item = new PlanItem { CalculatedPlants = 6 };
        item.Lines.Add(new PlanLine { Include = true });
        item.Lines.Add(new PlanLine { Include = false });
        item.Spread();

        Assert.Equal(6, item.Lines[0].CalculatedPlants);
        Assert.Equal(0, item.Lines[1].CalculatedPlants);
    }

    [Fact]
    public void A_crop_without_selected_varieties_gets_no_plants_and_no_shopping_list()
    {
        var item = new PlanItem { CalculatedPlants = 6 };
        item.Lines.Add(new PlanLine { Include = false, OutOfStock = true });
        item.Spread();

        var plan = new Plan();
        plan.Items.Add(item);
        Assert.Equal(0, item.Lines[0].Plants);
        Assert.Empty(plan.ShoppingList);
    }
}
