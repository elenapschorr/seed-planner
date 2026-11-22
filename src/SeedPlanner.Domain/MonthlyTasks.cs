namespace SeedPlanner.Domain;

public enum TaskKind
{
    /// <summary>Sow directly where the plant grows.</summary>
    Sow,
    /// <summary>Sow indoors (pots, trays) to plant out later.</summary>
    SowIndoors,
    /// <summary>Plant out what was started indoors.</summary>
    PlantOut,
    /// <summary>Harvest (flowers: the flowering time).</summary>
    Harvest
}

/// <summary>What the user has marked as done for a variety of a plan.</summary>
public record TaskProgress(bool Sown, bool PlantedOut, int HarvestedPlants, bool HarvestDone);

/// <summary>A variety of a plan with the number of plants, when within its sowing window it is sown, and what is marked as done.</summary>
public record TaskSource(Plant Plant, int Plants, SowingTiming Timing, int LineId = 0, TaskProgress? Progress = null);

/// <summary>
/// One job of a month. <see cref="Part"/> says in which part of the month (Whole: the whole month). <see cref="DueMonth"/> is the month the job
/// belongs to; <see cref="Overdue"/> means that month is before the month shown. A job that is <see cref="Done"/> is only shown in its own month.
/// </summary>
public record MonthTask(TaskKind Kind, Plant Plant, int Plants, MonthPart Part, int LineId = 0, int DueMonth = 0, bool Done = false, int Harvested = 0, bool Overdue = false);

/// <summary>What there is to do in a month for the varieties of a plan: sowing, planting out and harvesting.</summary>
public static class MonthlyTasks
{
    private const int ThirdsPerYear = OccupancyMask.Count;
    private const int ThirdsPerMonth = 3;

    /// <summary>
    /// The jobs of one month (1 to 12), sorted by kind, then by part of the month, then by name. Varieties without plants are left out.
    /// The sowing third follows <see cref="SowingTiming"/> within the first sowing window (Auto counts as Start); plants started
    /// indoors are planted out after the weeks indoors, frost sensitive ones not before the last frost (same rule as <see cref="PlantOccupancy"/>).
    /// With <paramref name="firstMonth"/> the months are ordered from the start of the plan (a plan from October: October is the first month). With <paramref name="carryOver"/> a job stays on the list in the following months until it is marked as done (a harvest until all plants
    /// are harvested or it is marked as finished); a job that is done is shown only in its own month. Without it, only the jobs of the month itself.
    /// </summary>
    public static List<MonthTask> For(IEnumerable<TaskSource> sources, int month, int lastFrostThird = PlantOccupancy.DefaultLastFrostThird, bool carryOver = false, int firstMonth = 1)
    {
        var order = new MonthOrder(firstMonth);
        var tasks = new List<(MonthTask Task, int Third)>();
        foreach (var source in sources.Where(s => s.Plants > 0))
        {
            AddSowingTasks(tasks, source, month, lastFrostThird, carryOver, order);
            AddHarvestTask(tasks, source, month, carryOver, order);
        }
        return tasks
            .OrderBy(t => t.Task.Kind).ThenBy(t => t.Third).ThenBy(t => t.Task.Plant.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(t => t.Task).ToList();
    }

    private static void AddSowingTasks(List<(MonthTask, int)> tasks, TaskSource source, int month, int lastFrostThird, bool carryOver, MonthOrder order)
    {
        var plant = source.Plant;
        var progress = source.Progress ?? new TaskProgress(false, false, 0, false);
        var window = plant.SeasonWindows.Where(w => w.Kind == WindowKind.Sowing).OrderBy(w => w.StartThird).FirstOrDefault();
        if (window is null) return;

        var sowThird = SowThird(window, source.Timing);
        var indoors = plant.SowingMethod == SowingMethod.StartIndoors;
        if (IsListed(MonthOf(sowThird), month, progress.Sown, carryOver, order))
            tasks.Add((new MonthTask(indoors ? TaskKind.SowIndoors : TaskKind.Sow, plant, source.Plants, PartOf(sowThird), source.LineId, MonthOf(sowThird), progress.Sown, 0, Overdue(MonthOf(sowThird), month, progress.Sown, order)), sowThird));

        if (indoors && PlantOccupancy.StartThird(plant, sowThird, lastFrostThird) is { } outThird && IsListed(MonthOf(outThird), month, progress.PlantedOut, carryOver, order))
            tasks.Add((new MonthTask(TaskKind.PlantOut, plant, source.Plants, PartOf(outThird), source.LineId, MonthOf(outThird), progress.PlantedOut, 0, Overdue(MonthOf(outThird), month, progress.PlantedOut, order)), outThird));
    }

    private static void AddHarvestTask(List<(MonthTask, int)> tasks, TaskSource source, int month, bool carryOver, MonthOrder order)
    {
        var plant = source.Plant;
        var progress = source.Progress ?? new TaskProgress(false, false, 0, false);
        var windows = plant.SeasonWindows.Where(w => w.Kind == WindowKind.Harvest).ToList();
        if (windows.Count == 0) windows = plant.SeasonWindows.Where(w => w.Kind == WindowKind.Bloom).ToList();

        var finished = progress.HarvestDone || progress.HarvestedPlants >= source.Plants;
        if (carryOver && finished) return;

        var due = month;
        if (!windows.Any(w => w.Contains(month)))
        {
            // After the window (in the same year) an unfinished harvest stays on the list
            var ended = carryOver ? windows.Where(w => order.Position(w.StartMonth) <= order.Position(w.EndMonth) && order.Position(w.EndMonth) < order.Position(month)).ToList() : [];
            if (ended.Count == 0) return;
            due = ended.OrderBy(w => order.Position(w.EndMonth)).Last().EndMonth;
        }
        tasks.Add((new MonthTask(TaskKind.Harvest, plant, source.Plants, MonthPart.Whole, source.LineId, due, false, progress.HarvestedPlants, due != month), 0));
    }

    // A job is listed in its own month; with carry over also later until it is done
    private static bool IsListed(int dueMonth, int shownMonth, bool done, bool carryOver, MonthOrder order) =>
        carryOver && !done ? order.Position(dueMonth) <= order.Position(shownMonth) : dueMonth == shownMonth;

    private static bool Overdue(int dueMonth, int shownMonth, bool done, MonthOrder order) =>
        !done && order.Position(dueMonth) < order.Position(shownMonth);

    /// <summary>The order of the months in a plan that starts in <c>firstMonth</c> (1 = January; a plan from October runs October ... September).</summary>
    private readonly record struct MonthOrder(int FirstMonth)
    {
        public int Position(int month) => (month - FirstMonth + 12) % 12;
    }

    /// <summary>
    /// The varieties that can be sown in the month by their sowing window and are in stock, sorted by name.
    /// The ones in <paramref name="exclude"/> (for example those that are already in the plan) are left out.
    /// </summary>
    public static List<Plant> SowableIn(IEnumerable<Plant> plants, int month, ISet<int>? exclude = null) =>
        plants
            .Where(p => p.StockStatus == StockStatus.InStock && exclude?.Contains(p.Id) != true)
            .Where(p => p.SeasonWindows.Any(w => w.Kind == WindowKind.Sowing && w.Contains(month)))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    // Start = first third of the window, End = last third, Middle = the one in between
    private static int SowThird(SeasonWindow window, SowingTiming timing)
    {
        var length = (window.EndThird - window.StartThird + ThirdsPerYear) % ThirdsPerYear;
        var shift = timing switch { SowingTiming.End => length, SowingTiming.Middle => length / 2, _ => 0 };
        return (window.StartThird + shift) % ThirdsPerYear;
    }

    private static int MonthOf(int third) => third / ThirdsPerMonth + 1;

    private static MonthPart PartOf(int third) => (third % ThirdsPerMonth) switch { 0 => MonthPart.Early, 1 => MonthPart.Mid, _ => MonthPart.Late };
}
