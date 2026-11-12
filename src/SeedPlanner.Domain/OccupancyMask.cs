namespace SeedPlanner.Domain;

/// <summary>
/// The time a plant takes up its place, as a set of thirds of months (36 bits, bit 0 = early January ...
/// bit 35 = late December). Two plants can share a bed or pot space only if their masks do not overlap.
/// </summary>
public static class OccupancyMask
{
    public const int Count = 36;

    /// <summary>The whole year (perennials, things already standing in a container).</summary>
    public const ulong AllYear = (1UL << Count) - 1;

    /// <summary>From the first to the last third, inclusive; wraps over the new year if the end is before the start.</summary>
    public static ulong Range(int startThird, int endThird)
    {
        ulong mask = 0;
        var s = ((startThird % Count) + Count) % Count;
        var e = ((endThird % Count) + Count) % Count;
        for (var i = s; ; i = (i + 1) % Count)
        {
            mask |= 1UL << i;
            if (i == e) break;
        }
        return mask;
    }

    public static bool Overlaps(ulong a, ulong b) => (a & b) != 0;

    public static int Bits(ulong mask) => System.Numerics.BitOperations.PopCount(mask);

    /// <summary>The first contiguous range of the mask (for display). False for an empty or full-year mask.</summary>
    public static bool TryGetRange(ulong mask, out int startThird, out int endThird)
    {
        startThird = endThird = 0;
        if (mask == 0 || mask == AllYear) return false;
        for (var i = 0; i < Count; i++)
        {
            var prev = (i + Count - 1) % Count;
            if ((mask & (1UL << i)) == 0 || (mask & (1UL << prev)) != 0) continue;
            startThird = i;
            var j = i;
            while ((mask & (1UL << ((j + 1) % Count))) != 0) j++;
            endThird = j % Count;
            return true;
        }
        return false;
    }
}
