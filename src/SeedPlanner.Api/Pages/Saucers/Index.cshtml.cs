using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Saucers;

public class IndexModel(SeedPlannerDbContext db, IStringLocalizer<SharedResource> L) : PageModel
{
    // PotCount: pots of the groups that are assigned to this saucer size
    public record Row(int Id, decimal DiameterCm, string? Color, int Quantity, int PotCount, string? Note);

    public List<Row> Rows { get; private set; } = new();
    public int TotalSaucers { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>Search, filters, chips and the addresses of the column titles.</summary>
    public ListFilterView Filter { get; private set; } = null!;

    // Column to sort by (diameter, color, quantity, pots, note) and direction (asc, desc)
    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Dir { get; set; }

    public async Task OnGetAsync()
    {
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);

        var saucers = await db.Saucers.AsNoTracking().ToListAsync();
        // Every pot belongs to a group; the group knows its saucer
        var potsPerSaucer = (await db.PotGroups.AsNoTracking()
                .Where(g => g.SaucerId != null)
                .Select(g => new { g.SaucerId, Count = g.Pots.Count })
                .ToListAsync())
            .GroupBy(g => g.SaucerId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

        var rows = saucers.Select(s => new Row(
            s.Id, s.DiameterCm, s.Color, s.Quantity,
            potsPerSaucer.GetValueOrDefault(s.Id), s.Note)).ToList();

        var colors = rows.Select(r => r.Color).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(c => c, comparer)
            .Select(c => (Value: c, Label: c)).ToList();

        var facets = new List<Facet<Row>>
        {
            new("diameter", "Filter_Group_Properties", L["Filter_Diameter"].Value, r => new[] { r.DiameterCm.ToString("0.#", CultureInfo.InvariantCulture) },
                rows.Select(r => r.DiameterCm).Distinct().OrderBy(d => d)
                    .Select(d => (Value: d.ToString("0.#", CultureInfo.InvariantCulture), Label: d.ToString("0.#", CultureInfo.CurrentCulture) + " cm")).ToList()),
            new("color", "Filter_Group_Properties", L["Col_Color"].Value, r => FilterOptions.One(r.Color), colors),
            new("inuse", "Filter_Group_Occupancy", L["Col_InUse"].Value, r => new[] { r.PotCount > 0 ? "used" : "free" },
                new List<(string Value, string Label)> { ("free", L["Filter_Unused"].Value), ("used", L["Filter_Used"].Value) }),
        };

        var filter = new ListFilter<Row>(Request, Q, Sort, Dir);
        Filter = filter;
        rows = filter.Apply(rows, facets, (r, q) =>
            (r.Color?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
            (r.Note?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
            r.DiameterCm.ToString("0.#", CultureInfo.CurrentCulture).Contains(q));

        var byDefault = rows.OrderBy(r => r.DiameterCm).ThenBy(r => r.Color, comparer).ToList();
        TotalSaucers = byDefault.Sum(r => r.Quantity);

        var desc = Filter.Descending;
        Rows = (Sort?.ToLowerInvariant()) switch
        {
            "diameter" => ListSorting.Sort(byDefault, r => r.DiameterCm, desc),
            "color" => ListSorting.Sort(byDefault, r => r.Color, desc),
            "quantity" => ListSorting.Sort(byDefault, r => r.Quantity, desc),
            "pots" => ListSorting.Sort(byDefault, r => r.PotCount, desc),
            "note" => ListSorting.Sort(byDefault, r => r.Note, desc),
            _ => byDefault
        };
    }
}
