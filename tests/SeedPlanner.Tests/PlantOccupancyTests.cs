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

    [Fact]
    public void Biennials_with_times_are_grown_like_annual_crops()
    {
        var p = Make(SowingMethod.DirectSow, 4, 5, 7, 9);

        Assert.Equal(OccupancyMask.Range(9, 26), PlantOccupancy.Slots(p, Lifecycle.Biennial));
    }

    [Fact]
    public void Biennials_without_times_are_not_distributed()
    {
        Assert.Equal(0UL, PlantOccupancy.Slots(new Plant { Name = "P" }, Lifecycle.Biennial));
    }

    [Fact]
    public void Frost_sensitive_plants_sown_in_winter_still_wait_for_the_last_frost()
    {
        // Sowing from December (third 33) + 10 weeks indoors wraps into February; frost sensitive: not before third 13
        var p = Make(SowingMethod.StartIndoors, 12, 2, 8, 10, weeks: 10, frost: true);

        Assert.Equal(OccupancyMask.Range(13, 29), PlantOccupancy.Slots(p, Lifecycle.Annual));
    }

    // ---------------------------------------------------------------- moving the sowing within its window

    [Fact]
    public void Sowing_at_the_start_of_the_window_is_the_normal_time()
    {
        var p = Make(SowingMethod.DirectSow, 4, 5, 7, 9);

        Assert.Equal(new[] { PlantOccupancy.Slots(p, Lifecycle.Annual) }, PlantOccupancy.SlotOptions(p, Lifecycle.Annual, 13, SowingTiming.Start));
    }

    [Fact]
    public void Sowing_in_the_middle_or_at_the_end_of_the_window_shortens_the_time_but_keeps_the_end_of_the_harvest()
    {
        // Sowing April to May = thirds 9..14, harvest until the end of September (26)
        var p = Make(SowingMethod.DirectSow, 4, 5, 7, 9);

        Assert.Equal(new[] { OccupancyMask.Range(11, 26) }, PlantOccupancy.SlotOptions(p, Lifecycle.Annual, 13, SowingTiming.Middle));
        Assert.Equal(new[] { OccupancyMask.Range(14, 26) }, PlantOccupancy.SlotOptions(p, Lifecycle.Annual, 13, SowingTiming.End));
    }

    [Fact]
    public void Automatic_timing_offers_every_start_in_the_window_earliest_first()
    {
        var p = Make(SowingMethod.DirectSow, 4, 5, 7, 9);

        var options = PlantOccupancy.SlotOptions(p, Lifecycle.Annual, 13, SowingTiming.Auto);

        Assert.Equal(6, options.Count);
        Assert.Equal(OccupancyMask.Range(9, 26), options[0]);
        Assert.Equal(OccupancyMask.Range(14, 26), options[^1]);
    }

    [Fact]
    public void Perennials_and_plants_without_times_have_only_one_option()
    {
        var perennial = Make(SowingMethod.DirectSow, 4, 5, 7, 9);
        Assert.Equal(new[] { OccupancyMask.AllYear }, PlantOccupancy.SlotOptions(perennial, Lifecycle.Perennial, 13, SowingTiming.Auto));

        var none = new Plant { Name = "P" };
        Assert.Equal(new[] { 0UL }, PlantOccupancy.SlotOptions(none, Lifecycle.Annual, 13, SowingTiming.Auto));
    }
}
