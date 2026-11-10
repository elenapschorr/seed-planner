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
    // Totals of the pots shown
    public int TotalPots { get; private set; }
    public int TotalFree { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>Search, filters, chips and the addresses of the column titles.</summary>
    public ListFilterView Filter { get; private set; } = null!;

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

        var rows = groups.Select(g => new Row(
            g.Id, g.Name, g.Kind, g.Shape, g.TopCm, g.BottomCm, g.HeightCm, g.VolumeLiters,
            g.Quantity, g.SetSize, g.SetCount, g.OccupiedQuantity, g.FreeQuantity,
            g.Color, g.Code,
            g.Saucer?.DiameterCm,
            g.Saucer is null ? null : $"{Format(g.Saucer.DiameterCm)} {g.Saucer.Color}".Trim(),
            UsesText(g, language, comparer),
            g.Note)).ToList();

        var colors = rows.Select(r => r.Color).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(c => c, comparer)
            .Select(c => (Value: c, Label: c)).ToList();
        var saucers = rows.Where(r => r.Saucer is not null).GroupBy(r => r.Saucer!)
            .OrderBy(g => g.First().SaucerDiameter).Select(g => (Value: g.Key, Label: g.Key)).ToList();

        var facets = new List<Facet<Row>>
        {
            new("kind", "Filter_Group_Properties", L["Col_Kind"].Value, r => FilterOptions.One(r.Kind), FilterOptions.Enum<PotKind>(L, "PotKind_")),
            new("shape", "Filter_Group_Properties", L["Col_Shape"].Value, r => FilterOptions.One(r.Shape), FilterOptions.Enum<PotShape>(L, "PotShape_")),
            new("color", "Filter_Group_Properties", L["Col_Color"].Value, r => FilterOptions.One(r.Color), colors),
            new("saucer", "Filter_Group_Properties", L["Col_Saucer"].Value, r => FilterOptions.One(r.Saucer), saucers),
            new("free", "Filter_Group_Occupancy", L["Col_Free"].Value, r => r.Free > 0 ? new[] { "1" } : Array.Empty<string>(),
                new List<(string Value, string Label)> { ("1", L["Filter_OnlyFree"].Value) }),
        };

        var filter = new ListFilter<Row>(Request, Q, Sort, Dir);
        Filter = filter;
        rows = filter.Apply(rows, facets, (r, q) =>
            Contains(r.Name, q) || Contains(r.Color, q) || Contains(r.Code, q) || Contains(r.Note, q) ||
            Contains(r.Uses, q) || Contains(r.Saucer, q));

        // Default order: big pots first
        var byDefault = rows
            .OrderByDescending(r => r.TopCm ?? 0)
            .ThenByDescending(r => r.Quantity)
            .ToList();

        TotalPots = byDefault.Sum(r => r.Quantity);
        TotalFree = byDefault.Sum(r => r.Free);

        var desc = Filter.Descending;
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
}
