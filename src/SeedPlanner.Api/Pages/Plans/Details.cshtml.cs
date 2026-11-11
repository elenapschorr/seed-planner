using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Plans;

// The result of a plan: crops and varieties with their numbers. The user can change the numbers and the choice of varieties.
public class DetailsModel(SeedPlannerDbContext db, IStringLocalizer<SharedResource> L) : PageModel
{
    public class ItemInput
    {
        public int Id { get; set; }
        public int? PlantsOverride { get; set; }
    }

    public class LineInput
    {
        public int Id { get; set; }
        public bool Include { get; set; }
        public int? PlantsOverride { get; set; }
    }

    public Plan Plan { get; private set; } = null!;

    public List<PlanItem> Crops { get; private set; } = new();

    [BindProperty]
    public List<ItemInput> Items { get; set; } = new();

    [BindProperty]
    public List<LineInput> Lines { get; set; } = new();

    public bool Saved => Request.Query["saved"] == "1";

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var plan = await LoadAsync(id);
        if (plan is null) return NotFound();
        Show(plan);
        return Page();
    }

    // Takes over the changes, then calculates again (the spacing depends on which varieties are included)
    public async Task<IActionResult> OnPostSaveAsync(int id)
    {
        var plan = await LoadAsync(id);
        if (plan is null) return NotFound();

        foreach (var input in Items)
        {
            var item = plan.Items.FirstOrDefault(i => i.Id == input.Id);
            if (item is not null) item.PlantsOverride = input.PlantsOverride is >= 0 ? input.PlantsOverride : null;
        }
        var lines = plan.Items.SelectMany(i => i.Lines).ToList();
        foreach (var input in Lines)
        {
            var line = lines.FirstOrDefault(l => l.Id == input.Id);
            if (line is null) continue;
            line.Include = input.Include;
            line.PlantsOverride = input.PlantsOverride is >= 0 ? input.PlantsOverride : null;
        }

        await Calculation.RebuildAsync(db, plan);
        await db.SaveChangesAsync();
        return RedirectToPage("Details", new { id, saved = 1 });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        // The result of the plan is deleted with it (cascade)
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == id);
        if (plan is not null)
        {
            db.Plans.Remove(plan);
            await db.SaveChangesAsync();
        }
        return RedirectToPage("Index");
    }

    private Task<Plan?> LoadAsync(int id) =>
        db.Plans
            .Include(p => p.Items).ThenInclude(i => i.PlantType).ThenInclude(t => t!.Translations)
            .Include(p => p.Items).ThenInclude(i => i.Lines).ThenInclude(l => l.Plant)
            .FirstOrDefaultAsync(p => p.Id == id);

    private void Show(Plan plan)
    {
        Plan = plan;
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        Crops = plan.Items.OrderBy(CropName, comparer).ToList();
    }

    // ---------------------------------------------------------------- texts for the page

    public string CropName(PlanItem item)
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var type = item.PlantType!;
        return type.Translations.FirstOrDefault(t => t.Language == language)?.Name ?? type.Name;
    }

    // The import stores "(unnamed)" for seed packets without a variety name
    public string VarietyName(PlanLine line) =>
        line.Plant is null || line.Plant.Name == "(unnamed)" ? L["Plan_UnnamedVariety"].Value : line.Plant.Name;

    public IEnumerable<PlanLine> LinesOf(PlanItem item) =>
        item.Lines.OrderBy(l => VarietyName(l), StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true));

    // "6 Quadratmeter"
    public string AmountText(PlanItem item) =>
        item.DemandAmount is null
            ? "–"
            : $"{item.DemandAmount.Value.ToString("0.##", CultureInfo.CurrentCulture)} {(item.Unit is null ? "" : L["DemandUnit_" + item.Unit].Value)}".Trim();

    public string LevelText => Plan.Level == DemandLevel.Custom
        ? L["DemandLevel_CustomShort", Plan.CustomPercent].Value
        : L["DemandLevel_" + Plan.Level].Value;

    public int TotalPlants => Plan.Items.Sum(i => i.Lines.Sum(l => l.Plants));

    /// <summary>Crops with at least one variety in the plan.</summary>
    public int IncludedCrops => Plan.Items.Count(i => i.IsIncluded);

    // State of a crop for the page: all, some or none of its varieties are part of the plan
    public static string StateOf(PlanItem item) => item.IsFullyIncluded ? "full" : item.IsPartlyIncluded ? "partial" : "none";
}
