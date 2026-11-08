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
        BedKind Kind,
        BedShape Shape,
        decimal WidthCm,
        decimal LengthCm,
        decimal? HeightCm,
        double AreaSquareMeters,
        double? VolumeLiters,
        string Plantings,
        string? Note);

    public List<Row> Rows { get; private set; } = new();
    public double TotalArea { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(Name = "kind", SupportsGet = true)]
    public BedKind? KindFilter { get; set; }

    [BindProperty(Name = "shape", SupportsGet = true)]
    public BedShape? ShapeFilter { get; set; }

    // Column to sort by (name, kind, shape, width, length, height, area, volume, plantings, note) and direction (asc, desc)
    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Dir { get; set; }

    public async Task OnGetAsync()
    {
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);

        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        var beds = await db.RaisedBeds.AsNoTracking()
            .Include(b => b.Plantings).ThenInclude(x => x.PlantType).ThenInclude(t => t!.Translations)
            .Include(b => b.Plantings).ThenInclude(x => x.Plant)
            .ToListAsync();
        var rows = beds.Select(b => new Row(
            b.Id, b.Name, b.Kind, b.Shape, b.WidthCm, b.LengthCm, b.HeightCm,
            b.AreaSquareMeters, b.VolumeLiters,
            // "Kiwi – Solissimo, Rhubarb – Livingstone 2, Wild strawberry 17"
            string.Join(", ", b.Plantings
                .Select(x => PlantName(x, language) + (x.PlantCount is null ? "" : $" {x.PlantCount}"))
                .OrderBy(n => n, comparer)),
            b.Note));

        if (KindFilter is not null) rows = rows.Where(r => r.Kind == KindFilter);
        if (ShapeFilter is not null) rows = rows.Where(r => r.Shape == ShapeFilter);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            rows = rows.Where(r =>
                r.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                r.Plantings.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                (r.Note?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false));
        }

        var byDefault = rows.OrderBy(r => r.Name, comparer).ToList();
        TotalArea = byDefault.Sum(r => r.AreaSquareMeters);

        var desc = Descending;
        Rows = (Sort?.ToLowerInvariant()) switch
        {
            "name" => ListSorting.Sort(byDefault, r => r.Name, desc),
            "kind" => ListSorting.Sort(byDefault, r => L["BedKind_" + r.Kind].Value, desc),
            "shape" => ListSorting.Sort(byDefault, r => L["BedShape_" + r.Shape].Value, desc),
            "width" => ListSorting.Sort(byDefault, r => r.WidthCm, desc),
            "length" => ListSorting.Sort(byDefault, r => r.LengthCm, desc),
            "height" => ListSorting.Sort(byDefault, r => r.HeightCm, desc),
            "area" => ListSorting.Sort(byDefault, r => r.AreaSquareMeters, desc),
            "volume" => ListSorting.Sort(byDefault, r => r.VolumeLiters, desc),
            "plantings" => ListSorting.Sort(byDefault, r => r.Plantings.Length == 0 ? null : r.Plantings, desc),
            "note" => ListSorting.Sort(byDefault, r => r.Note, desc),
            _ => byDefault
        };
    }

    // Name in the current language, English name if there is no translation
    private static string TypeName(PlantType type, string language) =>
        type.Translations.FirstOrDefault(t => t.Language == language)?.Name ?? type.Name;

    // The plant type, followed by the variety if there is one
    private static string PlantName(BedPlanting x, string language)
    {
        var type = TypeName(x.PlantType!, language);
        return x.Plant is not null && x.Plant.Name != "(unnamed)" ? $"{type} – {x.Plant.Name}" : type;
    }

    // ---------------------------------------------------------------- sort links

    public bool Descending => string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase);

    private bool SortedBy(string column) =>
        string.Equals(Sort ?? "", column, StringComparison.OrdinalIgnoreCase);

    public string Arrow(string column) => SortedBy(column) ? (Descending ? " ▼" : " ▲") : "";

    public Dictionary<string, string?> SortRoute(string column) => new()
    {
        ["q"] = Q,
        ["kind"] = KindFilter?.ToString(),
        ["shape"] = ShapeFilter?.ToString(),
        ["sort"] = column,
        ["dir"] = SortedBy(column) && !Descending ? "desc" : "asc"
    };
}
