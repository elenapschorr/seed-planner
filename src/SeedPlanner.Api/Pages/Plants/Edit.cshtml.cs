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
        public SowingMethod? SowingMethod { get; set; }
        public GrowingPlace? GrowingPlace { get; set; }
        public Exposure? Exposure { get; set; }

        public int? GerminationDaysMin { get; set; }
        public int? GerminationDaysMax { get; set; }
        public int? GerminationTempMinC { get; set; }
        public int? GerminationTempMaxC { get; set; }
        public int? MaturityDaysMin { get; set; }
        public int? IndoorWeeksMin { get; set; }
        public int? IndoorWeeksMax { get; set; }
        public bool FrostSensitive { get; set; }
        public int? MaturityDaysMax { get; set; }
        public int? RowSpacingCm { get; set; }
        public int? PlantSpacingCm { get; set; }
        public int? PotSizeMinLiters { get; set; }
        public int? PotSizeMaxLiters { get; set; }
        public int? HeightMinCm { get; set; }
        public int? HeightMaxCm { get; set; }
        public int? VineLengthMaxCm { get; set; }

        // Up to two windows per kind; the second one is added by the user on demand
        public List<WindowInput> Sowing { get; set; } = [new(), new()];
        public List<WindowInput> Harvest { get; set; } = [new(), new()];
        public List<WindowInput> Bloom { get; set; } = [new(), new()];
    }

    // One window: from month (and part of it) to month (and part of it)
    public class WindowInput
    {
        public int? FromMonth { get; set; }
        public MonthPart FromPart { get; set; } = MonthPart.Whole;
        public int? ToMonth { get; set; }
        public MonthPart ToPart { get; set; } = MonthPart.Whole;
    }

    // What the window partial needs
    public record WindowSectionModel(
        string Id, string Prefix, string Title, List<WindowInput> Items,
        List<SelectListItem> Months, List<SelectListItem> Parts);

    public int? Id { get; private set; }

    // The list page (with its filters) the user came from
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string BackUrl =>
        !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : Url.Page("Index")!;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<SelectListItem> TypeOptions { get; private set; } = new();

    // Plant types of the category flower: their "harvest" window is the flowering time, the page labels it accordingly
    public List<int> FlowerTypeIds { get; private set; } = new();
    public List<SelectListItem> PropagationOptions { get; private set; } = new();
    public List<SelectListItem> SeedTypeOptions { get; private set; } = new();
    public List<SelectListItem> StockOptions { get; private set; } = new();
    public List<SelectListItem> SowingOptions { get; private set; } = new();
    public List<SelectListItem> PlaceOptions { get; private set; } = new();
    public List<SelectListItem> ExposureOptions { get; private set; } = new();
    public List<SelectListItem> MonthOptions { get; private set; } = new();
    public List<SelectListItem> PartOptions { get; private set; } = new();

    // Where this variety grows: one entry per planting (a pot or a bed)
    public record PlantingRow(int Id, string Where, int? PlantCount);

    public List<PlantingRow> Plantings { get; private set; } = new();
    public List<SelectListItem> GroupOptions { get; private set; } = new();
    public List<SelectListItem> BedOptions { get; private set; } = new();

    // Message for the planting section (shown right there)
    public string? PlantingError { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Id = id;
        await LoadOptionsAsync();
        if (id is null) return Page();
        await LoadPlantingsAsync(id.Value);

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
        return LocalRedirect(BackUrl);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var plant = await db.Plants.FirstOrDefaultAsync(p => p.Id == id);   // filtered: only own plants
        if (plant is not null)
        {
            db.Plants.Remove(plant);   // season windows are deleted with it (cascade)
            await db.SaveChangesAsync();
        }
        return LocalRedirect(BackUrl);
    }

    // ---------------------------------------------------------------- where the variety grows

    // Plants the variety into some free pots of a group
    public async Task<IActionResult> OnPostAddToPotsAsync(int id, int groupId, int quantity, int? plantCount)
    {
        var plant = await db.Plants.FirstOrDefaultAsync(p => p.Id == id);   // filtered: only own plants
        if (plant is null) return NotFound();

        var group = await db.PotGroups.Include(g => g.Pots).ThenInclude(p => p.Plantings)
            .FirstOrDefaultAsync(g => g.Id == groupId);
        var free = group?.Pots.Where(p => p.IsFree).OrderBy(p => p.Id).ToList() ?? new();

        string? error = null;
        if (group is null) error = L["PotError_UseInvalid"];
        else if (quantity < 1) error = L["PotError_UseQuantity"];
        else if (quantity > free.Count) error = L["PotError_UseTooMany", free.Count];
        else if (plantCount is < 1) error = L["PotError_PlantCount"];
        if (error is not null) return await PageWithPlantingErrorAsync(plant, error);

        foreach (var pot in free.Take(quantity))
        {
            pot.Plantings.Add(new Planting { PlantTypeId = plant.PlantTypeId, PlantId = plant.Id, PlantCount = plantCount });
        }
        await db.SaveChangesAsync();
        return RedirectToPage("Edit", new { id, returnUrl = ReturnUrl });
    }

    // Plants the variety into a bed (raised bed, ground bed or planting ring)
    public async Task<IActionResult> OnPostAddToBedAsync(int id, int bedId, int? plantCount)
    {
        var plant = await db.Plants.FirstOrDefaultAsync(p => p.Id == id);
        if (plant is null) return NotFound();

        var bed = await db.RaisedBeds.Include(b => b.Plantings).FirstOrDefaultAsync(b => b.Id == bedId);

        string? error = null;
        if (bed is null) error = L["PotError_UseInvalid"];
        else if (plantCount is < 1) error = L["PotError_PlantCount"];
        else if (bed.Plantings.Any(x => x.PlantId == plant.Id)) error = L["PotError_AlreadyThere"];
        if (error is not null) return await PageWithPlantingErrorAsync(plant, error);

        bed!.Plantings.Add(new Planting { PlantTypeId = plant.PlantTypeId, PlantId = plant.Id, PlantCount = plantCount });
        await db.SaveChangesAsync();
        return RedirectToPage("Edit", new { id, returnUrl = ReturnUrl });
    }

    public async Task<IActionResult> OnPostRemovePlantingAsync(int id, int plantingId)
    {
        // The query filter only finds the user's own plantings
        var planting = await db.Plantings.FirstOrDefaultAsync(x => x.Id == plantingId && x.PlantId == id);
        if (planting is not null)
        {
            db.Plantings.Remove(planting);
            await db.SaveChangesAsync();
        }
        return RedirectToPage("Edit", new { id, returnUrl = ReturnUrl });
    }

    private async Task<IActionResult> PageWithPlantingErrorAsync(Plant plant, string error)
    {
        Id = plant.Id;
        var full = await db.Plants.AsNoTracking().Include(p => p.SeasonWindows).FirstAsync(p => p.Id == plant.Id);
        Input = ToInput(full);
        PlantingError = error;
        await LoadOptionsAsync();
        await LoadPlantingsAsync(plant.Id);
        return Page();
    }

    private async Task LoadPlantingsAsync(int plantId)
    {
        var groups = await db.PotGroups.AsNoTracking()
            .Include(g => g.Pots).ThenInclude(p => p.Plantings)
            .OrderBy(g => g.Name).ToListAsync();

        GroupOptions = groups
            .Where(g => g.FreeQuantity > 0)
            .Select(g => new SelectListItem(L["Where_FreeOf", g.Name, g.FreeQuantity].Value, g.Id.ToString()))
            .ToList();

        var beds = await db.RaisedBeds.AsNoTracking().OrderBy(b => b.Name).ToListAsync();
        BedOptions = beds.Select(b => new SelectListItem(b.Name, b.Id.ToString())).ToList();

        var plantings = await db.Plantings.AsNoTracking().Where(x => x.PlantId == plantId).ToListAsync();
        var rows = new List<PlantingRow>();
        foreach (var x in plantings)
        {
            if (x.PotId is not null)
            {
                var group = groups.First(g => g.Pots.Any(p => p.Id == x.PotId));
                var number = group.Pots.OrderBy(p => p.Id).ToList().FindIndex(p => p.Id == x.PotId) + 1;
                rows.Add(new PlantingRow(x.Id, $"{group.Name}, {L["Pot_Number", number]}", x.PlantCount));
            }
            else if (x.RaisedBedId is not null)
            {
                rows.Add(new PlantingRow(x.Id, beds.First(b => b.Id == x.RaisedBedId).Name, x.PlantCount));
            }
        }
        Plantings = rows.OrderBy(r => r.Where, StringComparer.CurrentCultureIgnoreCase).ToList();
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

        CheckWindows("Sowing", Input.Sowing);
        CheckWindows("Harvest", Input.Harvest);
        CheckWindows("Bloom", Input.Bloom);

        CheckMinMax(nameof(Input.GerminationDaysMin), Input.GerminationDaysMin, Input.GerminationDaysMax);
        CheckMinMax(nameof(Input.GerminationTempMinC), Input.GerminationTempMinC, Input.GerminationTempMaxC);
        CheckMinMax(nameof(Input.MaturityDaysMin), Input.MaturityDaysMin, Input.MaturityDaysMax);
        CheckMinMax(nameof(Input.IndoorWeeksMin), Input.IndoorWeeksMin, Input.IndoorWeeksMax);
        CheckMinMax(nameof(Input.PotSizeMinLiters), Input.PotSizeMinLiters, Input.PotSizeMaxLiters);
        CheckMinMax(nameof(Input.HeightMinCm), Input.HeightMinCm, Input.HeightMaxCm);

        // The pot size is optional, but required for pot culture
        if (Input.GrowingPlace == GrowingPlace.Container && (Input.PotSizeMinLiters is null || Input.PotSizeMaxLiters is null))
            ModelState.AddModelError($"Input.{nameof(Input.PotSizeMinLiters)}", L["Error_PotSizeRequired"]);
    }

    // Both months or none (a window may run across the new year, so from > to is fine).
    // Within one month the end part must not come before the start part.
    private void CheckWindows(string name, List<WindowInput> windows)
    {
        for (var i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            var key = $"Input.{name}[{i}].FromMonth";

            if ((w.FromMonth is null) != (w.ToMonth is null))
            {
                ModelState.AddModelError(key, L["Error_WindowIncomplete"]);
                continue;
            }
            if (w.FromMonth is null || w.ToMonth is null) continue;

            if (w.FromMonth is < 1 or > 12 || w.ToMonth is < 1 or > 12)
            {
                ModelState.AddModelError(key, L["Error_WindowIncomplete"]);
                continue;
            }

            var window = new SeasonWindow
            {
                StartMonth = w.FromMonth.Value, StartPart = w.FromPart,
                EndMonth = w.ToMonth.Value, EndPart = w.ToPart
            };
            if (window.StartMonth == window.EndMonth && window.StartThird > window.EndThird)
                ModelState.AddModelError(key, L["Error_WindowOrder"]);
        }
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
        plant.SowingMethod = Input.SowingMethod;
        plant.GrowingPlace = Input.GrowingPlace;
        plant.Exposure = Input.Exposure;
        plant.GerminationDaysMin = Input.GerminationDaysMin;
        plant.GerminationDaysMax = Input.GerminationDaysMax;
        plant.GerminationTempMinC = Input.GerminationTempMinC;
        plant.GerminationTempMaxC = Input.GerminationTempMaxC;
        plant.MaturityDaysMin = Input.MaturityDaysMin;
        plant.IndoorWeeksMin = Input.IndoorWeeksMin;
        plant.IndoorWeeksMax = Input.IndoorWeeksMax;
        plant.FrostSensitive = Input.FrostSensitive;
        plant.MaturityDaysMax = Input.MaturityDaysMax;
        plant.RowSpacingCm = Input.RowSpacingCm;
        plant.PlantSpacingCm = Input.PlantSpacingCm;
        plant.PotSizeMinLiters = Input.PotSizeMinLiters;
        plant.PotSizeMaxLiters = Input.PotSizeMaxLiters;
        plant.HeightMinCm = Input.HeightMinCm;
        plant.HeightMaxCm = Input.HeightMaxCm;
        plant.VineLengthMaxCm = Input.VineLengthMaxCm;

        // rewrite the season windows
        db.SeasonWindows.RemoveRange(plant.SeasonWindows);
        plant.SeasonWindows.Clear();
        AddWindows(plant, WindowKind.Sowing, Input.Sowing);
        AddWindows(plant, WindowKind.Harvest, Input.Harvest);
        AddWindows(plant, WindowKind.Bloom, Input.Bloom);
    }

    private static void AddWindows(Plant plant, WindowKind kind, List<WindowInput> windows)
    {
        foreach (var w in windows)
        {
            if (w.FromMonth is null || w.ToMonth is null) continue;
            plant.SeasonWindows.Add(new SeasonWindow
            {
                Kind = kind,
                StartMonth = w.FromMonth.Value,
                StartPart = w.FromPart,
                EndMonth = w.ToMonth.Value,
                EndPart = w.ToPart
            });
        }
    }

    // Two entries per kind: the existing windows in order, then empty ones
    private static List<WindowInput> WindowsOf(Plant plant, WindowKind kind)
    {
        var list = plant.SeasonWindows
            .Where(w => w.Kind == kind)
            .OrderBy(w => w.Id)
            .Take(2)
            .Select(w => new WindowInput
            {
                FromMonth = w.StartMonth,
                FromPart = w.StartPart,
                ToMonth = w.EndMonth,
                ToPart = w.EndPart
            })
            .ToList();
        while (list.Count < 2) list.Add(new WindowInput());
        return list;
    }

    private static InputModel ToInput(Plant plant)
    {
        return new InputModel
        {
            PlantTypeId = plant.PlantTypeId,
            Name = plant.Name,
            Propagation = plant.Propagation,
            SeedType = plant.SeedType,
            StockStatus = plant.StockStatus,
            SowingMethod = plant.SowingMethod,
            GrowingPlace = plant.GrowingPlace,
            Exposure = plant.Exposure,
            GerminationDaysMin = plant.GerminationDaysMin,
            GerminationDaysMax = plant.GerminationDaysMax,
            GerminationTempMinC = plant.GerminationTempMinC,
            GerminationTempMaxC = plant.GerminationTempMaxC,
            MaturityDaysMin = plant.MaturityDaysMin,
            IndoorWeeksMin = plant.IndoorWeeksMin,
            IndoorWeeksMax = plant.IndoorWeeksMax,
            FrostSensitive = plant.FrostSensitive,
            MaturityDaysMax = plant.MaturityDaysMax,
            RowSpacingCm = plant.RowSpacingCm,
            PlantSpacingCm = plant.PlantSpacingCm,
            PotSizeMinLiters = plant.PotSizeMinLiters,
            PotSizeMaxLiters = plant.PotSizeMaxLiters,
            HeightMinCm = plant.HeightMinCm,
            HeightMaxCm = plant.HeightMaxCm,
            VineLengthMaxCm = plant.VineLengthMaxCm,
            Sowing = WindowsOf(plant, WindowKind.Sowing),
            Harvest = WindowsOf(plant, WindowKind.Harvest),
            Bloom = WindowsOf(plant, WindowKind.Bloom)
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
        SowingOptions = EnumOptions<SowingMethod>(optional: true);
        PlaceOptions = EnumOptions<GrowingPlace>(optional: true);
        ExposureOptions = EnumOptions<Exposure>(optional: true);

        MonthOptions = Enumerable.Range(1, 12)
            .Select(m => new SelectListItem(CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m), m.ToString()))
            .ToList();
        MonthOptions.Insert(0, new SelectListItem("–", ""));

        PartOptions = Enum.GetValues<MonthPart>()
            .Select(v => new SelectListItem(L["Part_" + v].Value, v.ToString())).ToList();
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
