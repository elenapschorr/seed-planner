using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Plans;

// The distribution of a plan: choose pots and beds, set what happens when the space is not enough,
// see where the plants go, and transfer the result as plantings
public class DistributionModel(SeedPlannerDbContext db, IStringLocalizer<SharedResource> L) : PageModel
{
    public record PotRow(int Id, string Label, string Occupied, bool Selected);
    public record PotGroupRow(string Name, double? Liters, PotKind Kind, List<PotRow> Pots);
    public record BedRow(int Id, string Name, double Area, bool HasCover, string Occupied, bool Selected);

    public Plan Plan { get; private set; } = null!;
    public List<PotGroupRow> PotGroups { get; private set; } = new();
    public List<BedRow> Beds { get; private set; } = new();

    /// <summary>Where the plants go (calculated again if something has changed); empty until pots or beds are chosen.</summary>
    public AllocationResult? Result { get; private set; }

    // ---- settings and choice (filled from the plan, changed by the form)
    [BindProperty] public ShortageStrategy Shortage { get; set; }
    [BindProperty] public NoCoverStrategy NoCover { get; set; }
    // Typed as text: the browser sends a dot, the German culture of the server expects a comma
    [BindProperty] public string? MaxBedArea { get; set; }
    [BindProperty] public bool AllowOthers { get; set; }
    [BindProperty] public int SpacingPercent { get; set; } = 100;
    [BindProperty] public int LastFrostThird { get; set; } = PlantOccupancy.DefaultLastFrostThird;
    [BindProperty] public List<int> PotIds { get; set; } = new();
    [BindProperty] public List<int> BedIds { get; set; } = new();

    public bool Saved => Request.Query["saved"] == "1";
    public int? AppliedCount => int.TryParse(Request.Query["applied"], out var n) ? n : null;

    private Dictionary<int, string> plantNames = new();
    private Dictionary<int, (string Type, string Variety)> plantParts = new();

    private (string Type, string Variety) PartsOf(int plantId) =>
        plantParts.TryGetValue(plantId, out var parts) ? parts : ("?", "?");

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var plan = await LoadPlanAsync(id);
        if (plan is null) return NotFound();

        Shortage = plan.Shortage;
        NoCover = plan.NoCover;
        MaxBedArea = plan.MaxBedAreaSquareMeters > 0
            ? plan.MaxBedAreaSquareMeters.ToString("0.##", CultureInfo.CurrentCulture)
            : "";
        AllowOthers = plan.AllowOthersInCoveredBeds;
        SpacingPercent = plan.SpacingPercent;
        LastFrostThird = plan.LastFrostThird;

        Result = await PlanAllocation.EnsureAsync(db, plan);
        await ShowAsync(plan);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(int id)
    {
        var plan = await LoadPlanAsync(id);
        if (plan is null) return NotFound();

        plan.Shortage = Shortage;
        plan.NoCover = NoCover;
        plan.MaxBedAreaSquareMeters =
            double.TryParse(MaxBedArea?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var area)
                ? Math.Max(0, area) : 0;
        plan.AllowOthersInCoveredBeds = AllowOthers;
        plan.SpacingPercent = Math.Clamp(SpacingPercent, 50, 100);
        plan.LastFrostThird = Math.Clamp(LastFrostThird, 0, 35);

        // Only pots and beds of the signed-in user exist for this query (query filter)
        var potIds = await db.Pots.Where(p => PotIds.Contains(p.Id)).Select(p => p.Id).ToListAsync();
        var bedIds = await db.RaisedBeds.Where(b => BedIds.Contains(b.Id)).Select(b => b.Id).ToListAsync();

        var gone = plan.Containers
            .Where(c => (c.PotId is { } p && !potIds.Contains(p)) || (c.RaisedBedId is { } b && !bedIds.Contains(b)))
            .ToList();
        db.PlanContainers.RemoveRange(gone);
        foreach (var c in gone) plan.Containers.Remove(c);

        foreach (var pot in potIds.Where(p => plan.Containers.All(c => c.PotId != p)))
            plan.Containers.Add(new PlanContainer { PotId = pot });
        foreach (var bed in bedIds.Where(b => plan.Containers.All(c => c.RaisedBedId != b)))
            plan.Containers.Add(new PlanContainer { RaisedBedId = bed });

        await db.SaveChangesAsync();
        await PlanAllocation.EnsureAsync(db, plan);
        return RedirectToPage("Distribution", new { id, saved = 1 });
    }

    // ---------------------------------------------------------------- report

    public static readonly string[] ReportSections = ["plants", "stats", "unplaced", "shopping"];
    public static readonly string[] ReportColumns = ["container", "kind", "capacity", "plantType", "variety", "count", "role", "period"];

    /// <summary>Exports the report: format csv, xlsx or print; sec = the chosen parts, col = the columns of the plant list.</summary>
    public async Task<IActionResult> OnGetExportAsync(int id, string? format, string[] sec, string[] col)
    {
        var plan = await LoadPlanAsync(id);
        if (plan is null) return NotFound();

        Result = await PlanAllocation.EnsureAsync(db, plan);
        if (Result is null) return RedirectToPage("Distribution", new { id });
        await ShowAsync(plan);

        var doc = BuildReport(sec, col);
        var slug = new string(plan.Name.Where(ch => char.IsLetterOrDigit(ch) || ch == '-').ToArray());
        var file = $"{(slug.Length == 0 ? "plan" : slug)}-{L["Report_FileName"].Value}-{DateTime.Now:yyyyMMdd}";

        return format switch
        {
            "xlsx" => File(ReportWriters.Xlsx(doc), ReportWriters.XlsxContentType, file + ".xlsx"),
            "csv" => File(ReportWriters.Csv(doc), "text/csv; charset=utf-8", file + ".csv"),
            _ => Partial("_ReportPrint", doc)
        };
    }

    private ReportDocument BuildReport(string[] sec, string[] col)
    {
        bool Has(string section) => sec.Length == 0 || sec.Contains(section);
        var culture = CultureInfo.CurrentCulture;
        var tables = new List<ReportTable>();

        // ---- plants per pot and bed
        if (Has("plants"))
        {
            var keys = ReportColumns.Where(k => col.Length == 0 || col.Contains(k)).ToList();
            string CellOf(string key, ContainerResult cont, PlacementResult? pl) => key switch
            {
                "container" => Title(cont),
                "kind" => cont.Kind == ContainerKind.Pot ? L["Report_Pot"].Value : L["Distribution_Bed"].Value,
                "capacity" => Capacity(cont),
                "plantType" => pl is null ? "" : PartsOf(pl.PlantId).Type,
                "variety" => pl is null ? "" : PartsOf(pl.PlantId).Variety,
                "count" => pl is null ? "" : pl.Count.ToString(culture),
                "role" => pl is null ? "" : pl.Role == PlantingRole.Companion ? L["Distribution_Under"].Value : L["Report_Main"].Value,
                "period" => pl is null ? "" : PlanAllocation.Period(pl.Slots) ?? L["Report_AllYear"].Value,
                _ => ""
            };

            var rows = new List<string[]>();
            foreach (var cont in ContainersToShow)
            {
                var placements = cont.Placements.OrderBy(q => q.Role).ToList();
                if (placements.Count == 0) rows.Add(keys.Select(k => CellOf(k, cont, null)).ToArray());
                foreach (var pl in placements) rows.Add(keys.Select(k => CellOf(k, cont, pl)).ToArray());
            }
            tables.Add(new ReportTable(L["Report_SecPlants"].Value, keys.Select(k => L["Report_Col_" + k].Value).ToArray(), rows));
        }

        // ---- fill level
        if (Has("stats"))
        {
            var clusters = Clusters();
            string[] RowOf(string label, int total, Func<int, int> band) =>
                new[] { label, total.ToString(culture) }
                    .Concat(Enumerable.Range(0, AllocationStats.BandCount).Select(b => band(b).ToString(culture))).ToArray();
            foreach (var kind in new[] { ContainerKind.Pot, ContainerKind.Bed })
            {
                var part = clusters.Where(c => c.Kind == kind).ToList();
                if (part.Count == 0) continue;
                var rows = new List<string[]>();
                foreach (var c in part)
                    rows.Add(RowOf(c.IsNew ? $"{c.Label} ({L["Distribution_New"].Value})" : c.Label, c.Total, b => c.Bands[b]));
                rows.Add(RowOf(kind == ContainerKind.Pot ? L["Stats_Pots"].Value : L["Stats_Beds"].Value, part.Sum(c => c.Total), b => part.Sum(c => c.Bands[b])));
                var heads = new[] { (kind == ContainerKind.Pot ? L["Stats_ClusterPots"] : L["Stats_ClusterBeds"]).Value, L["Stats_Total"].Value }
                    .Concat(Enumerable.Range(0, AllocationStats.BandCount).Select(b => L["Stats_Band" + b].Value)).ToArray();
                var title = $"{L["Report_SecStats"].Value} – {(kind == ContainerKind.Pot ? L["Stats_ClusterPots"] : L["Stats_ClusterBeds"]).Value}";
                tables.Add(new ReportTable(title, heads, rows));
            }
        }

        // ---- not placed and reduced
        if (Has("unplaced") && Result is { } res)
        {
            tables.Add(new ReportTable(L["Report_SecUnplaced"].Value,
                [L["Report_Col_variety"].Value, L["Report_Col_count"].Value, L["Report_Reason"].Value],
                res.Unplaced.Select(u => new[] { PlantName(u.PlantId), u.Count.ToString(culture), Reason(u.Reason) }).ToList()));
            tables.Add(new ReportTable(L["Report_Reduced"].Value,
                [L["Report_Col_variety"].Value, L["Report_Wanted"].Value, L["Report_Placed"].Value],
                res.Reductions.Select(r => new[] { PlantName(r.PlantId), r.Wanted.ToString(culture), r.Placed.ToString(culture) }).ToList()));
        }

        // ---- shopping list
        if (Has("shopping") && Result is { } r2)
        {
            var rows = new List<string[]>();
            foreach (var n in r2.NewPots)
                rows.Add([L["Report_Pot"].Value, $"{n.Liters.ToString("0.#", culture)} l", n.Count.ToString(culture)]);
            foreach (var bed in r2.NewBeds)
                rows.Add([L["Distribution_Bed"].Value, $"{bed.Used.ToString("0.##", culture)} m²", "1"]);
            tables.Add(new ReportTable(L["Report_NewContainers"].Value,
                [L["Report_Col_kind"].Value, L["Report_Size"].Value, L["Report_Col_count"].Value], rows));

            var seeds = Plan.Items
                .SelectMany(i => i.Lines)
                .Where(l => l.ToBuy && l.Plant is not null)
                .Select(l =>
                {
                    var p = PartsOf(l.PlantId);
                    return new[] { p.Type, p.Variety, l.Plants.ToString(culture) };
                })
                .ToList();
            tables.Add(new ReportTable(L["Report_BuySeed"].Value,
                [L["Report_Col_plantType"].Value, L["Report_Col_variety"].Value, L["Report_Col_count"].Value], seeds));
        }

        return new ReportDocument(Plan.Name, $"{Plan.Year} · {L["Report_Generated", DateTime.Now.ToString("d", culture)].Value}", tables);
    }

    public async Task<IActionResult> OnPostApplyAsync(int id)
    {
        var plan = await LoadPlanAsync(id);
        if (plan is null) return NotFound();

        var result = await PlanAllocation.EnsureAsync(db, plan);
        if (result is null) return RedirectToPage("Distribution", new { id });

        var count = await PlanAllocation.ApplyAsync(db, plan, result);
        return RedirectToPage("Distribution", new { id, applied = count });
    }

    private Task<Plan?> LoadPlanAsync(int id) =>
        db.Plans
            .Include(p => p.Containers)
            .Include(p => p.Items).ThenInclude(i => i.PlantType).ThenInclude(t => t!.Translations)
            .Include(p => p.Items).ThenInclude(i => i.Lines).ThenInclude(l => l.Plant).ThenInclude(p => p!.SeasonWindows)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id);

    // ---------------------------------------------------------------- what the page shows

    private async Task ShowAsync(Plan plan)
    {
        Plan = plan;
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        var types = await db.PlantTypes.AsNoTracking().Include(t => t.Translations).ToListAsync();
        var typeNames = types.ToDictionary(
            t => t.Id, t => t.Translations.FirstOrDefault(x => x.Language == language)?.Name ?? t.Name);

        // "Tomato – Moneymaker"
        plantNames = new Dictionary<int, string>();
        plantParts = new Dictionary<int, (string, string)>();
        foreach (var item in plan.Items)
            foreach (var line in item.Lines.Where(l => l.Plant is not null))
            {
                var variety = line.Plant!.Name == "(unnamed)" ? L["Plan_UnnamedVariety"].Value : line.Plant.Name;
                var typeName = typeNames.GetValueOrDefault(item.PlantTypeId, "?");
                plantNames[line.PlantId] = $"{typeName} – {variety}";
                plantParts[line.PlantId] = (typeName, variety);
            }

        ChosenCategories = new HashSet<ContainerCategory>();
        var chosenPots = plan.Containers.Where(c => c.PotId is not null).Select(c => c.PotId!.Value).ToHashSet();
        var chosenBeds = plan.Containers.Where(c => c.RaisedBedId is not null).Select(c => c.RaisedBedId!.Value).ToHashSet();

        // What stands there now (without what this plan planted itself)
        string Occupied(IEnumerable<Planting> plantings) => string.Join(", ", plantings
            .Where(x => x.PlanId != plan.Id)
            .Select(x => typeNames.GetValueOrDefault(x.PlantTypeId, "?"))
            .Distinct().OrderBy(n => n, comparer));

        var groups = await db.PotGroups.AsNoTracking()
            .Include(g => g.Pots).ThenInclude(p => p.Plantings)
            .ToListAsync();
        PotGroups = groups
            .OrderBy(g => g.Name, comparer)
            .Select(g => new PotGroupRow(
                g.Name, g.VolumeLiters, g.Kind,
                g.Pots.OrderBy(p => p.Id).Select((p, i) => new PotRow(
                    p.Id,
                    g.Quantity > 1 ? $"{g.Name} #{i + 1}" : g.Name,
                    Occupied(p.Plantings),
                    chosenPots.Contains(p.Id))).ToList()))
            .ToList();

        foreach (var g in groups.Where(g => g.Pots.Any(p => chosenPots.Contains(p.Id))))
            ChosenCategories.Add(PlaceOrders.CategoryOf(g.Kind));
        if (chosenBeds.Count > 0) ChosenCategories.Add(ContainerCategory.Bed);

        var beds = await db.RaisedBeds.AsNoTracking().Include(b => b.Plantings).ToListAsync();
        Beds = beds
            .OrderBy(b => b.Name, comparer)
            .Select(b => new BedRow(b.Id, b.Name, b.AreaSquareMeters, b.HasCover, Occupied(b.Plantings), chosenBeds.Contains(b.Id)))
            .ToList();
    }

    // "May · mid" for each third of the year
    public IEnumerable<(int Value, string Label)> FrostOptions()
    {
        var names = CultureInfo.CurrentUICulture.DateTimeFormat;
        for (var third = 0; third < OccupancyMask.Count; third++)
        {
            var part = (third % 3) switch { 0 => MonthPart.Early, 1 => MonthPart.Mid, _ => MonthPart.Late };
            yield return (third, $"{names.GetMonthName(third / 3 + 1)} · {L["Part_" + part].Value}");
        }
    }

    /// <summary>One group of pots or beds of the same kind and size, with how many of them are in each fill band
    /// (see <see cref="AllocationStats.BandOf"/>).</summary>
    public record ClusterRow(string Label, ContainerKind Kind, bool IsNew, int Total, int[] Bands);

    // Pots: by pot group (name and litres); beds: by area; new ones separately. Pots first, then beds, large to small.
    public List<ClusterRow> Clusters()
    {
        var groupOf = PotGroups
            .SelectMany(g => g.Pots.Select(p => (p.Id, Label: g.Liters is { } l ? $"{g.Name} · {l:0.#} l" : g.Name)))
            .ToDictionary(x => x.Id, x => x.Label);
        var culture = CultureInfo.CurrentCulture;

        string LabelOf(ContainerResult c)
        {
            if (c.Kind == ContainerKind.Pot)
                return c.IsNew || c.Id is not { } id || !groupOf.TryGetValue(id, out var label)
                    ? $"{c.Capacity.ToString("0.#", culture)} l"
                    : label;
            return $"{L["Distribution_Bed"].Value} · {c.Capacity.ToString("0.##", culture)} m²";
        }

        return (Result?.Containers ?? Array.Empty<ContainerResult>())
            .GroupBy(c => (c.Kind, c.IsNew, Label: LabelOf(c), Capacity: Math.Round(c.Capacity, 2)))
            .OrderBy(g => g.Key.IsNew)
            .ThenBy(g => g.Key.Kind)
            .ThenByDescending(g => g.Key.Capacity)
            .ThenBy(g => g.Key.Label, StringComparer.Create(culture, ignoreCase: true))
            .Select(g =>
            {
                var bands = g.Select(AllocationStats.BandOf).ToList();
                return new ClusterRow(g.Key.Label, g.Key.Kind, g.Key.IsNew, bands.Count,
                    Enumerable.Range(0, AllocationStats.BandCount).Select(b => bands.Count(x => x == b)).ToArray());
            })
            .ToList();
    }

    // ---- the result is shown by kind of container: pots, bowls, grow bags, beds

    /// <summary>The kinds the user chose something from before the distribution.</summary>
    public HashSet<ContainerCategory> ChosenCategories { get; private set; } = new();

    public static readonly ContainerCategory[] CategoryOrder =
        [ContainerCategory.Pot, ContainerCategory.Bowl, ContainerCategory.GrowBag, ContainerCategory.Bed];

    public static ContainerCategory CategoryOf(ContainerResult c) =>
        c.Category ?? (c.Kind == ContainerKind.Bed ? ContainerCategory.Bed : ContainerCategory.Pot);

    /// <summary>The kinds to show details for: something of the kind was chosen, or new containers of it were added.</summary>
    public IEnumerable<ContainerCategory> CategoriesToShow =>
        CategoryOrder.Where(c => ChosenCategories.Contains(c)
                                 || ContainersToShow.Any(x => x.IsNew && CategoryOf(x) == c));

    public IEnumerable<ContainerResult> ContainersOf(ContainerCategory category) =>
        ContainersToShow.Where(c => CategoryOf(c) == category);

    public string CategoryTitle(ContainerCategory category) => category switch
    {
        ContainerCategory.Bed => L["Distribution_Beds"].Value,
        ContainerCategory.Bowl => L["Distribution_Bowls"].Value,
        ContainerCategory.GrowBag => L["Distribution_GrowBags"].Value,
        _ => L["Distribution_Pots"].Value
    };

    // Containers that hold plants, and the plants in them, per kind
    public (int Containers, int Plants) CategoryTotals(ContainerCategory category)
    {
        var list = ContainersOf(category).ToList();
        return (list.Count(c => c.Placements.Count > 0), list.Sum(c => c.Placements.Sum(p => p.Count)));
    }

    public string PlantName(int plantId) => plantNames.GetValueOrDefault(plantId, "?");

    // ---------------------------------------------------------------- texts of the result

    public int WantedPlants => Plan.Items.Sum(i => i.Lines.Sum(l => l.Plants));

    public int PlacedPlants => Result?.Containers.Sum(c => c.Placements.Sum(p => p.Count)) ?? 0;

    public IEnumerable<ContainerResult> ContainersToShow => (Result?.Containers ?? Array.Empty<ContainerResult>())
        .OrderBy(c => c.IsNew)
        .ThenBy(c => c.Kind)
        .ThenBy(c => c.Name, NaturalComparer.Instance);

    // New pots and beds are numbered: "New bed 1", "New bed 2", ...
    public string Title(ContainerResult c)
    {
        if (!c.IsNew) return c.Name;
        var number = (Result?.Containers ?? Array.Empty<ContainerResult>())
            .Where(x => x.IsNew && CategoryOf(x) == CategoryOf(c))
            .ToList()
            .FindIndex(x => ReferenceEquals(x, c)) + 1;
        var name = CategoryOf(c) switch
        {
            ContainerCategory.Bed => L["Distribution_NewBed"].Value,
            ContainerCategory.Bowl => L["Distribution_NewBowl"].Value,
            ContainerCategory.GrowBag => L["Distribution_NewGrowBag"].Value,
            _ => L["Distribution_NewPot"].Value
        };
        return $"{name} {number}";
    }

    // "3.2 / 4 l" or "0.8 / 1.5 m²"
    public string Capacity(ContainerResult c)
    {
        var format = c.Kind == ContainerKind.Pot ? "0.#" : "0.##";
        var unit = c.Kind == ContainerKind.Pot ? "l" : "m²";
        return $"{c.Used.ToString(format, CultureInfo.CurrentCulture)} / {c.Capacity.ToString(format, CultureInfo.CurrentCulture)} {unit}";
    }

    public string Placement(PlacementResult p) =>
        $"{PlantName(p.PlantId)} ×{p.Count}" + (p.Role == PlantingRole.Companion ? $" ({L["Distribution_Under"].Value})" : "")
        + (PlanAllocation.Period(p.Slots) is { } period ? $" · {period}" : "");

    public string Reason(UnplacedReason reason) => L["Unplaced_" + reason].Value;

    public string StrategyText(ShortageStrategy s) => L["Shortage_" + s].Value;
    public string NoCoverText(NoCoverStrategy s) => L["NoCover_" + s].Value;
}
