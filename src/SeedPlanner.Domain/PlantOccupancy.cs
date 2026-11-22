namespace SeedPlanner.Domain;

/// <summary>When a variety takes its place in a bed or pot (see <see cref="OccupancyMask"/>).</summary>
public static class PlantOccupancy
{
    /// <summary>Mid-May: third 13 of the year.</summary>
    public const int DefaultLastFrostThird = 13;

    /// <summary>
    /// Perennials take the place all year. Everything else (annual, biennial grown as an annual crop, unknown) from the
    /// start to the end of the harvest (flowers: of the flowering time). Start: direct sowing = the sowing window; started indoors = sowing
    /// plus the weeks indoors, and for frost sensitive plants not before the last frost. Without the needed times the
    /// result is 0: the variety is not distributed until they are added.
    /// </summary>
    public static ulong Slots(Plant p, Lifecycle? life, int lastFrostThird = DefaultLastFrostThird)
    {
        if (life is Lifecycle.Perennial) return OccupancyMask.AllYear;

        var sowings = p.SeasonWindows.Where(w => w.Kind == WindowKind.Sowing).ToList();
        var ends = p.SeasonWindows.Where(w => w.Kind == WindowKind.Harvest).ToList();
        if (ends.Count == 0) ends = p.SeasonWindows.Where(w => w.Kind == WindowKind.Bloom).ToList();
        if (sowings.Count == 0 || ends.Count == 0) return 0;

        if (StartOf(p, sowings.Min(w => w.StartThird), lastFrostThird) is not { } start) return 0;
        return OccupancyMask.Range(start, EndOf(ends, start));
    }

    // The place is taken from the day of planting out: direct sowing = the sowing date; started indoors = sowing
    // plus the weeks indoors, and for frost sensitive plants not before the last frost. Empty without the weeks indoors.
    private static int? StartOf(Plant p, int sowThird, int lastFrostThird)
    {
        var start = sowThird;
        if (p.SowingMethod == SowingMethod.StartIndoors)
        {
            var weeks = p.IndoorWeeksMax ?? p.IndoorWeeksMin;
            if (weeks is null) return null;
            // A third of a month is about ten days
            start += (int)Math.Round(weeks.Value * 7 / 10.0);
            // Sowing in December plus the weeks indoors ends up in the next year: wrap before comparing with the frost date
            start %= OccupancyMask.Count;
            if (p.FrostSensitive) start = Math.Max(start, lastFrostThird);
        }
        return start % OccupancyMask.Count;
    }

    /// <summary>The third of the year in which the plant takes its place when it is sown in the given third: sowing date for direct sowing, planting out for plants started indoors. Empty without the weeks indoors.</summary>
    public static int? StartThird(Plant p, int sowThird, int lastFrostThird) => StartOf(p, sowThird, lastFrostThird);

    // The end is the nearest end of a harvest window after the start
    private static int EndOf(List<SeasonWindow> ends, int start) =>
        ends.Select(w => w.EndThird)
            .OrderBy(e => (e - start + OccupancyMask.Count) % OccupancyMask.Count)
            .First();

    /// <summary>
    /// The times the variety may take its place when the sowing is moved within the sowing window, earliest first
    /// (the end of the harvest stays as entered, so the later the sowing the shorter the time). Every entry is a mask like
    /// <see cref="Slots"/>. With <see cref="SowingTiming.Start"/>, <see cref="SowingTiming.Middle"/> and
    /// <see cref="SowingTiming.End"/> there is exactly one entry; with <see cref="SowingTiming.Auto"/> all the possible
    /// ones, the first is the same as with Start. Perennials and varieties without the needed times have one entry.
    /// </summary>
    public static List<ulong> SlotOptions(Plant p, Lifecycle? life, int lastFrostThird, SowingTiming timing)
    {
        var baseMask = Slots(p, life, lastFrostThird);
        if (baseMask == 0 || baseMask == OccupancyMask.AllYear || timing == SowingTiming.Start) return [baseMask];

        var window = p.SeasonWindows.Where(w => w.Kind == WindowKind.Sowing).OrderBy(w => w.StartThird).First();
        var ends = p.SeasonWindows.Where(w => w.Kind == WindowKind.Harvest).ToList();
        if (ends.Count == 0) ends = p.SeasonWindows.Where(w => w.Kind == WindowKind.Bloom).ToList();

        var baseStart = StartOf(p, window.StartThird, lastFrostThird)!.Value;
        var end = EndOf(ends, baseStart);
        var latest = (end - baseStart + OccupancyMask.Count) % OccupancyMask.Count;
        var length = (window.EndThird - window.StartThird + OccupancyMask.Count) % OccupancyMask.Count;

        var options = new List<ulong>();
        for (var k = 0; k <= length; k++)
        {
            if (StartOf(p, (window.StartThird + k) % OccupancyMask.Count, lastFrostThird) is not { } start) continue;
            // A sowing after the end of the harvest leaves no time
            if ((start - baseStart + OccupancyMask.Count) % OccupancyMask.Count > latest) continue;
            var mask = OccupancyMask.Range(start, end);
            if (!options.Contains(mask)) options.Add(mask);
        }
        if (options.Count == 0) options.Add(baseMask);

        return timing switch
        {
            SowingTiming.Middle => [options[(options.Count - 1) / 2]],
            SowingTiming.End => [options[^1]],
            _ => options
        };
    }
}
