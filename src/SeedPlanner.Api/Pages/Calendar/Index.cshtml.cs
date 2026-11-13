using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Calendar;

/// <summary>Sowing, bloom and harvest time of all varieties as a strip of the year (36 thirds of months).</summary>
public class IndexModel(SeedPlannerDbContext db, IStringLocalizer<SharedResource> L) : PageModel
{
    public const int Thirds = 36;

    /// <summary>The three rows of marks of one strip; each array has one entry per third of a month.</summary>
    public record Strip(bool[] Sowing, bool[] Bloom, bool[] Harvest, string Label);

    public record VarietyRow(int Id, string Name, bool IsUnnamed, Strip Strip);

    public record TypeRow(int Id, string Name, Strip Strip, List<VarietyRow> Varieties);

    public List<TypeRow> Types { get; private set; } = new();

    /// <summary>For each month: how many plant types can be harvested in it (all types, regardless of the search).</summary>
    public int[] HarvestTypesPerMonth { get; private set; } = new int[12];

    public string[] MonthNames { get; private set; } = Array.Empty<string>();

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    // The filter form sends "f"; without it (first visit) everything is shown
    [BindProperty(SupportsGet = true)]
    public string? F { get; set; }

    /// <summary>Only varieties that are in stock (not out of stock, not retired).</summary>
    [BindProperty(SupportsGet = true)]
    public bool OnlyInStock { get; set; }

    // Which of the marks are shown (default: all)
    [BindProperty(SupportsGet = true)]
    public bool Sow { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Bloom { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Harvest { get; set; }

    public async Task OnGetAsync()
    {
        if (F is null) { Sow = Bloom = Harvest = true; }

        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        MonthNames = Enumerable.Range(1, 12).Select(m => CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(m)).ToArray();

        var plants = await db.Plants.AsNoTracking()
            .Include(p => p.PlantType).ThenInclude(t => t.Translations)
            .Include(p => p.SeasonWindows)
            .ToListAsync();

        if (OnlyInStock) plants = plants.Where(p => p.StockStatus == StockStatus.InStock).ToList();

        var all = plants.GroupBy(p => p.PlantTypeId).Select(g =>
        {
            var type = g.First().PlantType;
            var name = type.Translations.FirstOrDefault(t => t.Language == language)?.Name ?? type.Name;
            var varieties = g.OrderBy(p => p.Name, comparer)
                .Select(p => new VarietyRow(p.Id, p.Name, IsUnnamed(p.Name), StripOf(new[] { p }, p.Name)))
                .ToList();
            return new TypeRow(type.Id, name, StripOf(g, name), varieties);
        }).OrderBy(t => t.Name, comparer).ToList();

        // Counted over the shown varieties (so the stock filter counts), and independent of the shown marks
        var harvestOf = plants.GroupBy(p => p.PlantTypeId)
            .Select(g => StripOf(g, "", sow: false, bloom: false, harvest: true).Harvest).ToList();
        for (var m = 0; m < 12; m++)
            HarvestTypesPerMonth[m] = harvestOf.Count(h => h.Skip(m * 3).Take(3).Any(x => x));

        var q = Q?.Trim();
        Types = string.IsNullOrEmpty(q)
            ? all
            : all.Where(t => t.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                             || t.Varieties.Any(v => v.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase))).ToList();
    }

    private static bool IsUnnamed(string name) => string.IsNullOrWhiteSpace(name) || name == "(unnamed)";

    // The union of the windows of the given varieties
    private Strip StripOf(IEnumerable<Plant> plants, string label) => StripOf(plants, label, Sow, Bloom, Harvest);

    private static Strip StripOf(IEnumerable<Plant> plants, string label, bool sow, bool bloom, bool harvest)
    {
        var sowing = new bool[Thirds];
        var blooming = new bool[Thirds];
        var harvesting = new bool[Thirds];
        foreach (var w in plants.SelectMany(p => p.SeasonWindows))
        {
            if (w.Kind == WindowKind.Sowing && !sow || w.Kind == WindowKind.Bloom && !bloom || w.Kind == WindowKind.Harvest && !harvest) continue;
            var target = w.Kind switch { WindowKind.Sowing => sowing, WindowKind.Bloom => blooming, _ => harvesting };
            for (var t = 0; t < Thirds; t++)
            {
                var part = (t % 3) switch { 0 => MonthPart.Early, 1 => MonthPart.Mid, _ => MonthPart.Late };
                if (w.Contains(t / 3 + 1, part)) target[t] = true;
            }
        }
        return new Strip(sowing, blooming, harvesting, label);
    }

    public string Unnamed => L["Plant_Unnamed"].Value;
}
