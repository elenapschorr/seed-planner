using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Roles;

// The partners of every main plant: the secondary plants that stand with it. The suggestion comes from the table of
// good neighbours (main plant with secondary plant); the user can take a partner away or add another secondary plant.
// Only the differences from the suggestion are stored (UserPlantPartner). The role itself is changed at the plant type
// or the variety.
public class IndexModel(SeedPlannerDbContext db) : PageModel
{
    /// <summary>A partner of a main plant; Added = not in the suggestion, the user added it.</summary>
    public record PartnerItem(int Id, string Name, bool Added);

    public record Row(int MainId, string Name, List<PartnerItem> Partners, List<SelectListItem> AddOptions, bool Changed);

    public List<Row> Rows { get; private set; } = new();

    /// <summary>The main plant whose list is shown open.</summary>
    [BindProperty(SupportsGet = true)]
    public int? Open { get; set; }

    public async Task OnGetAsync()
    {
        var ctx = await LoadAsync();
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        var partners = PlantPairs.MainPartners(ctx.Effective, ctx.Companions, Changes(ctx.Changes));

        Rows = partners
            .Select(m =>
            {
                var items = m.Value
                    .Select(id => new PartnerItem(id, ctx.Names[id], Added: !ctx.Suggested[m.Key].Contains(id)))
                    .OrderBy(i => i.Name, comparer)
                    .ToList();
                var taken = m.Value.ToHashSet();
                var options = ctx.Effective
                    .Where(t => t.Value == PlantRole.Secondary && !taken.Contains(t.Key))
                    .OrderBy(t => ctx.Names[t.Key], comparer)
                    .Select(t => new SelectListItem(ctx.Names[t.Key], t.Key.ToString()))
                    .ToList();
                return new Row(m.Key, ctx.Names[m.Key], items, options, ctx.Changes.Any(c => c.MainPlantTypeId == m.Key));
            })
            .OrderBy(r => r.Name, comparer)
            .ToList();
    }

    // Adds a secondary plant to the partners of a main plant
    public async Task<IActionResult> OnPostAddAsync(int mainId, int secondaryId)
    {
        var ctx = await LoadAsync();
        if (IsPair(ctx, mainId, secondaryId))
        {
            var existing = ctx.Changes.FirstOrDefault(c => c.MainPlantTypeId == mainId && c.SecondaryPlantTypeId == secondaryId);
            if (ctx.Suggested[mainId].Contains(secondaryId))
            {
                // Back to the suggestion: nothing has to be stored
                if (existing is not null) db.UserPlantPartners.Remove(existing);
            }
            else if (existing is null)
                db.UserPlantPartners.Add(new UserPlantPartner { MainPlantTypeId = mainId, SecondaryPlantTypeId = secondaryId, Included = true });
            else
                existing.Included = true;
            await db.SaveChangesAsync();
        }
        return Back(mainId);
    }

    // Takes a secondary plant away from the partners of a main plant
    public async Task<IActionResult> OnPostRemoveAsync(int mainId, int secondaryId)
    {
        var ctx = await LoadAsync();
        var existing = ctx.Changes.FirstOrDefault(c => c.MainPlantTypeId == mainId && c.SecondaryPlantTypeId == secondaryId);
        if (ctx.Effective.GetValueOrDefault(mainId) == PlantRole.Main && ctx.Suggested.TryGetValue(mainId, out var suggested) && suggested.Contains(secondaryId))
        {
            if (existing is null)
                db.UserPlantPartners.Add(new UserPlantPartner { MainPlantTypeId = mainId, SecondaryPlantTypeId = secondaryId, Included = false });
            else
                existing.Included = false;
        }
        else if (existing is not null)
            db.UserPlantPartners.Remove(existing);
        await db.SaveChangesAsync();
        return Back(mainId);
    }

    // Back to the suggestion for one main plant
    public async Task<IActionResult> OnPostResetAsync(int mainId)
    {
        var own = await db.UserPlantPartners.Where(c => c.MainPlantTypeId == mainId).ToListAsync();
        db.UserPlantPartners.RemoveRange(own);
        await db.SaveChangesAsync();
        return Back(mainId);
    }

    private IActionResult Back(int mainId) => RedirectToPage("Index", null, new { open = mainId }, $"t{mainId}");

    private static bool IsPair(Context ctx, int mainId, int secondaryId) =>
        ctx.Effective.GetValueOrDefault(mainId) == PlantRole.Main && ctx.Effective.GetValueOrDefault(secondaryId) == PlantRole.Secondary;

    private static IEnumerable<PlantPairs.PartnerChange> Changes(IEnumerable<UserPlantPartner> list) =>
        list.Select(c => new PlantPairs.PartnerChange(c.MainPlantTypeId, c.SecondaryPlantTypeId, c.Included));

    private sealed record Context(
        Dictionary<int, string> Names,
        Dictionary<int, PlantRole?> Effective,
        IReadOnlyDictionary<int, IReadOnlyList<int>> Suggested,
        List<PlantTypeCompanion> Companions,
        List<UserPlantPartner> Changes);

    // Names in the current language, the role that counts for the user (own choice before the suggestion of the plant type),
    // the suggested partners and the user's changes (tracked, so the handlers can change them)
    private async Task<Context> LoadAsync()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var types = await db.PlantTypes.AsNoTracking().Include(t => t.Translations).ToListAsync();
        var names = types.ToDictionary(t => t.Id, t => t.Translations.FirstOrDefault(x => x.Language == language)?.Name ?? t.Name);
        var effective = types.ToDictionary(t => t.Id, t => t.Role);
        foreach (var own in await db.UserPlantRoles.AsNoTracking().ToListAsync())
            effective[own.PlantTypeId] = own.Role;

        var companions = await db.PlantTypeCompanions.AsNoTracking().Where(c => c.Relation == CompanionRelation.Good).ToListAsync();
        var suggested = PlantPairs.Partners(effective, companions);
        var changes = await db.UserPlantPartners.ToListAsync();
        return new Context(names, effective, suggested, companions, changes);
    }
}
