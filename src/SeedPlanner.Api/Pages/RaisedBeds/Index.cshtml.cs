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
        bool Overbooked,
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

    // Column to sort by (kind, shape, size, volume, quantity, inuse, free, color, code, saucer, uses, note)
    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Dir { get; set; }

    public async Task OnGetAsync()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);

        var pots = await db.Pots.AsNoTracking()
            .Include(p => p.Saucer)
            .Include(p => p.Uses).ThenInclude(u => u.PlantType).ThenInclude(t => t!.Translations)
            .ToListAsync();

        ColorOptions = pots
            .Select(p => p.Color)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(c => c, comparer)
            .ToList();

        var rows = pots.Select(p => new Row(
            p.Id, p.Kind, p.Shape, p.TopCm, p.BottomCm, p.HeightCm, p.VolumeLiters,
            p.Quantity, p.SetSize, p.SetCount, p.OccupiedQuantity, p.FreeQuantity, p.IsOverbooked,
            p.Color, p.Code,
            p.Saucer?.DiameterCm,
            p.Saucer is null ? null : $"{Format(p.Saucer.DiameterCm)} {p.Saucer.Color}".Trim(),
            string.Join(", ", p.Uses
                .Select(u => (Name: TypeName(u.PlantType!, language), u.Quantity))
                .OrderBy(u => u.Name, comparer)
                .Select(u => $"{u.Name} {u.Quantity}")),
            p.Note));

        if (KindFilter is not null) rows = rows.Where(r => r.Kind == KindFilter);
        if (ShapeFilter is not null) rows = rows.Where(r => r.Shape == ShapeFilter);
        if (!string.IsNullOrEmpty(ColorFilter))
            rows = rows.Where(r => string.Equals(r.Color, ColorFilter, StringComparison.CurrentCultureIgnoreCase));
        if (OnlyFree) rows = rows.Where(r => r.Free > 0);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            rows = rows.Where(r =>
                Contains(r.Color, q) || Contains(r.Code, q) || Contains(r.Note, q) ||
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
