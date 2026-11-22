using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Plans;

// The settings of a plan. Saving calculates the result; own numbers and choices of an existing plan are kept.
public class EditModel(SeedPlannerDbContext db, IStringLocalizer<SharedResource> L) : PageModel
{
    public class InputModel
    {
        [Required, StringLength(100)]
        public string Name { get; set; } = "";

        [DataType(DataType.Date)]
        public DateOnly ValidFrom { get; set; } = new(DateTime.Today.Year, 1, 1);

        [DataType(DataType.Date)]
        public DateOnly ValidTo { get; set; } = new(DateTime.Today.Year, 12, 31);

        public int Persons { get; set; } = 2;
        public DemandLevel Level { get; set; } = DemandLevel.Middle;
        public int CustomPercent { get; set; } = 50;
        public bool IncludeOutOfStock { get; set; }
    }

    public int? Id { get; private set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<SelectListItem> LevelOptions { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Id = id;
        if (id is not null)
        {
            var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            if (plan is null) return NotFound();
            Input = new InputModel
            {
                Name = plan.Name, ValidFrom = plan.ValidFrom, ValidTo = plan.ValidTo, Persons = plan.Persons, Level = plan.Level,
                CustomPercent = plan.CustomPercent, IncludeOutOfStock = plan.IncludeOutOfStock
            };
        }
        Prepare();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Id = id;
        Plan? plan = null;
        if (id is not null)
        {
            plan = await db.Plans.Include(p => p.Items).ThenInclude(i => i.Lines).FirstOrDefaultAsync(p => p.Id == id);
            if (plan is null) return NotFound();
        }

        Input.Name = Input.Name?.Trim() ?? "";
        if (Input.Name.Length > 0 &&
            await db.Plans.AnyAsync(p => p.ValidFrom == Input.ValidFrom && p.Name == Input.Name && p.Id != (id ?? 0)))
            ModelState.AddModelError("Input.Name", L["PlanError_NameExists"]);
        if (Input.ValidFrom.Year is < 2000 or > 2100) ModelState.AddModelError("Input.ValidFrom", L["PlanError_Year"]);
        else if (Input.ValidTo < Input.ValidFrom) ModelState.AddModelError("Input.ValidTo", L["PlanError_Period"]);
        else if (Input.ValidTo > Input.ValidFrom.AddYears(1).AddDays(-1)) ModelState.AddModelError("Input.ValidTo", L["PlanError_PeriodLong"]);
        if (Input.Persons < 1) ModelState.AddModelError("Input.Persons", L["PlanError_Persons"]);
        if (Input.CustomPercent is < 0 or > 100) ModelState.AddModelError("Input.CustomPercent", L["PlanError_Percent"]);
        if (!ModelState.IsValid)
        {
            Prepare();
            return Page();
        }

        if (plan is null)
        {
            plan = new Plan();
            db.Plans.Add(plan);
        }
        plan.Name = Input.Name;
        plan.ValidFrom = Input.ValidFrom;
        plan.ValidTo = Input.ValidTo;
        plan.Persons = Input.Persons;
        plan.Level = Input.Level;
        plan.CustomPercent = Input.CustomPercent;
        plan.IncludeOutOfStock = Input.IncludeOutOfStock;

        await Calculation.RebuildAsync(db, plan);
        await db.SaveChangesAsync();
        return RedirectToPage("Details", new { id = plan.Id });
    }

    private void Prepare() =>
        LevelOptions = Enum.GetValues<DemandLevel>()
            .Select(v => new SelectListItem(L["DemandLevel_" + v].Value, v.ToString())).ToList();
}

/// <summary>Loads what the calculation needs (plant types with the varieties of the user, the own demand) and runs it.</summary>
public static class Calculation
{
    public static async Task RebuildAsync(SeedPlannerDbContext db, Plan plan)
    {
        // The query filter on varieties only finds the ones of the signed-in user
        var types = await db.PlantTypes.Include(t => t.Plants).ToListAsync();
        var own = await db.UserDemands.ToDictionaryAsync(d => d.PlantTypeId);
        PlanCalculator.Rebuild(plan, types, own);
    }
}
