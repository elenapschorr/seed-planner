using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Plants;

public class IndexModel(SeedPlannerDbContext db, IStringLocalizer<SharedResource> L) : PageModel
{
    public record Row(
        int Id,
        string Name,
        int TypeId,
        string TypeName,
        Propagation Propagation,
        Lifecycle? Lifecycle,
        SeedType? SeedType,
        StockStatus StockStatus,
        CultivationMethod? CultivationMethod,
        NutrientDemand? NutrientDemand,
        Exposure? Exposure,
        int PotCount,
        bool ShouldBuy)
    {
        // The import stores "(unnamed)" for seed packets without a variety name
        public bool IsUnnamed => string.IsNullOrWhiteSpace(Name) || Name == "(unnamed)";
    }

    public record TypeOption(int Id, string Name);

    public List<Row> Rows { get; private set; } = new();

    // Seed packets and living plants (young plants, cuttings) are shown in separate tables
    public List<Row> SeedRows => Rows.Where(r => r.Propagation == Propagation.Seed).ToList();
    public List<Row> PlantRows => Rows.Where(r => r.Propagation != Propagation.Seed).ToList();
    public List<TypeOption> TypeOptions { get; private set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? TypeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public StockStatus? Stock { get; set; }

    // 1..12: only plants that can be sown in this month
    [BindProperty(SupportsGet = true)]
    public int? Month { get; set; }

    [BindProperty(Name = "propagation", SupportsGet = true)]
    public Propagation? PropagationFilter { get; set; }

    [BindProperty(Name = "lifecycle", SupportsGet = true)]
    public Lifecycle? LifecycleFilter { get; set; }

    [BindProperty(Name = "seedType", SupportsGet = true)]
    public SeedType? SeedTypeFilter { get; set; }

    [BindProperty(Name = "cultivation", SupportsGet = true)]
    public CultivationMethod? CultivationFilter { get; set; }

    [BindProperty(Name = "nutrient", SupportsGet = true)]
    public NutrientDemand? NutrientFilter { get; set; }

    [BindProperty(Name = "exposure", SupportsGet = true)]
    public Exposure? ExposureFilter { get; set; }

    // Only plants that should be bought
    [BindProperty(Name = "toBuy", SupportsGet = true)]
    public bool OnlyToBuy { get; set; }

    // Column to sort by (name, type, propagation, lifecycle, seedtype, stock, cultivation, nutrient, exposure, pots, tobuy) and direction (asc, desc)
    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Dir { get; set; }

    public async Task OnGetAsync()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);

        // Plant types for the filter, named in the current language
        var types = await db.PlantTypes.AsNoTracking()
            .Include(t => t.Translations)
            .ToListAsync();
        TypeOptions = types
            .Select(t => new TypeOption(t.Id, TranslatedName(t, language)))
            .OrderBy(t => t.Name, comparer)
            .ToList();

        var query = db.Plants.AsNoTracking()
            .Include(p => p.PlantType).ThenInclude(t => t.Translations)
            .Include(p => p.SeasonWindows)
            .AsQueryable();
        if (TypeId is not null) query = query.Where(p => p.PlantTypeId == TypeId);
        if (Stock is not null) query = query.Where(p => p.StockStatus == Stock);
        if (PropagationFilter is not null) query = query.Where(p => p.Propagation == PropagationFilter);
        if (LifecycleFilter is not null) query = query.Where(p => p.PlantType.Lifecycle == LifecycleFilter);
        if (SeedTypeFilter is not null) query = query.Where(p => p.SeedType == SeedTypeFilter);
        if (CultivationFilter is not null) query = query.Where(p => p.CultivationMethod == CultivationFilter);
        if (NutrientFilter is not null) query = query.Where(p => p.PlantType.NutrientDemand == NutrientFilter);

        var plants = await query.ToListAsync();

        // These rules live in the domain classes, so they are checked in memory
        if (Month is >= 1 and <= 12)
        {
            plants = plants.Where(p => p.CanSowIn(Month.Value)).ToList();
        }
        if (ExposureFilter is not null)
        {
            plants = plants.Where(p => p.EffectiveExposure == ExposureFilter).ToList();
        }
        if (OnlyToBuy)
        {
            plants = plants.Where(p => p.ShouldBuy).ToList();
        }

        // Pots in which the variety grows: one planting in a pot counts as one pot
        var potsPerPlant = (await db.Plantings.AsNoTracking()
                .Where(x => x.PlantId != null && x.PotId != null)
                .Select(x => new { x.PlantId, x.PotId })
                .ToListAsync())
            .GroupBy(x => x.PlantId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.PotId).Distinct().Count());

        var rows = plants.Select(p => new Row(
            p.Id,
            p.Name,
            p.PlantTypeId,
            TranslatedName(p.PlantType, language),
            p.Propagation,
            p.PlantType.Lifecycle,
            p.SeedType,
            p.StockStatus,
            p.CultivationMethod,
            p.PlantType.NutrientDemand,
            p.EffectiveExposure,   // own value of the plant, otherwise the default of its plant type
            potsPerPlant.GetValueOrDefault(p.Id),
            p.ShouldBuy));         // rule lives in the domain class

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            rows = rows.Where(r =>
                (!r.IsUnnamed && r.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)) ||
                r.TypeName.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        }

        // Default order: plant type, then named varieties, then unnamed ones
        var byDefault = rows
            .OrderBy(r => r.TypeName, comparer)
            .ThenBy(r => r.IsUnnamed)
            .ThenBy(r => r.Name, comparer)
            .ToList();

        Rows = (Sort?.ToLowerInvariant()) switch
        {
            "name" => ListSorting.Sort(byDefault, r => r.IsUnnamed ? null : r.Name, Descending),
            "type" => ListSorting.Sort(byDefault, r => r.TypeName, Descending),
            "propagation" => ListSorting.Sort(byDefault, r => L["Propagation_" + r.Propagation].Value, Descending),
            "lifecycle" => ListSorting.Sort(byDefault, r => r.Lifecycle is null ? null : L["Lifecycle_" + r.Lifecycle].Value, Descending),
            "seedtype" => ListSorting.Sort(byDefault, r => r.SeedType is null ? null : L["SeedType_" + r.SeedType].Value, Descending),
            "stock" => ListSorting.Sort(byDefault, r => L["StockStatus_" + r.StockStatus].Value, Descending),
            "cultivation" => ListSorting.Sort(byDefault, r => r.CultivationMethod is null ? null : L["CultivationMethod_" + r.CultivationMethod].Value, Descending),
            "nutrient" => ListSorting.Sort(byDefault, r => ListSorting.Rank(r.NutrientDemand), Descending),
            "exposure" => ListSorting.Sort(byDefault, r => ListSorting.Rank(r.Exposure), Descending),
            "pots" => ListSorting.Sort(byDefault, r => r.PotCount, Descending),
            "tobuy" => ListSorting.Sort(byDefault, r => r.ShouldBuy ? 0 : 1, Descending),
            _ => byDefault
        };
    }

    // Name in the current language, English name if there is no translation
    private static string TranslatedName(PlantType type, string language) =>
        type.Translations.FirstOrDefault(t => t.Language == language)?.Name ?? type.Name;

    // ---------------------------------------------------------------- sort links

    public bool Descending => string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase);

    private bool SortedBy(string column) =>
        string.Equals(Sort ?? "", column, StringComparison.OrdinalIgnoreCase);

    // Arrow behind the column title
    public string Arrow(string column) => SortedBy(column) ? (Descending ? " ▼" : " ▲") : "";

    // Address of a column title: keeps the filters; a second click reverses the direction
    public Dictionary<string, string?> SortRoute(string column) => new()
    {
        ["q"] = Q,
        ["typeId"] = TypeId?.ToString(),
        ["stock"] = Stock?.ToString(),
        ["month"] = Month?.ToString(),
        ["propagation"] = PropagationFilter?.ToString(),
        ["lifecycle"] = LifecycleFilter?.ToString(),
        ["seedType"] = SeedTypeFilter?.ToString(),
        ["cultivation"] = CultivationFilter?.ToString(),
        ["nutrient"] = NutrientFilter?.ToString(),
        ["exposure"] = ExposureFilter?.ToString(),
        ["toBuy"] = OnlyToBuy ? "true" : null,
        ["sort"] = column,
        ["dir"] = SortedBy(column) && !Descending ? "desc" : "asc"
    };
}
