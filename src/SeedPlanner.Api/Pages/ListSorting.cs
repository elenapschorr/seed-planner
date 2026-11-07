using System.Globalization;
using SeedPlanner.Domain;

namespace SeedPlanner.Api.Pages;

// Helpers for the sortable list pages
public static class ListSorting
{
    // Sort order of the values: light < medium < heavy, sun < partial shade < shade
    public static int? Rank(NutrientDemand? value) => value switch
    {
        NutrientDemand.Light => 0,
        NutrientDemand.Medium => 1,
        NutrientDemand.Heavy => 2,
        _ => null
    };

    public static int? Rank(Exposure? value) => value switch
    {
        Exposure.FullSun => 0,
        Exposure.PartialShade => 1,
        Exposure.Shade => 2,
        _ => null
    };

    private static readonly Comparer<IComparable> KeyComparer = Comparer<IComparable>.Create((a, b) =>
        a is string x && b is string y
            ? string.Compare(x, y, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase)
            : a.CompareTo(b));

    // Sorts by the key; rows without a value (key is null) always come last.
    // The sort is stable, so rows are expected to be pre-sorted by name.
    public static List<T> Sort<T>(IEnumerable<T> rows, Func<T, IComparable?> key, bool descending)
    {
        var list = rows.ToList();
        var with = list.Where(r => key(r) is not null);
        var without = list.Where(r => key(r) is null);

        var sorted = descending
            ? with.OrderByDescending(r => key(r)!, KeyComparer)
            : with.OrderBy(r => key(r)!, KeyComparer);

        return sorted.Concat(without).ToList();
    }
}
