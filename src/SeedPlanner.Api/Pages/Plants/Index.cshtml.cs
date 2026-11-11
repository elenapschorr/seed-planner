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
        SowingMethod? SowingMethod,
        GrowingPlace? GrowingPlace,
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

    /// <summary>Search, filters, chips and the addresses of the column titles (shared with the other lists).</summary>
    public ListFilterView Filter { get; private set; } = null!;

    // Search text, sort column (name, type, propagation, lifecycle, seedtype, stock, sowing, place, nutrient, exposure, pots, tobuy) and direction (asc, desc)
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Dir { get; set; }

    private sealed class Candidate
    {
        public required Plant P { get; init; }
        public required Row R { get; init; }
    }

    // ---------------------------------------------------------------- loading and filtering

    public async Task OnGetAsync()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);

        var types = await db.PlantTypes.AsNoTracking().Include(t => t.Translations).ToListAsync();
        var typeNames = types.ToDictionary(t => t.Id, t => TranslatedName(t, language));

        var plants = await db.Plants.AsNoTracking()
            .Include(p => p.PlantType).ThenInclude(t => t.Translations)
            .Include(p => p.SeasonWindows)
            .ToListAsync();

        // Where the varieties grow: pots (one planting in a pot counts as one pot) and beds
        var plantings = await db.Plantings.AsNoTracking()
            .Where(x => x.PlantId != null)
            .Select(x => new { x.PlantId, x.PotId, x.RaisedBedId })
            .ToListAsync();
        var potsPerPlant = plantings.Where(x => x.PotId != null)
            .GroupBy(x => x.PlantId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.PotId).Distinct().Count());
        var bedsPerPlant = plantings.Where(x => x.RaisedBedId != null)
            .GroupBy(x => x.PlantId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.RaisedBedId).Distinct().Count());

        // Good neighbours between plant types, in both directions
        var goodNeighbours = new Dictionary<int, HashSet<int>>();
        var pairs = await db.PlantTypeCompanions.AsNoTracking()
            .Where(c => c.Relation == CompanionRelation.Good)
            .Select(c => new { c.PlantTypeId, c.OtherPlantTypeId })
            .ToListAsync();
        foreach (var pair in pairs)
        {
            if (!goodNeighbours.TryGetValue(pair.PlantTypeId, out var a)) goodNeighbours[pair.PlantTypeId] = a = new();
            if (!goodNeighbours.TryGetValue(pair.OtherPlantTypeId, out var b)) goodNeighbours[pair.OtherPlantTypeId] = b = new();
            a.Add(pair.OtherPlantTypeId);
            b.Add(pair.PlantTypeId);
        }

        var candidates = plants.Select(p => new Candidate
        {
            P = p,
            R = new Row(
                p.Id, p.Name, p.PlantTypeId, TranslatedName(p.PlantType, language),
                p.Propagation, p.PlantType.Lifecycle, p.SeedType, p.StockStatus,
                p.SowingMethod, p.GrowingPlace, p.PlantType.NutrientDemand,
                p.EffectiveExposure,   // own value of the plant, otherwise the default of its plant type
                potsPerPlant.GetValueOrDefault(p.Id),
                p.ShouldBuy)           // rule lives in the domain class
        }).ToList();

        // ---- the filters: key in the address, group of the panel, title, values of a variety, options
        var neighbourTypes = candidates.SelectMany(c => goodNeighbours.GetValueOrDefault(c.P.PlantTypeId) ?? new HashSet<int>())
            .Distinct()
            .Select(id => (Value: id.ToString(), Label: typeNames[id]))
            .OrderBy(o => o.Label, comparer).ToList();
        var months = Enumerable.Range(1, 12)
            .Select(m => (Value: m.ToString(), Label: CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m))).ToList();

        static IEnumerable<string> One(object? value) => FilterOptions.One(value);
        static IEnumerable<string> TempClass(int? minC) =>
            minC is null ? Array.Empty<string>() : new[] { minC < 15 ? "cool" : minC < 20 ? "mild" : "warm" };
        static IEnumerable<string> MonthsOf(Plant p, WindowKind kind) =>
            Enumerable.Range(1, 12)
                .Where(m => p.SeasonWindows.Any(w => w.Kind == kind && w.Contains(m)))
                .Select(m => m.ToString());
        static bool IsFlower(Plant p) => p.PlantType.Category == PlantCategory.Flower;

        var facets = new List<Facet<Candidate>>
        {
            new("category", "Filter_Group_Plant", L["Filter_Category"].Value, c => One(c.P.PlantType.Category), EnumOptions<PlantCategory>("Category_")),
            // plant types as sub-items of the categories
            new("type", "Filter_Group_Plant", L["Filter_PlantType"].Value, c => new[] { c.P.PlantTypeId.ToString() },
                types.Select(t => (Value: t.Id.ToString(), Label: typeNames[t.Id])).OrderBy(o => o.Label, comparer).ToList(),
                ParentKey: "category",
                ParentOf: v => types.FirstOrDefault(t => t.Id.ToString() == v)?.Category?.ToString()),
            new("lifecycle", "Filter_Group_Plant", L["Col_Lifecycle"].Value, c => One(c.P.PlantType.Lifecycle), EnumOptions<Lifecycle>("Lifecycle_")),
            new("nutrient", "Filter_Group_Plant", L["Col_NutrientDemand"].Value, c => One(c.P.PlantType.NutrientDemand), EnumOptions<NutrientDemand>("NutrientDemand_")),
            new("germType", "Filter_Group_Plant", L["Filter_GermType"].Value, c => One(c.P.PlantType.GerminationType), EnumOptions<GerminationType>("GerminationType_")),

            new("propagation", "Filter_Group_Stock", L["Col_Propagation"].Value, c => One(c.P.Propagation), EnumOptions<Propagation>("Propagation_")),
            new("seedType", "Filter_Group_Stock", L["Col_SeedType"].Value, c => One(c.P.SeedType), EnumOptions<SeedType>("SeedType_")),
            new("stock", "Filter_Group_Stock", L["Col_Stock"].Value, c => One(c.P.StockStatus), EnumOptions<StockStatus>("StockStatus_")),
            new("toBuy", "Filter_Group_Stock", L["Badge_ToBuy"].Value, c => c.P.ShouldBuy ? new[] { "1" } : Array.Empty<string>(), new List<(string Value, string Label)> { ("1", L["Filter_OnlyToBuy"].Value) },
                Normalize: v => v == "true" ? "1" : v),   // addresses from older versions of the list

            new("sowing", "Filter_Group_Growing", L["Col_Sowing"].Value, c => One(c.P.SowingMethod), EnumOptions<SowingMethod>("SowingMethod_")),
            new("place", "Filter_Group_Growing", L["Col_GrowingPlace"].Value, c => One(c.P.GrowingPlace), EnumOptions<GrowingPlace>("GrowingPlace_")),
            new("exposure", "Filter_Group_Growing", L["Col_Exposure"].Value, c => One(c.P.EffectiveExposure), EnumOptions<Exposure>("Exposure_")),
            new("germTemp", "Filter_Group_Growing", L["Filter_GermTemp"].Value,
                c => TempClass(c.P.GerminationTempMinC),
                new List<(string Value, string Label)> { ("cool", L["Temp_Cool"].Value), ("mild", L["Temp_Mild"].Value), ("warm", L["Temp_Warm"].Value) }),

            new("month", "Filter_Group_Time", L["Filter_SowMonth"].Value, c => MonthsOf(c.P, WindowKind.Sowing), months),
            new("bloom", "Filter_Group_Time", L["Filter_BloomMonth"].Value, c => MonthsOf(c.P, WindowKind.Bloom), months),
            new("harvest", "Filter_Group_Time", L["Filter_HarvestMonth"].Value, c => MonthsOf(c.P, WindowKind.Harvest), months),

            new("use", "Filter_Group_Use", L["Filter_Use"].Value,
                c =>
                {
                    var inPot = potsPerPlant.ContainsKey(c.P.Id);
                    var inBed = bedsPerPlant.ContainsKey(c.P.Id);
                    var list = new List<string>();
                    if (inPot) list.Add("pot");
                    if (inBed) list.Add("bed");
                    if (!inPot && !inBed) list.Add("none");
                    return list;
                },
                new List<(string Value, string Label)> { ("pot", L["Use_Pot"].Value), ("bed", L["Use_Bed"].Value), ("none", L["Use_None"].Value) }),
            new("goodWith", "Filter_Group_Use", L["Filter_GoodWith"].Value,
                c => (goodNeighbours.GetValueOrDefault(c.P.PlantTypeId) ?? new HashSet<int>()).Select(id => id.ToString()),
                neighbourTypes, Scroll: true),

            new("missing", "Filter_Group_Data", L["Filter_Missing"].Value,
                c =>
                {
                    var p = c.P;
                    var isSeed = p.Propagation == Propagation.Seed;
                    var list = new List<string>();
                    // variety
                    if (isSeed && p.SeedType is null) list.Add("seedType");
                    if (p.SowingMethod is null) list.Add("sowingMethod");
                    if (p.GrowingPlace is null) list.Add("place");
                    if (p.EffectiveExposure is null) list.Add("exposure");
                    // germination only matters for seeds, not for young plants and cuttings
                    if (isSeed && (p.GerminationTempMinC is null || p.GerminationTempMaxC is null)) list.Add("germTemp");
                    if (isSeed && (p.GerminationDaysMin is null || p.GerminationDaysMax is null)) list.Add("germDays");
                    if (p.MaturityDaysMin is null || p.MaturityDaysMax is null) list.Add("maturity");
                    if (!p.SeasonWindows.Any(w => w.Kind == WindowKind.Sowing)) list.Add("sowing");
                    // flowers have a bloom time instead of a harvest
                    if (IsFlower(p) && !p.SeasonWindows.Any(w => w.Kind == WindowKind.Bloom)) list.Add("bloom");
                    if (!IsFlower(p) && !p.SeasonWindows.Any(w => w.Kind == WindowKind.Harvest)) list.Add("harvest");
                    // spacing is optional for pot culture, the pot size is required there
                    if (p.GrowingPlace != GrowingPlace.Container && p.RowSpacingCm is null && p.PlantSpacingCm is null) list.Add("spacing");
                    if (p.GrowingPlace == GrowingPlace.Container && (p.PotSizeMinLiters is null || p.PotSizeMaxLiters is null)) list.Add("potSize");
                    if (p.HeightMinCm is null || p.HeightMaxCm is null) list.Add("height");
                    // plant type
                    if (p.PlantType.Category is null) list.Add("category");
                    if (p.PlantType.Lifecycle is null) list.Add("lifecycle");
                    if (p.PlantType.NutrientDemand is null) list.Add("nutrient");
                    if (isSeed && p.PlantType.GerminationType is null) list.Add("germType");
                    return list;
                },
                new List<(string Value, string Label)>
                {
                    ("seedType", L["Missing_SeedType"].Value), ("sowingMethod", L["Missing_SowingMethod"].Value), ("place", L["Missing_Place"].Value),
                    ("exposure", L["Missing_Exposure"].Value), ("germTemp", L["Missing_GermTemp"].Value), ("germDays", L["Missing_GermDays"].Value),
                    ("maturity", L["Missing_Maturity"].Value), ("sowing", L["Missing_Sowing"].Value), ("bloom", L["Missing_Bloom"].Value),
                    ("harvest", L["Missing_Harvest"].Value), ("spacing", L["Missing_Spacing"].Value), ("potSize", L["Missing_PotSize"].Value),
                    ("height", L["Missing_Height"].Value), ("category", L["Missing_Category"].Value), ("lifecycle", L["Missing_Lifecycle"].Value),
                    ("nutrient", L["Missing_Nutrient"].Value), ("germType", L["Missing_GermType"].Value)
                }),
        };

        // ---- search and filters (several options of one filter = OR, different filters = AND)
        var filter = new ListFilter<Candidate>(Request, Q, Sort, Dir);
        Filter = filter;
        var rows = filter.Apply(candidates, facets,
                (c, q) => (!c.R.IsUnnamed && c.R.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                          || c.R.TypeName.Contains(q, StringComparison.CurrentCultureIgnoreCase))
            .Select(c => c.R);

        // Default order: plant type, then named varieties, then unnamed ones
        var byDefault = rows
            .OrderBy(r => r.TypeName, comparer)
            .ThenBy(r => r.IsUnnamed)
            .ThenBy(r => r.Name, comparer)
            .ToList();

        Rows = (Sort?.ToLowerInvariant()) switch
        {
            "name" => ListSorting.Sort(byDefault, r => r.IsUnnamed ? null : r.Name, Filter.Descending),
            "type" => ListSorting.Sort(byDefault, r => r.TypeName, Filter.Descending),
            "propagation" => ListSorting.Sort(byDefault, r => L["Propagation_" + r.Propagation].Value, Filter.Descending),
            "lifecycle" => ListSorting.Sort(byDefault, r => r.Lifecycle is null ? null : L["Lifecycle_" + r.Lifecycle].Value, Filter.Descending),
            "seedtype" => ListSorting.Sort(byDefault, r => r.SeedType is null ? null : L["SeedType_" + r.SeedType].Value, Filter.Descending),
            "stock" => ListSorting.Sort(byDefault, r => L["StockStatus_" + r.StockStatus].Value, Filter.Descending),
            "sowing" => ListSorting.Sort(byDefault, r => r.SowingMethod is null ? null : L["SowingMethod_" + r.SowingMethod].Value, Filter.Descending),
            "place" => ListSorting.Sort(byDefault, r => r.GrowingPlace is null ? null : L["GrowingPlace_" + r.GrowingPlace].Value, Filter.Descending),
            "nutrient" => ListSorting.Sort(byDefault, r => ListSorting.Rank(r.NutrientDemand), Filter.Descending),
            "exposure" => ListSorting.Sort(byDefault, r => ListSorting.Rank(r.Exposure), Filter.Descending),
            "pots" => ListSorting.Sort(byDefault, r => r.PotCount, Filter.Descending),
            "tobuy" => ListSorting.Sort(byDefault, r => r.ShouldBuy ? 0 : 1, Filter.Descending),
            _ => byDefault
        };
    }

    private List<(string Value, string Label)> EnumOptions<T>(string prefix) where T : struct, Enum =>
        FilterOptions.Enum<T>(L, prefix);

    // Name in the current language, English name if there is no translation
    private static string TranslatedName(PlantType type, string language) =>
        type.Translations.FirstOrDefault(t => t.Language == language)?.Name ?? type.Name;

    // ---------------------------------------------------------------- addresses

    // The list with its filters and sorting; the edit page returns here, so the filters are kept
    public string ReturnUrl => Filter.ReturnUrl;

    // Title of a column: keeps search and filters; a second click reverses the direction
    public string SortUrl(string column) => Filter.SortUrl(column);

    // Arrow behind the column title
    public string Arrow(string column) => Filter.Arrow(column);
}
