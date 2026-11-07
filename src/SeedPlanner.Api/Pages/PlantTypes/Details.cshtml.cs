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
                t.Exposure
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
