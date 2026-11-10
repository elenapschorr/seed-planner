using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Demand;

// The demand per person for every crop: the default of the plant type, and the user's own value instead
public class IndexModel(SeedPlannerDbContext db, IStringLocalizer<SharedResource> L) : PageModel
{
    public class RowInput
    {
        public int TypeId { get; set; }
        public decimal? Min { get; set; }
        public decimal? Max { get; set; }
        public DemandUnit? Unit { get; set; }

        public bool IsEmpty => Min is null && Max is null && Unit is null;
    }

    // Default: the demand stored with the plant type, as text ("2 – 4 Pflanzen")
    public record Row(int TypeId, string Name, string? Default, bool HasOwn);

    public List<Row> Rows { get; private set; } = new();

    [BindProperty]
    public List<RowInput> Inputs { get; set; } = new();

    // Error text per row (index in Inputs)
    public Dictionary<int, string> RowErrors { get; } = new();

    public bool Saved => Request.Query["saved"] == "1";

    public async Task OnGetAsync()
    {
        var own = await db.UserDemands.AsNoTracking().ToDictionaryAsync(d => d.PlantTypeId);
        await LoadRowsAsync(own);
        Inputs = Rows.Select(r => own.TryGetValue(r.TypeId, out var d)
                ? new RowInput { TypeId = r.TypeId, Min = d.Min, Max = d.Max, Unit = d.Unit }
                : new RowInput { TypeId = r.TypeId })
            .ToList();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var own = await db.UserDemands.ToDictionaryAsync(d => d.PlantTypeId);
        await LoadRowsAsync(own);

        // Only crops that are in the list can be changed (flowers have no demand)
        var allowed = Rows.Select(r => r.TypeId).ToHashSet();
        var inputs = Inputs.Where(i => allowed.Contains(i.TypeId)).ToList();

        for (var i = 0; i < Inputs.Count; i++)
        {
            var input = Inputs[i];
            if (!allowed.Contains(input.TypeId) || input.IsEmpty) continue;
            if (input.Min < 0 || input.Max < 0) RowErrors[i] = L["Demand_ErrorNegative"];
            else if (input.Min is not null && input.Max is not null && input.Min > input.Max) RowErrors[i] = L["Demand_ErrorMinMax"];
            else if (input.Unit is null) RowErrors[i] = L["PlantTypeError_UnitMissing"];
            else if (input.Min is null && input.Max is null) RowErrors[i] = L["Demand_ErrorAmount"];
        }
        if (RowErrors.Count > 0) return Page();

        foreach (var input in inputs)
        {
            own.TryGetValue(input.TypeId, out var existing);
            if (input.IsEmpty)
            {
                // Empty row: back to the default of the plant type
                if (existing is not null) db.UserDemands.Remove(existing);
                continue;
            }
            if (existing is null)
            {
                existing = new UserDemand { PlantTypeId = input.TypeId };
                db.UserDemands.Add(existing);
            }
            existing.Min = input.Min;
            existing.Max = input.Max;
            existing.Unit = input.Unit;
        }
        await db.SaveChangesAsync();
        return RedirectToPage("Index", new { saved = 1 });
    }

    private async Task LoadRowsAsync(Dictionary<int, UserDemand> own)
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);

        var types = await db.PlantTypes.AsNoTracking().Include(t => t.Translations)
            .Where(t => t.Category != PlantCategory.Flower)
            .ToListAsync();
        Rows = types
            .Select(t => new Row(
                t.Id,
                t.Translations.FirstOrDefault(x => x.Language == language)?.Name ?? t.Name,
                Describe(t.DemandPerPersonMin, t.DemandPerPersonMax, t.DemandUnit),
                own.ContainsKey(t.Id)))
            .OrderBy(r => r.Name, comparer)
            .ToList();
    }

    // "2 – 4 Pflanzen", "3 Quadratmeter"; nothing if there is no demand
    private string? Describe(decimal? min, decimal? max, DemandUnit? unit)
    {
        if (min is null && max is null) return null;
        static string F(decimal v) => v.ToString("0.##", CultureInfo.CurrentCulture);
        var range = min is not null && max is not null && min != max ? $"{F(min.Value)} – {F(max.Value)}" : F((min ?? max)!.Value);
        return unit is null ? range : $"{range} {L["DemandUnit_" + unit].Value}";
    }
}
