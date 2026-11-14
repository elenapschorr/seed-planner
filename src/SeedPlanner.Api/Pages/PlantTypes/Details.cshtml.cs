using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.PlantTypes;

public class DetailsModel(
    SeedPlannerDbContext db,
    IAuthorizationService auth,
    IStringLocalizer<SharedResource> L) : PageModel
{
    public record Neighbour(int Id, string Name);

    public int Id { get; private set; }
    public string Name { get; private set; } = "";
    public PlantCategory? Category { get; private set; }
    public NutrientDemand? NutrientDemand { get; private set; }
    public Exposure? Exposure { get; private set; }
    public PlantRole? Role { get; private set; }

    /// <summary>The role that counts for the signed-in user: the own choice, otherwise the suggestion (<see cref="Role"/>).</summary>
    public PlantRole? OwnRole { get; private set; }
    public bool SignedIn => User.Identity?.IsAuthenticated == true;
    public List<SelectListItem> RoleOptions { get; private set; } = new();
    public SoilType? SoilType { get; private set; }
    public string? PlaceOrder { get; private set; }
    public decimal? PhMin { get; private set; }
    public decimal? PhMax { get; private set; }
    public Lifecycle? Lifecycle { get; private set; }
    public GerminationType? GerminationType { get; private set; }
    public int? GrowthDaysMin { get; private set; }
    public int? GrowthDaysMax { get; private set; }
    public decimal? DemandPerPersonMin { get; private set; }
    public decimal? DemandPerPersonMax { get; private set; }
    public DemandUnit? DemandUnit { get; private set; }

    // Names in all languages
    public string? NameDe { get; private set; }
    public string? NameEn { get; private set; }
    public string? NameRu { get; private set; }

    // Space needs, taken from the user's own varieties of this plant type (smallest and largest value)
    public int VarietyCount { get; private set; }
    public string? PotSize { get; private set; }
    public string? RowSpacing { get; private set; }
    public string? PlantSpacing { get; private set; }

    // "60 – 90", "60" or null if there is no value
    public static string? Range<T>(T? min, T? max) where T : struct
    {
        if (min is null && max is null) return null;
        var from = min ?? max;
        var to = max ?? min;
        var a = Format(from!.Value);
        var b = Format(to!.Value);
        return a == b ? a : $"{a} – {b}";
    }

    private static string Format<T>(T value) where T : struct =>
        Convert.ToString(value, CultureInfo.CurrentCulture) ?? "";
    public List<Neighbour> Good { get; private set; } = new();
    public List<Neighbour> Bad { get; private set; } = new();

    // Only administrators may change plant types and their neighbours
    public bool CanEdit { get; private set; }
    public List<SelectListItem> AddOptions { get; private set; } = new();
    public List<SelectListItem> RelationOptions { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        // Language of the current request: "de", "en" or "ru"
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        CanEdit = await IsAdminAsync();

        var type = await db.PlantTypes.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new
            {
                EnglishName = t.Name,
                TranslatedName = t.Translations
                    .Where(x => x.Language == language)
                    .Select(x => x.Name)
                    .FirstOrDefault(),
                t.Category,
                t.NutrientDemand,
                t.Exposure,
                t.Role,
                t.SoilType,
                t.PlaceOrder,
                t.PhMin,
                t.PhMax,
                t.Lifecycle,
                t.GerminationType,
                t.GrowthDaysMin,
                t.GrowthDaysMax,
                t.DemandPerPersonMin,
                t.DemandPerPersonMax,
                t.DemandUnit
            })
            .FirstOrDefaultAsync();

        if (type is null)
        {
            return NotFound();
        }

        Id = id;
        Name = type.TranslatedName ?? type.EnglishName;
        Category = type.Category;
        NutrientDemand = type.NutrientDemand;
        Exposure = type.Exposure;
        Role = type.Role;
        var ownRole = await db.UserPlantRoles.AsNoTracking().Where(r => r.PlantTypeId == id).Select(r => (PlantRole?)r.Role).FirstOrDefaultAsync();
        OwnRole = ownRole ?? type.Role;
        RoleOptions = Enum.GetValues<PlantRole>().Select(r => new SelectListItem(L["PlantRole_" + r].Value, r.ToString())).ToList();
        RoleOptions.Insert(0, new SelectListItem("–", ""));
        SoilType = type.SoilType;
        PlaceOrder = type.PlaceOrder;
        PhMin = type.PhMin;
        PhMax = type.PhMax;
        Lifecycle = type.Lifecycle;
        GerminationType = type.GerminationType;
        GrowthDaysMin = type.GrowthDaysMin;
        GrowthDaysMax = type.GrowthDaysMax;
        DemandPerPersonMin = type.DemandPerPersonMin;
        DemandPerPersonMax = type.DemandPerPersonMax;
        DemandUnit = type.DemandUnit;

        var names = await db.PlantTypeTranslations.AsNoTracking()
            .Where(x => x.PlantTypeId == id)
            .ToDictionaryAsync(x => x.Language, x => x.Name);
        NameDe = names.GetValueOrDefault("de");
        NameEn = names.GetValueOrDefault("en") ?? type.EnglishName;
        NameRu = names.GetValueOrDefault("ru");

        // The user's own varieties of this plant type (the query filter limits the rows to the signed-in user)
        var varieties = await db.Plants.AsNoTracking()
            .Where(p => p.PlantTypeId == id)
            .Select(p => new { p.PotSizeMinLiters, p.PotSizeMaxLiters, p.RowSpacingCm, p.PlantSpacingCm })
            .ToListAsync();
        VarietyCount = varieties.Count;
        PotSize = Range(
            varieties.Min(v => v.PotSizeMinLiters ?? v.PotSizeMaxLiters),
            varieties.Max(v => v.PotSizeMaxLiters ?? v.PotSizeMinLiters));
        RowSpacing = Range(varieties.Min(v => v.RowSpacingCm), varieties.Max(v => v.RowSpacingCm));
        PlantSpacing = Range(varieties.Min(v => v.PlantSpacingCm), varieties.Max(v => v.PlantSpacingCm));

        // Each pair is stored once, so the type can be on either side
        var pairs = await db.PlantTypeCompanions.AsNoTracking()
            .Where(c => c.PlantTypeId == id || c.OtherPlantTypeId == id)
            .Select(c => new
            {
                c.Relation,
                OtherId = c.PlantTypeId == id ? c.OtherPlantTypeId : c.PlantTypeId
            })
            .ToListAsync();

        // All other types with their names in the current language (for the neighbours and the "add" list)
        var all = await db.PlantTypes.AsNoTracking()
            .Where(t => t.Id != id)
            .Select(t => new
            {
                t.Id,
                EnglishName = t.Name,
                TranslatedName = t.Translations
                    .Where(x => x.Language == language)
                    .Select(x => x.Name)
                    .FirstOrDefault()
            })
            .ToListAsync();

        // Fall back to the English name if there is no translation
        var nameById = all.ToDictionary(o => o.Id, o => o.TranslatedName ?? o.EnglishName);
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);

        List<Neighbour> Pick(CompanionRelation relation) => pairs
            .Where(p => p.Relation == relation)
            .Select(p => new Neighbour(p.OtherId, nameById[p.OtherId]))
            .OrderBy(n => n.Name, comparer)
            .ToList();

        Good = Pick(CompanionRelation.Good);
        Bad = Pick(CompanionRelation.Bad);

        if (CanEdit)
        {
            var taken = pairs.Select(p => p.OtherId).ToHashSet();
            AddOptions = nameById
                .Where(kv => !taken.Contains(kv.Key))
                .OrderBy(kv => kv.Value, comparer)
                .Select(kv => new SelectListItem(kv.Value, kv.Key.ToString()))
                .ToList();
            AddOptions.Insert(0, new SelectListItem("–", ""));

            RelationOptions = Enum.GetValues<CompanionRelation>()
                .Select(r => new SelectListItem(L["CompanionRelation_" + r].Value, r.ToString()))
                .ToList();
        }

        return Page();
    }

    // The user's own role for this plant type; only a role that differs from the suggestion is stored
    public async Task<IActionResult> OnPostOwnRoleAsync(int id, PlantRole? role)
    {
        if (!SignedIn) return Forbid();
        var type = await db.PlantTypes.AsNoTracking().Where(t => t.Id == id).Select(t => new { t.Role }).FirstOrDefaultAsync();
        if (type is null) return NotFound();

        var existing = await db.UserPlantRoles.FirstOrDefaultAsync(r => r.PlantTypeId == id);
        if (role is null || role == type.Role)
        {
            if (existing is not null) db.UserPlantRoles.Remove(existing);
        }
        else if (existing is null)
            db.UserPlantRoles.Add(new UserPlantRole { PlantTypeId = id, Role = role.Value });
        else
            existing.Role = role.Value;

        await db.SaveChangesAsync();
        return RedirectToPage(new { id });
    }

    // Adds a neighbour, or changes Good/Bad if the pair already exists
    public async Task<IActionResult> OnPostSetNeighbourAsync(int id, int otherId, CompanionRelation relation)
    {
        if (!await IsAdminAsync()) return Forbid();
        if (otherId <= 0 || otherId == id) return RedirectToPage(new { id });

        var bothExist = await db.PlantTypes.CountAsync(t => t.Id == id || t.Id == otherId) == 2;
        if (!bothExist) return NotFound();

        var low = Math.Min(id, otherId);
        var high = Math.Max(id, otherId);
        var pair = await db.PlantTypeCompanions
            .FirstOrDefaultAsync(c => c.PlantTypeId == low && c.OtherPlantTypeId == high);

        if (pair is null)
            db.PlantTypeCompanions.Add(PlantTypeCompanion.Between(id, otherId, relation));
        else
            pair.Relation = relation;

        await db.SaveChangesAsync();
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveNeighbourAsync(int id, int otherId)
    {
        if (!await IsAdminAsync()) return Forbid();

        var low = Math.Min(id, otherId);
        var high = Math.Max(id, otherId);
        var pair = await db.PlantTypeCompanions
            .FirstOrDefaultAsync(c => c.PlantTypeId == low && c.OtherPlantTypeId == high);

        if (pair is not null)
        {
            db.PlantTypeCompanions.Remove(pair);
            await db.SaveChangesAsync();
        }
        return RedirectToPage(new { id });
    }

    private async Task<bool> IsAdminAsync() =>
        (await auth.AuthorizeAsync(User, "PlantTypeAdmin")).Succeeded;
}
