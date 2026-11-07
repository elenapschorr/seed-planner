using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.RaisedBeds;

public class IndexModel(SeedPlannerDbContext db, IStringLocalizer<SharedResource> L) : PageModel
{
    public record Row(
        int Id,
        string Name,
        BedShape Shape,
        decimal WidthCm,
        decimal LengthCm,
        decimal HeightCm,
        double AreaSquareMeters,
        double VolumeLiters,
        string? Note);

    public List<Row> Rows { get; private set; } = new();
    public double TotalArea { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(Name = "shape", SupportsGet = true)]
    public BedShape? ShapeFilter { get; set; }

    // Column to sort by (name, shape, width, length, height, area, volume, note) and direction (asc, desc)
    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Dir { get; set; }

    public async Task OnGetAsync()
    {
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);

        var beds = await db.RaisedBeds.AsNoTracking().ToListAsync();
        var rows = beds.Select(b => new Row(
            b.Id, b.Name, b.Shape, b.WidthCm, b.LengthCm, b.HeightCm,
            b.AreaSquareMeters, b.VolumeLiters, b.Note));

        if (ShapeFilter is not null) rows = rows.Where(r => r.Shape == ShapeFilter);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            rows = rows.Where(r =>
                r.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                (r.Note?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false));
        }

        var byDefault = rows.OrderBy(r => r.Name, comparer).ToList();
        TotalArea = byDefault.Sum(r => r.AreaSquareMeters);

        var desc = Descending;
        Rows = (Sort?.ToLowerInvariant()) switch
        {
            "name" => ListSorting.Sort(byDefault, r => r.Name, desc),
            "shape" => ListSorting.Sort(byDefault, r => L["BedShape_" + r.Shape].Value, desc),
            "width" => ListSorting.Sort(byDefault, r => r.WidthCm, desc),
            "length" => ListSorting.Sort(byDefault, r => r.LengthCm, desc),
            "height" => ListSorting.Sort(byDefault, r => r.HeightCm, desc),
            "area" => ListSorting.Sort(byDefault, r => r.AreaSquareMeters, desc),
            "volume" => ListSorting.Sort(byDefault, r => r.VolumeLiters, desc),
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
        ["shape"] = ShapeFilter?.ToString(),
        ["sort"] = column,
        ["dir"] = SortedBy(column) && !Descending ? "desc" : "asc"
    };
}
