namespace SeedPlanner.Domain;

/// <summary>How full the pots and beds are after a distribution.</summary>
public static class AllocationStats
{
    /// <summary>Number of bands: 0 = empty, 1 = up to 25 %, 2 = up to 50 %, 3 = up to 75 %, 4 = up to 100 %.</summary>
    public const int BandCount = 5;

    /// <summary>The band of a pot or bed by the share of its capacity that is used at the busiest time of the year.</summary>
    public static int BandOf(ContainerResult c)
    {
        if (c.Placements.Count == 0) return 0;
        if (c.Capacity <= 0) return BandCount - 1;
        var share = c.Used / c.Capacity;
        if (share <= 0) return 0;
        const double eps = 1e-9;
        if (share <= 0.25 + eps) return 1;
        if (share <= 0.5 + eps) return 2;
        if (share <= 0.75 + eps) return 3;
        return 4;
    }
}
