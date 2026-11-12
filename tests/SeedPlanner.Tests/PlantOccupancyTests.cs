using SeedPlanner.Domain;
using Xunit;

namespace SeedPlanner.Tests;

public class PlantOccupancyTests
{
    private static SeasonWindow Window(WindowKind kind, int from, int to) =>
        new() { Kind = kind, StartMonth = from, EndMonth = to };

    private static Plant Make(SowingMethod? method, int sowFrom, int sowTo, int harvestFrom, int harvestTo,
        int? weeks = null, bool frost = false, WindowKind endKind = WindowKind.Harvest)
    {
        var p = new Plant { Name = "P", SowingMethod = method, IndoorWeeksMax = weeks, FrostSensitive = frost };
        p.SeasonWindows.Add(Window(WindowKind.Sowing, sowFrom, sowTo));
        p.SeasonWindows.Add(Window(endKind, harvestFrom, harvestTo));
        return p;
    }

    [Fact]
    public void Direct_sowing_starts_with_the_sowing_window_and_ends_with_the_harvest()
    {
        var p = Make(SowingMethod.DirectSow, 4, 5, 7, 9);

        Assert.Equal(OccupancyMask.Range(9, 26), PlantOccupancy.Slots(p, Lifecycle.Annual));
    }

    [Fact]
    public void Plants_started_indoors_start_after_the_weeks_indoors()
    {
        // Sowing from March (third 6) + 6 weeks (4 thirds) = third 10; harvest until the end of October (29)
        var p = Make(SowingMethod.StartIndoors, 3, 3, 8, 10, weeks: 6);

        Assert.Equal(OccupancyMask.Range(10, 29), PlantOccupancy.Slots(p, Lifecycle.Annual));
    }

    [Fact]
    public void Frost_sensitive_plants_wait_for_the_last_frost()
    {
        var p = Make(SowingMethod.StartIndoors, 3, 3, 8, 10, weeks: 6, frost: true);

        Assert.Equal(OccupancyMask.Range(13, 29), PlantOccupancy.Slots(p, Lifecycle.Annual, lastFrostThird: 13));
        Assert.Equal(OccupancyMask.Range(16, 29), PlantOccupancy.Slots(p, Lifecycle.Annual, lastFrostThird: 16));
    }

    [Fact]
    public void Missing_weeks_for_plants_started_indoors_give_no_times()
    {
        var p = Make(SowingMethod.StartIndoors, 3, 3, 8, 10);

        Assert.Equal(0UL, PlantOccupancy.Slots(p, Lifecycle.Annual));
    }

    [Fact]
    public void Missing_harvest_gives_no_times()
    {
        var p = new Plant { Name = "P", SowingMethod = SowingMethod.DirectSow };
        p.SeasonWindows.Add(Window(WindowKind.Sowing, 4, 5));

        Assert.Equal(0UL, PlantOccupancy.Slots(p, null));
    }

    [Fact]
    public void Flowers_end_with_the_bloom_window()
    {
        var p = Make(SowingMethod.DirectSow, 4, 5, 7, 9, endKind: WindowKind.Bloom);

        Assert.Equal(OccupancyMask.Range(9, 26), PlantOccupancy.Slots(p, Lifecycle.Annual));
    }

    [Fact]
    public void Perennials_take_the_place_all_year_even_without_times()
    {
        Assert.Equal(OccupancyMask.AllYear, PlantOccupancy.Slots(new Plant { Name = "P" }, Lifecycle.Perennial));
    }
}
