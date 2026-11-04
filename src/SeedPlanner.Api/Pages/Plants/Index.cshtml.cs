using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Plants;

public class IndexModel(SeedPlannerDbContext db) : PageModel
{
    public record Row(
        int Id,
        string Name,
        string TypeName,
        SeedType SeedType,
        StockStatus StockStatus,
        CultivationMethod? CultivationMethod,
        Exposure? Exposure,
        bool ShouldBuy);

    public record TypeOption(int Id, string Name);

    public List<Row> Rows { get; private set; } = new();
    public List<TypeOption> TypeOptions { get; private set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? TypeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public StockStatus? Stock { get; set; }

    // 1..12: only plants that can be sown in this month
    [BindProperty(SupportsGet = true)]
    public int? Month { get; set; }

    public async Task OnGetAsync()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);

        // Plant types for the filter, named in the current language
        var types = await db.PlantTypes.AsNoTracking()
            .Include(t => t.Translations)
            .ToListAsync();
        TypeOptions = types
            .Select(t => new TypeOption(t.Id, TranslatedName(t, language)))
            .OrderBy(t => t.Name, comparer)
            .ToList();

        var query = db.Plants.AsNoTracking()
            .Include(p => p.PlantType).ThenInclude(t => t.Translations)
            .Include(p => p.SeasonWindows)
            .AsQueryable();
        if (TypeId is not null) query = query.Where(p => p.PlantTypeId == TypeId);
        if (Stock is not null) query = query.Where(p => p.StockStatus == Stock);

        var plants = await query.ToListAsync();

        if (Month is >= 1 and <= 12)
        {
            plants = plants.Where(p => p.CanSowIn(Month.Value)).ToList();
        }

        var rows = plants.Select(p => new Row(
            p.Id,
            p.Name,
            TranslatedName(p.PlantType, language),
            p.SeedType,
            p.StockStatus,
            p.CultivationMethod,
            p.EffectiveExposure,   // own value of the plant, otherwise the default of its plant type
            p.ShouldBuy));         // rule lives in the domain class

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            rows = rows.Where(r =>
                r.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                r.TypeName.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        }

        Rows = rows
            .OrderBy(r => r.TypeName, comparer)
            .ThenBy(r => r.Name, comparer)
            .ToList();
    }

    // Name in the current language, English name if there is no translation
    private static string TranslatedName(PlantType type, string language) =>
        type.Translations.FirstOrDefault(t => t.Language == language)?.Name ?? type.Name;
}
