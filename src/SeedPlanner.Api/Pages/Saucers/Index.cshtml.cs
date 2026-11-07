using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Saucers;

public class IndexModel(SeedPlannerDbContext db) : PageModel
{
    // PotCount: pots of the groups that are assigned to this saucer size
    public record Row(int Id, decimal DiameterCm, string? Color, int Quantity, int PotCount, string? Note);

    public List<Row> Rows { get; private set; } = new();
    public List<string> ColorOptions { get; private set; } = new();
    public int TotalSaucers { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(Name = "color", SupportsGet = true)]
    public string? ColorFilter { get; set; }

    // Column to sort by (diameter, color, quantity, pots, note) and direction (asc, desc)
    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Dir { get; set; }

    public async Task OnGetAsync()
    {
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);

        var saucers = await db.Saucers.AsNoTracking().ToListAsync();
        var potsPerSaucer = (await db.Pots.AsNoTracking()
                .Where(p => p.SaucerId != null)
                .Select(p => new { p.SaucerId, p.Quantity })
                .ToListAsync())
            .GroupBy(p => p.SaucerId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Quantity));

        ColorOptions = saucers
            .Select(s => s.Color)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(c => c, comparer)
            .ToList();

        var rows = saucers.Select(s => new Row(
            s.Id, s.DiameterCm, s.Color, s.Quantity,
            potsPerSaucer.GetValueOrDefault(s.Id), s.Note));

        if (!string.IsNullOrEmpty(ColorFilter))
            rows = rows.Where(r => string.Equals(r.Color, ColorFilter, StringComparison.CurrentCultureIgnoreCase));
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            rows = rows.Where(r =>
                (r.Color?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                (r.Note?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                r.DiameterCm.ToString("0.#", CultureInfo.CurrentCulture).Contains(q));
        }

        var byDefault = rows.OrderBy(r => r.DiameterCm).ThenBy(r => r.Color, comparer).ToList();
        TotalSaucers = byDefault.Sum(r => r.Quantity);

        var desc = Descending;
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

    // ---------------------------------------------------------------- sort links

    public bool Descending => string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase);

    private bool SortedBy(string column) =>
        string.Equals(Sort ?? "", column, StringComparison.OrdinalIgnoreCase);

    public string Arrow(string column) => SortedBy(column) ? (Descending ? " ▼" : " ▲") : "";

    public Dictionary<string, string?> SortRoute(string column) => new()
    {
        ["q"] = Q,
        ["color"] = ColorFilter,
        ["sort"] = column,
        ["dir"] = SortedBy(column) && !Descending ? "desc" : "asc"
    };
}
