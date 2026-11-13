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

        var start = sowings.Min(w => w.StartThird);
        if (p.SowingMethod == SowingMethod.StartIndoors)
        {
            var weeks = p.IndoorWeeksMax ?? p.IndoorWeeksMin;
            if (weeks is null) return 0;
            // A third of a month is about ten days
            start += (int)Math.Round(weeks.Value * 7 / 10.0);
            // Sowing in December plus the weeks indoors ends up in the next year: wrap before comparing with the frost date
            start %= OccupancyMask.Count;
            if (p.FrostSensitive) start = Math.Max(start, lastFrostThird);
        }
        start %= OccupancyMask.Count;

        // The end is the nearest end of a harvest window after the start
        var end = ends.Select(w => w.EndThird)
            .OrderBy(e => (e - start + OccupancyMask.Count) % OccupancyMask.Count)
            .First();
        return OccupancyMask.Range(start, end);
    }
}
