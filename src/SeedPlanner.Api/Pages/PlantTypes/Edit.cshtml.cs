using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.PlantTypes;

// Plant types are shared by all users, so only administrators may change them
[Authorize(Policy = "PlantTypeAdmin")]
public class EditModel(SeedPlannerDbContext db, IStringLocalizer<SharedResource> L) : PageModel
{
    public class InputModel
    {
        // The English name is the canonical name in the database; German and Russian are translations
        [Required, StringLength(100)]
        public string NameEn { get; set; } = "";

        [Required, StringLength(100)]
        public string NameDe { get; set; } = "";

        [Required, StringLength(100)]
        public string NameRu { get; set; } = "";

        public PlantCategory? Category { get; set; }
        public Lifecycle? Lifecycle { get; set; }
        public NutrientDemand? NutrientDemand { get; set; }
        public GerminationType? GerminationType { get; set; }
        public Exposure? Exposure { get; set; }
        public SoilType? SoilType { get; set; }

        [Range(0, 14)]
        public decimal? PhMin { get; set; }
        [Range(0, 14)]
        public decimal? PhMax { get; set; }

        public int? GrowthDaysMin { get; set; }
        public int? GrowthDaysMax { get; set; }

        public decimal? DemandPerPersonMin { get; set; }
        public decimal? DemandPerPersonMax { get; set; }
        public DemandUnit? DemandUnit { get; set; }
    }

    public int? Id { get; private set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    // Page to return to after saving or cancelling (e.g. the filtered list); only local URLs are accepted
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    // Target of the cancel button and of the redirect after saving
    public string BackUrl(int? id) =>
        !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)
            ? ReturnUrl
            : id is null
                ? Url.Page("Index")!
                : Url.Page("Details", new { id })!;

    public List<SelectListItem> CategoryOptions { get; private set; } = new();
    public List<SelectListItem> LifecycleOptions { get; private set; } = new();
    public List<SelectListItem> NutrientOptions { get; private set; } = new();
    public List<SelectListItem> GerminationOptions { get; private set; } = new();
    public List<SelectListItem> ExposureOptions { get; private set; } = new();
    public List<SelectListItem> SoilOptions { get; private set; } = new();
    public List<SelectListItem> UnitOptions { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Id = id;
        LoadOptions();
        if (id is null) return Page();

        var type = await db.PlantTypes.AsNoTracking()
            .Include(t => t.Translations)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (type is null) return NotFound();

        Input = ToInput(type);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Id = id;
        await ValidateAsync(id);
        if (!ModelState.IsValid)
        {
            LoadOptions();
            return Page();
        }

        PlantType? type;
        if (id is null)
        {
            type = new PlantType { Name = Input.NameEn.Trim() };
            db.PlantTypes.Add(type);
        }
        else
        {
            type = await db.PlantTypes.Include(t => t.Translations).FirstOrDefaultAsync(t => t.Id == id);
            if (type is null) return NotFound();
        }

        Apply(type);
        await db.SaveChangesAsync();
        return LocalRedirect(BackUrl(type.Id));
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        Id = id;
        var type = await db.PlantTypes.Include(t => t.Translations).FirstOrDefaultAsync(t => t.Id == id);
        if (type is null) return RedirectToPage("Index");

        // Plants and pots of all users count here (the query filter would only show the signed-in user's rows).
        // Deleting a type that is in use would delete those plants with it.
        if (await db.Plants.IgnoreQueryFilters().AnyAsync(p => p.PlantTypeId == id) ||
            await db.Plantings.IgnoreQueryFilters().AnyAsync(x => x.PlantTypeId == id))
        {
            ModelState.AddModelError(string.Empty, L["PlantTypeError_InUse"]);
            LoadOptions();
            Input = ToInput(type);
            return Page();
        }

        // Translations and neighbour entries are deleted with the type (cascade)
        db.PlantTypes.Remove(type);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    // ---------------------------------------------------------------- validation

    private async Task ValidateAsync(int? id)
    {
        var nameEn = Input.NameEn?.Trim() ?? "";
        if (nameEn.Length > 0 &&
            await db.PlantTypes.AnyAsync(t => t.Name == nameEn && t.Id != (id ?? 0)))
        {
            ModelState.AddModelError("Input.NameEn", L["PlantTypeError_NameExists"]);
        }

        CheckMinMax(nameof(Input.PhMin), Input.PhMin, Input.PhMax);
        CheckMinMax(nameof(Input.GrowthDaysMin), Input.GrowthDaysMin, Input.GrowthDaysMax);
        CheckMinMax(nameof(Input.DemandPerPersonMin), Input.DemandPerPersonMin, Input.DemandPerPersonMax);

        var hasDemand = Input.Category != PlantCategory.Flower &&
            (Input.DemandPerPersonMin is not null || Input.DemandPerPersonMax is not null);
        if (hasDemand && Input.DemandUnit is null)
        {
            ModelState.AddModelError("Input.DemandUnit", L["PlantTypeError_UnitMissing"]);
        }
    }

    private void CheckMinMax<T>(string field, T? min, T? max) where T : struct, IComparable<T>
    {
        if (min is not null && max is not null && min.Value.CompareTo(max.Value) > 0)
            ModelState.AddModelError($"Input.{field}", L["Error_MinGreaterMax"]);
    }

    // ---------------------------------------------------------------- mapping

    private void Apply(PlantType type)
    {
        type.Name = Input.NameEn.Trim();
        type.Category = Input.Category;
        type.Lifecycle = Input.Lifecycle;
        type.NutrientDemand = Input.NutrientDemand;
        type.GerminationType = Input.GerminationType;
        type.Exposure = Input.Exposure;
        type.SoilType = Input.SoilType;
        type.PhMin = Input.PhMin;
        type.PhMax = Input.PhMax;
        type.GrowthDaysMin = Input.GrowthDaysMin;
        type.GrowthDaysMax = Input.GrowthDaysMax;
        // Flowers are not harvested, so they have no demand per person
        var isFlower = Input.Category == PlantCategory.Flower;
        type.DemandPerPersonMin = isFlower ? null : Input.DemandPerPersonMin;
        type.DemandPerPersonMax = isFlower ? null : Input.DemandPerPersonMax;

        // A unit only makes sense together with an amount
        var hasDemand = type.DemandPerPersonMin is not null || type.DemandPerPersonMax is not null;
        type.DemandUnit = hasDemand ? Input.DemandUnit : null;

        SetTranslation(type, "de", Input.NameDe);
        SetTranslation(type, "en", Input.NameEn);
        SetTranslation(type, "ru", Input.NameRu);
    }

    private static void SetTranslation(PlantType type, string language, string name)
    {
        name = name.Trim();
        var existing = type.Translations.FirstOrDefault(t => t.Language == language);
        if (existing is null)
            type.Translations.Add(new PlantTypeTranslation { Language = language, Name = name });
        else
            existing.Name = name;
    }

    private static InputModel ToInput(PlantType type)
    {
        string Translated(string language) =>
            type.Translations.FirstOrDefault(t => t.Language == language)?.Name ?? "";

        return new InputModel
        {
            NameEn = type.Name,
            NameDe = Translated("de"),
            NameRu = Translated("ru"),
            Category = type.Category,
            Lifecycle = type.Lifecycle,
            NutrientDemand = type.NutrientDemand,
            GerminationType = type.GerminationType,
            Exposure = type.Exposure,
            SoilType = type.SoilType,
            PhMin = type.PhMin,
            PhMax = type.PhMax,
            GrowthDaysMin = type.GrowthDaysMin,
            GrowthDaysMax = type.GrowthDaysMax,
            DemandPerPersonMin = type.DemandPerPersonMin,
            DemandPerPersonMax = type.DemandPerPersonMax,
            DemandUnit = type.DemandUnit
        };
    }

    // ---------------------------------------------------------------- dropdown lists

    private void LoadOptions()
    {
        CategoryOptions = EnumOptions<PlantCategory>("Category_");
        LifecycleOptions = EnumOptions<Lifecycle>("Lifecycle_");
        NutrientOptions = EnumOptions<NutrientDemand>("NutrientDemand_");
        GerminationOptions = EnumOptions<GerminationType>("GerminationType_");
        ExposureOptions = EnumOptions<Exposure>("Exposure_");
        SoilOptions = EnumOptions<SoilType>("SoilType_");
        UnitOptions = EnumOptions<DemandUnit>("DemandUnit_");
    }

    // Every list starts with an empty choice "–", because all of these values are optional
    private List<SelectListItem> EnumOptions<T>(string resourcePrefix) where T : struct, Enum
    {
        var items = Enum.GetValues<T>()
            .Select(v => new SelectListItem(L[resourcePrefix + v].Value, v.ToString()))
            .ToList();
        items.Insert(0, new SelectListItem("–", ""));
        return items;
    }
}
