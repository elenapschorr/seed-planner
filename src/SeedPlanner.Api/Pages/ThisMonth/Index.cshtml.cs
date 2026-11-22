using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.ThisMonth;

/// <summary>What there is to do in a month for the varieties of a plan: sow, sow indoors, plant out and harvest.</summary>
public class IndexModel(SeedPlannerDbContext db) : PageModel
{
    public record PlanOption(int Id, string Name, DateOnly ValidFrom, DateOnly ValidTo);

    public record TaskRow(int LineId, TaskKind Kind, string Crop, string Variety, bool IsUnnamed, int Plants, MonthPart Part,
        bool Done, bool Overdue, string DueMonthName, int Harvested);

    /// <summary>A variety from the collection that can be sown in the month and is not among the sowing jobs of the plan.</summary>
    public record SowableRow(int Id, string Crop, string Variety, bool IsUnnamed);

    [BindProperty(SupportsGet = true)]
    public int? PlanId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Month { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    public List<PlanOption> Plans { get; private set; } = new();

    public int MonthNumber { get; private set; }
    public int YearNumber { get; private set; }

    /// <summary>The month shown (first day).</summary>
    public DateOnly Shown => new(YearNumber, MonthNumber, 1);
    public DateOnly PreviousShown => Shown.AddMonths(-1);
    public DateOnly NextShown => Shown.AddMonths(1);

    /// <summary>Whether the shown month is before, inside or after the period of the chosen plan.</summary>
    public enum PlanPeriod { None, NotStarted, Active, Ended }

    public PlanPeriod Period { get; private set; }
    public DateOnly? PlanStart { get; private set; }
    public DateOnly? PlanEnd { get; private set; }

    public string MonthName => MonthNameOf(MonthNumber);

    private static string MonthNameOf(int month)
    {
        var name = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month);
        return name.Length == 0 ? name : char.ToUpper(name[0], CultureInfo.CurrentCulture) + name[1..];
    }

    /// <summary>The jobs of the month, one entry per kind of job (only kinds that have jobs).</summary>
    public List<(TaskKind Kind, List<TaskRow> Rows)> Groups { get; private set; } = new();

    /// <summary>Varieties in stock that can be sown now (by their sowing window) and that the plan does not already sow in this month.</summary>
    public List<SowableRow> Sowable { get; private set; } = new();

    public async Task OnGetAsync()
    {
        MonthNumber = Month is >= 1 and <= 12 ? Month.Value : DateTime.Today.Month;
        YearNumber = Year is >= 2000 and <= 2100 ? Year.Value : DateTime.Today.Year;
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var alreadySown = new HashSet<int>();
        await LoadPlanTasksAsync(language, alreadySown);
        await LoadSowableAsync(language, alreadySown);
    }

    private async Task LoadPlanTasksAsync(string language, HashSet<int> alreadySown)
    {
        var plans = await db.Plans.AsNoTracking().OrderByDescending(p => p.ValidFrom).ThenByDescending(p => p.CreatedAt).ToListAsync();
        Plans = plans.Select(p => new PlanOption(p.Id, p.Name, p.ValidFrom, p.ValidTo)).ToList();

        var today = DateOnly.FromDateTime(DateTime.Today);
        // Without a choice: the newest plan valid today, otherwise the newest plan there is
        var chosen = plans.FirstOrDefault(p => p.Id == PlanId)
            ?? plans.FirstOrDefault(p => p.ValidFrom <= today && today <= p.ValidTo)
            ?? plans.FirstOrDefault();
        if (chosen is null) return;
        PlanId = chosen.Id;

        // The jobs of a plan are only shown for the months in which the plan is valid
        (PlanStart, PlanEnd) = (chosen.ValidFrom, chosen.ValidTo);
        var shownEnd = Shown.AddMonths(1).AddDays(-1);
        Period = shownEnd < chosen.ValidFrom ? PlanPeriod.NotStarted : Shown > chosen.ValidTo ? PlanPeriod.Ended : PlanPeriod.Active;
        if (Period != PlanPeriod.Active) return;

        var plan = await db.Plans.AsNoTracking()
            .Include(p => p.Items).ThenInclude(i => i.PlantType).ThenInclude(t => t!.Translations)
            .Include(p => p.Items).ThenInclude(i => i.Lines).ThenInclude(l => l.Plant).ThenInclude(p => p!.SeasonWindows)
            .FirstAsync(p => p.Id == chosen.Id);

        var cropOf = new Dictionary<int, string>();
        var sources = new List<TaskSource>();
        foreach (var item in plan.Items)
        {
            var crop = item.PlantType?.Translations.FirstOrDefault(t => t.Language == language)?.Name ?? item.PlantType?.Name ?? "";
            foreach (var line in item.Lines.Where(l => l.Include && l.Plant is not null))
            {
                cropOf[line.PlantId] = crop;
                sources.Add(new TaskSource(line.Plant!, line.Plants, item.SowingTiming, line.Id, line.Progress));
            }
        }

        var tasks = MonthlyTasks.For(sources, MonthNumber, plan.LastFrostThird, carryOver: true, firstMonth: plan.ValidFrom.Month);
        // What the plan sows in this month is listed above and not again below
        foreach (var task in tasks.Where(t => t.Kind is TaskKind.Sow or TaskKind.SowIndoors)) alreadySown.Add(task.Plant.Id);
        Groups = Enum.GetValues<TaskKind>()
            .Select(kind => (kind, tasks.Where(t => t.Kind == kind)
                .Select(t => new TaskRow(t.LineId, t.Kind, cropOf.GetValueOrDefault(t.Plant.Id, ""), t.Plant.Name, IsUnnamed(t.Plant.Name), t.Plants, t.Part,
                    t.Done, t.Overdue, MonthNameOf(t.DueMonth), t.Harvested))
                .ToList()))
            .Where(g => g.Item2.Count > 0)
            .ToList();
    }

    /// <summary>The value of the "what" field of the button that marks the job as done, and of the one that takes the mark back.</summary>
    public string DoOf(TaskKind kind) => kind == TaskKind.PlantOut ? "plantedout" : "sown";

    public string UndoOf(TaskKind kind) => "un" + DoOf(kind);

    /// <summary>Marks a job of a plan line as done or takes the mark back; <paramref name="plants"/> is the number of harvested plants.</summary>
    public async Task<IActionResult> OnPostMarkAsync(int lineId, string what, int? plants, int? planId, int? month, int? year)
    {
        var line = await db.PlanLines.FirstOrDefaultAsync(l => l.Id == lineId);   // the query filter only finds the lines of the user
        if (line is null) return NotFound();

        var today = DateOnly.FromDateTime(DateTime.Today);
        switch (what)
        {
            case "sown": line.SownOn = today; break;
            case "unsown": line.SownOn = null; break;
            case "plantedout": line.PlantedOutOn = today; break;
            case "unplantedout": line.PlantedOutOn = null; break;
            case "harvested":
                line.HarvestedPlants = Math.Clamp(plants ?? line.HarvestedPlants, 0, Math.Max(line.Plants, 0));
                break;
            case "harvestfinished": line.HarvestDoneOn = today; break;
            case "harvestreset": line.HarvestedPlants = 0; line.HarvestDoneOn = null; break;
            default: return BadRequest();
        }
        await db.SaveChangesAsync();
        return RedirectToPage(new { planId, month, year });
    }

    private async Task LoadSowableAsync(string language, HashSet<int> alreadySown)
    {
        var plants = await db.Plants.AsNoTracking()
            .Include(p => p.SeasonWindows)
            .Include(p => p.PlantType).ThenInclude(t => t.Translations)
            .ToListAsync();

        Sowable = MonthlyTasks.SowableIn(plants, MonthNumber, alreadySown)
            .Select(p => new SowableRow(p.Id, p.PlantType.Translations.FirstOrDefault(t => t.Language == language)?.Name ?? p.PlantType.Name, p.Name, IsUnnamed(p.Name)))
            .ToList();
    }

    private static bool IsUnnamed(string name) => string.IsNullOrWhiteSpace(name) || name == "(unnamed)";
}
