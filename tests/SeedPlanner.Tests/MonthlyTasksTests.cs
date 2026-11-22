using SeedPlanner.Domain;
using Xunit;

namespace SeedPlanner.Tests;

public class MonthlyTasksTests
{
    private static Plant Make(string name, SowingMethod? method, int sowFrom, int sowTo, int harvestFrom, int harvestTo,
        int? weeks = null, bool frost = false, WindowKind endKind = WindowKind.Harvest)
    {
        var p = new Plant { Name = name, SowingMethod = method, IndoorWeeksMax = weeks, FrostSensitive = frost };
        p.SeasonWindows.Add(new SeasonWindow { Kind = WindowKind.Sowing, StartMonth = sowFrom, EndMonth = sowTo });
        p.SeasonWindows.Add(new SeasonWindow { Kind = endKind, StartMonth = harvestFrom, EndMonth = harvestTo });
        return p;
    }

    private static List<MonthTask> Tasks(Plant p, int month, SowingTiming timing = SowingTiming.Start, int plants = 4) =>
        MonthlyTasks.For([new TaskSource(p, plants, timing)], month);

    [Fact]
    public void Direct_sowing_is_a_job_in_the_first_month_of_the_window()
    {
        var p = Make("Carrot", SowingMethod.DirectSow, 4, 5, 7, 9);

        var task = Assert.Single(Tasks(p, 4), t => t.Kind == TaskKind.Sow);
        Assert.Equal(MonthPart.Early, task.Part);
        Assert.Equal(4, task.Plants);
        Assert.DoesNotContain(Tasks(p, 5), t => t.Kind == TaskKind.Sow);
    }

    [Fact]
    public void Timing_moves_the_sowing_within_the_window()
    {
        var p = Make("Carrot", SowingMethod.DirectSow, 4, 5, 7, 9);

        Assert.Contains(Tasks(p, 5, SowingTiming.End), t => t.Kind == TaskKind.Sow && t.Part == MonthPart.Late);
        Assert.DoesNotContain(Tasks(p, 4, SowingTiming.End), t => t.Kind == TaskKind.Sow);
    }

    [Fact]
    public void Plants_started_indoors_are_sown_indoors_and_planted_out_not_before_the_last_frost()
    {
        var p = Make("Tomato", SowingMethod.StartIndoors, 3, 3, 7, 9, weeks: 6, frost: true);

        Assert.Contains(Tasks(p, 3), t => t.Kind == TaskKind.SowIndoors && t.Part == MonthPart.Early);
        Assert.DoesNotContain(Tasks(p, 4), t => t.Kind == TaskKind.PlantOut);
        var plantOut = Assert.Single(Tasks(p, 5), t => t.Kind == TaskKind.PlantOut);
        Assert.Equal(MonthPart.Mid, plantOut.Part);
    }

    [Fact]
    public void Harvest_is_a_job_in_every_month_of_the_harvest_window()
    {
        var p = Make("Carrot", SowingMethod.DirectSow, 4, 5, 7, 9);

        Assert.Contains(Tasks(p, 8), t => t.Kind == TaskKind.Harvest && t.Part == MonthPart.Whole);
        Assert.DoesNotContain(Tasks(p, 10), t => t.Kind == TaskKind.Harvest);
    }

    [Fact]
    public void Flowers_without_a_harvest_window_use_the_bloom_window()
    {
        var p = Make("Dahlia", SowingMethod.DirectSow, 5, 5, 8, 10, endKind: WindowKind.Bloom);

        Assert.Contains(Tasks(p, 9), t => t.Kind == TaskKind.Harvest);
    }

    [Fact]
    public void Varieties_without_plants_have_no_jobs()
    {
        var p = Make("Carrot", SowingMethod.DirectSow, 4, 5, 7, 9);

        Assert.Empty(Tasks(p, 4, plants: 0));
    }

    [Fact]
    public void Jobs_are_sorted_by_kind_then_name()
    {
        var b = Make("Beet", SowingMethod.DirectSow, 4, 4, 8, 9);
        var a = Make("Arugula", SowingMethod.DirectSow, 4, 4, 8, 9);

        var tasks = MonthlyTasks.For([new TaskSource(b, 2, SowingTiming.Start), new TaskSource(a, 2, SowingTiming.Start)], 4);

        Assert.Equal(new[] { "Arugula", "Beet" }, tasks.Where(t => t.Kind == TaskKind.Sow).Select(t => t.Plant.Name).ToArray());
    }

    private static Plant InStock(int id, string name, int sowFrom, int sowTo, StockStatus stock = StockStatus.InStock)
    {
        var p = Make(name, SowingMethod.DirectSow, sowFrom, sowTo, 8, 9);
        p.Id = id;
        p.StockStatus = stock;
        return p;
    }

    [Fact]
    public void SowableIn_returns_varieties_in_stock_whose_sowing_window_contains_the_month()
    {
        var plants = new[]
        {
            InStock(1, "Radish", 4, 6),
            InStock(2, "Carrot", 5, 7),
            InStock(3, "Lettuce", 3, 4),
            InStock(4, "Beet", 4, 6, StockStatus.OutOfStock)
        };

        var names = MonthlyTasks.SowableIn(plants, 5).Select(p => p.Name).ToArray();

        Assert.Equal(new[] { "Carrot", "Radish" }, names);
    }

    [Fact]
    public void SowableIn_leaves_out_the_excluded_varieties()
    {
        var plants = new[] { InStock(1, "Radish", 4, 6), InStock(2, "Carrot", 5, 7) };

        var names = MonthlyTasks.SowableIn(plants, 5, new HashSet<int> { 1 }).Select(p => p.Name).ToArray();

        Assert.Equal(new[] { "Carrot" }, names);
    }

    private static List<MonthTask> Carried(Plant p, int month, TaskProgress? progress = null, int plants = 4) =>
        MonthlyTasks.For([new TaskSource(p, plants, SowingTiming.Start, 7, progress)], month, carryOver: true);

    [Fact]
    public void A_sowing_that_is_not_marked_stays_on_the_list_in_later_months()
    {
        var p = Make("Carrot", SowingMethod.DirectSow, 4, 5, 7, 9);

        var task = Assert.Single(Carried(p, 6), t => t.Kind == TaskKind.Sow);
        Assert.Equal(4, task.DueMonth);
        Assert.True(task.Overdue);
        Assert.Equal(7, task.LineId);
        Assert.DoesNotContain(Carried(p, 3), t => t.Kind == TaskKind.Sow);
    }

    [Fact]
    public void A_sowing_that_is_marked_is_only_shown_in_its_own_month()
    {
        var p = Make("Carrot", SowingMethod.DirectSow, 4, 5, 7, 9);
        var sown = new TaskProgress(true, false, 0, false);

        var task = Assert.Single(Carried(p, 4, sown), t => t.Kind == TaskKind.Sow);
        Assert.True(task.Done);
        Assert.DoesNotContain(Carried(p, 6, sown), t => t.Kind == TaskKind.Sow);
    }

    [Fact]
    public void Planting_out_stays_on_the_list_until_it_is_marked()
    {
        var p = Make("Tomato", SowingMethod.StartIndoors, 3, 3, 7, 9, weeks: 6, frost: true);

        Assert.Contains(Carried(p, 7), t => t.Kind == TaskKind.PlantOut && t.DueMonth == 5);
        Assert.DoesNotContain(Carried(p, 7, new TaskProgress(true, true, 0, false)), t => t.Kind == TaskKind.PlantOut);
    }

    [Fact]
    public void A_harvest_stays_on_the_list_after_the_window_until_it_is_finished()
    {
        var p = Make("Carrot", SowingMethod.DirectSow, 4, 5, 7, 9);

        var task = Assert.Single(Carried(p, 10, new TaskProgress(true, false, 2, false)), t => t.Kind == TaskKind.Harvest);
        Assert.Equal(9, task.DueMonth);
        Assert.Equal(2, task.Harvested);
        Assert.True(task.Overdue);
        Assert.DoesNotContain(MonthlyTasks.For([new TaskSource(p, 4, SowingTiming.Start)], 10), t => t.Kind == TaskKind.Harvest);
    }

    [Fact]
    public void A_harvest_is_finished_when_marked_or_when_all_plants_are_harvested()
    {
        var p = Make("Carrot", SowingMethod.DirectSow, 4, 5, 7, 9);

        Assert.DoesNotContain(Carried(p, 8, new TaskProgress(true, false, 1, true)), t => t.Kind == TaskKind.Harvest);
        Assert.DoesNotContain(Carried(p, 8, new TaskProgress(true, false, 4, false)), t => t.Kind == TaskKind.Harvest);
        Assert.Contains(Carried(p, 8, new TaskProgress(true, false, 3, false)), t => t.Kind == TaskKind.Harvest);
    }

    [Fact]
    public void A_plan_that_starts_in_autumn_carries_jobs_over_the_turn_of_the_year()
    {
        var p = Make("Garlic", SowingMethod.DirectSow, 10, 11, 7, 9);
        var sources = new[] { new TaskSource(p, 4, SowingTiming.Start, 1) };

        var january = MonthlyTasks.For(sources, 1, carryOver: true, firstMonth: 10);
        Assert.Contains(january, t => t.Kind == TaskKind.Sow && t.DueMonth == 10 && t.Overdue);
        Assert.DoesNotContain(MonthlyTasks.For(sources, 9, carryOver: true, firstMonth: 10), t => t.Kind == TaskKind.Sow && t.Overdue && t.DueMonth == 11);
    }
}
