using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Plants;

public class EditModel(SeedPlannerDbContext db, IStringLocalizer<SharedResource> L) : PageModel
{
    public class InputModel
    {
        [Required]
        public int? PlantTypeId { get; set; }

        [Required, StringLength(200)]
        public string Name { get; set; } = "";

        public Propagation Propagation { get; set; } = Propagation.Seed;

        // Only for seed; empty for young plants and cuttings
        public SeedType? SeedType { get; set; }
        public StockStatus StockStatus { get; set; } = StockStatus.InStock;
        public CultivationMethod? CultivationMethod { get; set; }
        public Exposure? Exposure { get; set; }

        public int? GerminationDaysMin { get; set; }
        public int? GerminationDaysMax { get; set; }
        public int? MaturityDaysMin { get; set; }
        public int? MaturityDaysMax { get; set; }
        public int? RowSpacingCm { get; set; }
        public int? PlantSpacingCm { get; set; }
        public int? PotSizeMinCm { get; set; }
        public int? PotSizeMaxCm { get; set; }

        // up to two sowing and two harvest windows (months 1..12)
        public int? Sow1From { get; set; }
        public int? Sow1To { get; set; }
        public int? Sow2From { get; set; }
        public int? Sow2To { get; set; }
        public int? Harv1From { get; set; }
        public int? Harv1To { get; set; }
        public int? Harv2From { get; set; }
        public int? Harv2To { get; set; }
    }

    public int? Id { get; private set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<SelectListItem> TypeOptions { get; private set; } = new();

    // Plant types of the category flower: their "harvest" window is the flowering time, the page labels it accordingly
    public List<int> FlowerTypeIds { get; private set; } = new();
    public List<SelectListItem> PropagationOptions { get; private set; } = new();
    public List<SelectListItem> SeedTypeOptions { get; private set; } = new();
    public List<SelectListItem> StockOptions { get; private set; } = new();
    public List<SelectListItem> CultivationOptions { get; private set; } = new();
    public List<SelectListItem> ExposureOptions { get; private set; } = new();
    public List<SelectListItem> MonthOptions { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Id = id;
        await LoadOptionsAsync();
        if (id is null) return Page();

        var plant = await db.Plants.AsNoTracking()
            .Include(p => p.SeasonWindows)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (plant is null) return NotFound();

        Input = ToInput(plant);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Id = id;
        await ValidateAsync(id);
        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync();
            return Page();
        }

        Plant? plant;
        if (id is null)
        {
            plant = new Plant { Name = Input.Name.Trim() };
            db.Plants.Add(plant);
        }
        else
        {
            plant = await db.Plants.Include(p => p.SeasonWindows).FirstOrDefaultAsync(p => p.Id == id);
            if (plant is null) return NotFound();
        }

        Apply(plant);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var plant = await db.Plants.FirstOrDefaultAsync(p => p.Id == id);   // filtered: only own plants
        if (plant is not null)
        {
            db.Plants.Remove(plant);   // season windows are deleted with it (cascade)
            await db.SaveChangesAsync();
        }
        return RedirectToPage("Index");
    }

    // ---------------------------------------------------------------- validation

    private async Task ValidateAsync(int? id)
    {
        var name = Input.Name?.Trim() ?? "";
        if (Input.PlantTypeId is not null && name.Length > 0)
        {
            var exists = await db.Plants.AnyAsync(p =>
                p.PlantTypeId == Input.PlantTypeId && p.Name == name && p.Id != (id ?? 0));
            if (exists) ModelState.AddModelError("Input.Name", L["Error_NameExists"]);
        }

        CheckWindow(nameof(Input.Sow1From), Input.Sow1From, Input.Sow1To);
        CheckWindow(nameof(Input.Sow2From), Input.Sow2From, Input.Sow2To);
        CheckWindow(nameof(Input.Harv1From), Input.Harv1From, Input.Harv1To);
        CheckWindow(nameof(Input.Harv2From), Input.Harv2From, Input.Harv2To);

        CheckMinMax(nameof(Input.GerminationDaysMin), Input.GerminationDaysMin, Input.GerminationDaysMax);
        CheckMinMax(nameof(Input.MaturityDaysMin), Input.MaturityDaysMin, Input.MaturityDaysMax);
        CheckMinMax(nameof(Input.PotSizeMinCm), Input.PotSizeMinCm, Input.PotSizeMaxCm);
    }

    // both months or none (a window may run across the new year, so from > to is fine)
    private void CheckWindow(string field, int? from, int? to)
    {
        if ((from is null) != (to is null))
            ModelState.AddModelError($"Input.{field}", L["Error_WindowIncomplete"]);
    }

    private void CheckMinMax(string field, int? min, int? max)
    {
        if (min is not null && max is not null && min > max)
            ModelState.AddModelError($"Input.{field}", L["Error_MinGreaterMax"]);
    }

    // ---------------------------------------------------------------- mapping

    private void Apply(Plant plant)
    {
        plant.PlantTypeId = Input.PlantTypeId!.Value;
        plant.Name = Input.Name.Trim();
        plant.Propagation = Input.Propagation;
        plant.SeedType = Input.Propagation == Propagation.Seed ? Input.SeedType : null;
        plant.StockStatus = Input.StockStatus;
        plant.CultivationMethod = Input.CultivationMethod;
        plant.Exposure = Input.Exposure;
        plant.GerminationDaysMin = Input.GerminationDaysMin;
        plant.GerminationDaysMax = Input.GerminationDaysMax;
        plant.MaturityDaysMin = Input.MaturityDaysMin;
        plant.MaturityDaysMax = Input.MaturityDaysMax;
        plant.RowSpacingCm = Input.RowSpacingCm;
        plant.PlantSpacingCm = Input.PlantSpacingCm;
        plant.PotSizeMinCm = Input.PotSizeMinCm;
        plant.PotSizeMaxCm = Input.PotSizeMaxCm;

        // rewrite the season windows
        db.SeasonWindows.RemoveRange(plant.SeasonWindows);
        plant.SeasonWindows.Clear();
        AddWindow(plant, WindowKind.Sowing, Input.Sow1From, Input.Sow1To);
        AddWindow(plant, WindowKind.Sowing, Input.Sow2From, Input.Sow2To);
        AddWindow(plant, WindowKind.Harvest, Input.Harv1From, Input.Harv1To);
        AddWindow(plant, WindowKind.Harvest, Input.Harv2From, Input.Harv2To);
    }

    private static void AddWindow(Plant plant, WindowKind kind, int? from, int? to)
    {
        if (from is null || to is null) return;
        plant.SeasonWindows.Add(new SeasonWindow { Kind = kind, StartMonth = from.Value, EndMonth = to.Value });
    }

    private static InputModel ToInput(Plant plant)
    {
        var sowing = plant.SeasonWindows.Where(w => w.Kind == WindowKind.Sowing).OrderBy(w => w.Id).ToList();
        var harvest = plant.SeasonWindows.Where(w => w.Kind == WindowKind.Harvest).OrderBy(w => w.Id).ToList();

        return new InputModel
        {
            PlantTypeId = plant.PlantTypeId,
            Name = plant.Name,
            Propagation = plant.Propagation,
            SeedType = plant.SeedType,
            StockStatus = plant.StockStatus,
            CultivationMethod = plant.CultivationMethod,
            Exposure = plant.Exposure,
            GerminationDaysMin = plant.GerminationDaysMin,
            GerminationDaysMax = plant.GerminationDaysMax,
            MaturityDaysMin = plant.MaturityDaysMin,
            MaturityDaysMax = plant.MaturityDaysMax,
            RowSpacingCm = plant.RowSpacingCm,
            PlantSpacingCm = plant.PlantSpacingCm,
            PotSizeMinCm = plant.PotSizeMinCm,
            PotSizeMaxCm = plant.PotSizeMaxCm,
            Sow1From = sowing.ElementAtOrDefault(0)?.StartMonth,
            Sow1To = sowing.ElementAtOrDefault(0)?.EndMonth,
            Sow2From = sowing.ElementAtOrDefault(1)?.StartMonth,
            Sow2To = sowing.ElementAtOrDefault(1)?.EndMonth,
            Harv1From = harvest.ElementAtOrDefault(0)?.StartMonth,
            Harv1To = harvest.ElementAtOrDefault(0)?.EndMonth,
            Harv2From = harvest.ElementAtOrDefault(1)?.StartMonth,
            Harv2To = harvest.ElementAtOrDefault(1)?.EndMonth,
        };
    }

    // ---------------------------------------------------------------- dropdown lists

    private async Task LoadOptionsAsync()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);

        var types = await db.PlantTypes.AsNoTracking().Include(t => t.Translations).ToListAsync();
        TypeOptions = types
            .Select(t => new
            {
                t.Id,
                Name = t.Translations.FirstOrDefault(x => x.Language == language)?.Name ?? t.Name
            })
            .OrderBy(t => t.Name, comparer)
            .Select(t => new SelectListItem(t.Name, t.Id.ToString()))
            .ToList();
        TypeOptions.Insert(0, new SelectListItem("–", ""));
        FlowerTypeIds = types.Where(t => t.Category == PlantCategory.Flower).Select(t => t.Id).ToList();

        PropagationOptions = EnumOptions<Propagation>();
        SeedTypeOptions = EnumOptions<SeedType>(optional: true);
        StockOptions = EnumOptions<StockStatus>();
        CultivationOptions = EnumOptions<CultivationMethod>(optional: true);
        ExposureOptions = EnumOptions<Exposure>(optional: true);

        MonthOptions = Enumerable.Range(1, 12)
            .Select(m => new SelectListItem(CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m), m.ToString()))
            .ToList();
        MonthOptions.Insert(0, new SelectListItem("–", ""));
    }

    // Resource keys follow the pattern "<EnumName>_<Value>", e.g. "StockStatus_InStock"
    private List<SelectListItem> EnumOptions<T>(bool optional = false) where T : struct, Enum
    {
        var items = Enum.GetValues<T>()
            .Select(v => new SelectListItem(L[$"{typeof(T).Name}_{v}"].Value, v.ToString()))
            .ToList();
        if (optional) items.Insert(0, new SelectListItem("–", ""));
        return items;
    }
}
