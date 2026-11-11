using Microsoft.Extensions.Localization;

namespace SeedPlanner.Api.Pages;

public record FilterOption(string Value, string Label, int Count, bool Selected, string? Parent = null);

/// <summary>One filter in the panel: a title and its options. Several options of one filter are combined with OR.</summary>
/// <param name="ParentKey">Set for a sub-filter: its options are shown below the options of the parent filter.</param>
public record FilterField(string Key, string Group, string Label, List<FilterOption> Options, bool Scroll, string? ParentKey = null);

public record FilterChip(string Key, string Value, string Text);

/// <summary>
/// Describes one filter of a list: its key in the address, the group in the panel, the title,
/// which values an item has, and which options the panel offers.
/// </summary>
/// <param name="Normalize">Maps values from addresses of older versions to the current ones.</param>
/// <param name="ParentKey">Makes this a sub-filter of the filter with this key (e.g. plant types below categories).
/// Choosing an option of the parent alone means everything below it; choosing options below it narrows that down.</param>
/// <param name="ParentOf">For a sub-filter: the option of the parent filter that a value belongs to.</param>
public sealed record Facet<T>(
    string Key,
    string Group,
    string Label,
    Func<T, IEnumerable<string>> Values,
    List<(string Value, string Label)> Options,
    bool Scroll = false,
    Func<string, string>? Normalize = null,
    string? ParentKey = null,
    Func<string, string?>? ParentOf = null);

/// <summary>
/// What the shared filter panel (Pages/Shared/_FilterBar.cshtml) needs, plus the addresses of the
/// column titles. It does not depend on the kind of items in the list.
/// </summary>
public abstract class ListFilterView(HttpRequest request, string? q, string? sort, string? dir, string defaultSort)
{
    protected HttpRequest Request => request;
    public string Path => request.Path.Value ?? "";
    public string? Q => q;
    public string? Sort => sort;
    public string? Dir => dir;

    /// <summary>The filters that have at least one option to offer, in the order of the panel.</summary>
    public List<FilterField> Fields { get; protected set; } = new();

    /// <summary>The chosen options, shown as removable chips above the table.</summary>
    public List<FilterChip> Chips { get; protected set; } = new();

    public int ActiveCount => Chips.Count;

    /// <summary>The panel stays open while the user clicks through the options.</summary>
    public bool PanelOpen => request.Query["panel"] == "1";

    public bool Descending => string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

    private bool SortedBy(string column) =>
        string.Equals(sort ?? defaultSort, column, StringComparison.OrdinalIgnoreCase);

    /// <summary>Arrow behind the title of the column the list is sorted by.</summary>
    public string Arrow(string column) => SortedBy(column) ? (Descending ? " ▼" : " ▲") : "";

    // Address of the list with the given search, filters, sorting and panel state
    private string BuildUrl(IEnumerable<KeyValuePair<string, string>> filters, string? sortColumn, string? direction, bool panel)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(q)) parts.Add("q=" + Uri.EscapeDataString(q.Trim()));
        foreach (var (key, value) in filters) parts.Add(key + "=" + Uri.EscapeDataString(value));
        if (!string.IsNullOrEmpty(sortColumn)) parts.Add("sort=" + Uri.EscapeDataString(sortColumn));
        if (!string.IsNullOrEmpty(direction)) parts.Add("dir=" + Uri.EscapeDataString(direction));
        if (panel) parts.Add("panel=1");
        return Path + (parts.Count == 0 ? "" : "?" + string.Join("&", parts));
    }

    private IEnumerable<KeyValuePair<string, string>> ChosenFilters(FilterChip? except = null) =>
        Chips.Where(c => except is null || c.Key != except.Key || c.Value != except.Value)
             .Select(c => new KeyValuePair<string, string>(c.Key, c.Value));

    /// <summary>Title of a column: keeps search and filters; a second click reverses the direction.</summary>
    public string SortUrl(string column) =>
        BuildUrl(ChosenFilters(), column, SortedBy(column) && !Descending ? "desc" : "asc", panel: false);

    /// <summary>Chip: the same list without this one option.</summary>
    public string RemoveUrl(FilterChip chip) => BuildUrl(ChosenFilters(chip), sort, dir, panel: false);

    /// <summary>All filters removed (the search text stays).</summary>
    public string ClearUrl => BuildUrl(Array.Empty<KeyValuePair<string, string>>(), sort, dir, panel: false);

    /// <summary>The same list with the panel closed.</summary>
    public string ClosePanelUrl => BuildUrl(ChosenFilters(), sort, dir, panel: false);

    /// <summary>The list with its filters and sorting; an edit page returns here, so the filters are kept.</summary>
    public string ReturnUrl => ClosePanelUrl;
}

/// <summary>Search and filters of one list (several options of one filter = OR, different filters = AND).</summary>
public sealed class ListFilter<T>(HttpRequest request, string? q, string? sort, string? dir, string defaultSort = "")
    : ListFilterView(request, q, sort, dir, defaultSort)
{
    /// <summary>
    /// Keeps the items that match search and filters, and prepares panel and chips. Every option shows how many
    /// items would match if it were added to the other filters.
    /// </summary>
    public List<T> Apply(IReadOnlyList<T> items, IReadOnlyList<Facet<T>> facets, Func<T, string, bool>? searchMatch = null)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;

        var values = items
            .Select(item => facets.ToDictionary(f => f.Key, f => f.Values(item).ToHashSet(comparer)))
            .ToList();

        var selected = facets.ToDictionary(f => f.Key, f => ReadSelection(Request, f));

        var parents = facets.Where(f => f.ParentKey is not null).Select(f => f.ParentKey!).ToHashSet();

        var search = Q?.Trim();
        bool Matches(int index, string? exceptKey)
        {
            if (!string.IsNullOrEmpty(search) && searchMatch is not null && !searchMatch(items[index], search)) return false;
            foreach (var f in facets)
            {
                if (f.Key == exceptKey || f.ParentKey is not null || parents.Contains(f.Key)) continue;
                var chosen = selected[f.Key];
                if (chosen.Count > 0 && !values[index][f.Key].Overlaps(chosen)) return false;
            }
            foreach (var child in facets.Where(f => f.ParentKey is not null))
            {
                if (child.Key == exceptKey || child.ParentKey == exceptKey) continue;
                if (!MatchesPair(values[index], child, selected)) return false;
            }
            return true;
        }

        Fields = facets.Select(f => new FilterField(
                f.Key, f.Group, f.Label,
                f.Options.Select(o => new FilterOption(
                        o.Value, o.Label,
                        Enumerable.Range(0, items.Count).Count(i => Matches(i, f.Key) && values[i][f.Key].Contains(o.Value)),
                        selected[f.Key].Contains(o.Value),
                        f.ParentOf?.Invoke(o.Value)))
                    .Where(o => o.Count > 0 || o.Selected)
                    .ToList(),
                f.Scroll,
                f.ParentKey))
            .Where(f => f.Options.Count > 0)
            .ToList();

        Chips = facets.SelectMany(f => f.Options
                .Where(o => selected[f.Key].Contains(o.Value))
                .Select(o => new FilterChip(f.Key, o.Value, f.Label + ": " + o.Label)))
            .ToList();

        return Enumerable.Range(0, items.Count).Where(i => Matches(i, null)).Select(i => items[i]).ToList();
    }

    // A sub-filter and its parent act as one filter: an item matches if its own option below the parent is chosen,
    // or if its parent option is chosen and nothing below that parent option is chosen
    private static bool MatchesPair(Dictionary<string, HashSet<string>> item, Facet<T> child, Dictionary<string, HashSet<string>> selected)
    {
        var chosenParents = selected[child.ParentKey!];
        var chosenChildren = selected[child.Key];
        if (chosenParents.Count == 0 && chosenChildren.Count == 0) return true;
        if (item[child.Key].Overlaps(chosenChildren)) return true;
        return item[child.ParentKey!].Any(p =>
            chosenParents.Contains(p) && !chosenChildren.Any(c => string.Equals(child.ParentOf?.Invoke(c), p, StringComparison.OrdinalIgnoreCase)));
    }

    private static HashSet<string> ReadSelection(HttpRequest request, Facet<T> facet) =>
        request.Query.TryGetValue(facet.Key, out var chosen)
            ? chosen.OfType<string>()
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => facet.Normalize?.Invoke(v) ?? v)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

public static class FilterOptions
{
    /// <summary>All values of an enum, labelled with the resource "prefix + value".</summary>
    public static List<(string Value, string Label)> Enum<TEnum>(IStringLocalizer L, string prefix) where TEnum : struct, Enum =>
        System.Enum.GetValues<TEnum>().Select(v => (Value: v.ToString(), Label: L[prefix + v].Value)).ToList();

    /// <summary>The value of an optional enum as filter value (nothing if it is not set).</summary>
    public static IEnumerable<string> One(object? value) =>
        value is null ? Array.Empty<string>() : new[] { value.ToString()! };
}
