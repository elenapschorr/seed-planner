using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Pots;

public class IndexModel(SeedPlannerDbContext db, IStringLocalizer<SharedResource> L) : PageModel
{
    public record Row(
        int Id,
        string Name,
        PotKind Kind,
        PotShape Shape,
        decimal? TopCm,
        decimal? BottomCm,
        decimal? HeightCm,
        double? Volume,
        int Quantity,
        int SetSize,
        int SetCount,
        int Occupied,
        int Free,
        string? Color,
        string? Code,
        decimal? SaucerDiameter,
        string? Saucer,
        string Uses,
        string? Note);

    public List<Row> Rows { get; private set; } = new();
    public List<string> ColorOptions { get; private set; } = new();

    // Totals of the pots shown
    public int TotalPots { get; private set; }
    public int TotalFree { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(Name = "kind", SupportsGet = true)]
    public PotKind? KindFilter { get; set; }

    [BindProperty(Name = "shape", SupportsGet = true)]
    public PotShape? ShapeFilter { get; set; }

    [BindProperty(Name = "color", SupportsGet = true)]
    public string? ColorFilter { get; set; }

    // Only groups that have free pots
    [BindProperty(Name = "free", SupportsGet = true)]
    public bool OnlyFree { get; set; }

    // Column to sort by (name, kind, shape, size, volume, quantity, inuse, free, color, code, saucer, uses, note)
    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Dir { get; set; }

    public async Task OnGetAsync()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);

        // One row per group; the single pots and their plantings are counted in memory
        var groups = await db.PotGroups.AsNoTracking()
            .Include(g => g.Saucer)
            .Include(g => g.Pots).ThenInclude(p => p.Plantings).ThenInclude(x => x.PlantType).ThenInclude(t => t!.Translations)
            .Include(g => g.Pots).ThenInclude(p => p.Plantings).ThenInclude(x => x.Plant)
            .ToListAsync();

        ColorOptions = groups
            .Select(p => p.Color)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(c => c, comparer)
            .ToList();

        var rows = groups.Select(g => new Row(
            g.Id, g.Name, g.Kind, g.Shape, g.TopCm, g.BottomCm, g.HeightCm, g.VolumeLiters,
            g.Quantity, g.SetSize, g.SetCount, g.OccupiedQuantity, g.FreeQuantity,
            g.Color, g.Code,
            g.Saucer?.DiameterCm,
            g.Saucer is null ? null : $"{Format(g.Saucer.DiameterCm)} {g.Saucer.Color}".Trim(),
            UsesText(g, language, comparer),
            g.Note));

        if (KindFilter is not null) rows = rows.Where(r => r.Kind == KindFilter);
        if (ShapeFilter is not null) rows = rows.Where(r => r.Shape == ShapeFilter);
        if (!string.IsNullOrEmpty(ColorFilter))
            rows = rows.Where(r => string.Equals(r.Color, ColorFilter, StringComparison.CurrentCultureIgnoreCase));
        if (OnlyFree) rows = rows.Where(r => r.Free > 0);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            rows = rows.Where(r =>
                Contains(r.Name, q) || Contains(r.Color, q) || Contains(r.Code, q) || Contains(r.Note, q) ||
                Contains(r.Uses, q) || Contains(r.Saucer, q));
        }

        // Default order: big pots first
        var byDefault = rows
            .OrderByDescending(r => r.TopCm ?? 0)
            .ThenByDescending(r => r.Quantity)
            .ToList();

        TotalPots = byDefault.Sum(r => r.Quantity);
        TotalFree = byDefault.Sum(r => r.Free);

        var desc = Descending;
        Rows = (Sort?.ToLowerInvariant()) switch
        {
            "name" => ListSorting.Sort(byDefault, r => r.Name, desc),
            "kind" => ListSorting.Sort(byDefault, r => L["PotKind_" + r.Kind].Value, desc),
            "shape" => ListSorting.Sort(byDefault, r => L["PotShape_" + r.Shape].Value, desc),
            "size" => ListSorting.Sort(byDefault, r => r.TopCm, desc),
            "volume" => ListSorting.Sort(byDefault, r => r.Volume, desc),
            "quantity" => ListSorting.Sort(byDefault, r => r.Quantity, desc),
            "inuse" => ListSorting.Sort(byDefault, r => r.Occupied, desc),
            "free" => ListSorting.Sort(byDefault, r => r.Free, desc),
            "color" => ListSorting.Sort(byDefault, r => r.Color, desc),
            "code" => ListSorting.Sort(byDefault, r => r.Code, desc),
            "saucer" => ListSorting.Sort(byDefault, r => r.SaucerDiameter, desc),
            "uses" => ListSorting.Sort(byDefault, r => r.Uses.Length == 0 ? null : r.Uses, desc),
            "note" => ListSorting.Sort(byDefault, r => r.Note, desc),
            _ => byDefault
        };
    }

    private static bool Contains(string? value, string q) =>
        value is not null && value.Contains(q, StringComparison.CurrentCultureIgnoreCase);

    // Name in the current language, English name if there is no translation
    private static string TypeName(PlantType type, string language) =>
        type.Translations.FirstOrDefault(t => t.Language == language)?.Name ?? type.Name;

    // The plant type, followed by the variety if there is one, and the number of plants if it is known
    private static string PlantName(Planting x, string language)
    {
        var type = TypeName(x.PlantType!, language);
        var name = x.Plant is not null && x.Plant.Name != "(unnamed)" ? $"{type} – {x.Plant.Name}" : type;
        return x.PlantCount is null ? name : $"{name} ×{x.PlantCount}";
    }

    // "Cranberry – Howes + Cranberry – Red Star 1, Heidelbeere 2": the pots with the same plants are counted together
    private static string UsesText(PotGroup group, string language, StringComparer comparer) =>
        string.Join(", ", group.Pots
            .Where(p => p.Plantings.Count > 0)
            .GroupBy(p => string.Join(" + ", p.Plantings.Select(x => PlantName(x, language)).OrderBy(n => n, comparer)))
            .OrderBy(g => g.Key, comparer)
            .Select(g => $"{g.Key} {g.Count()}"));

    private static string Format(decimal value) => value.ToString("0.#", CultureInfo.CurrentCulture);

    // "37 × 18 × 60", missing measures as "–"
    public string Size(Row r) =>
        string.Join(" × ", new[] { r.TopCm, r.BottomCm, r.HeightCm }.Select(v => v is null ? "–" : Format(v.Value)));

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
        ["color"] = ColorFilter,
        ["free"] = OnlyFree ? "true" : null,
        ["sort"] = column,
        ["dir"] = SortedBy(column) && !Descending ? "desc" : "asc"
    };
}
