using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.PlantTypes;

public class IndexModel(SeedPlannerDbContext db) : PageModel
{
    public record Row(
        string Name,
        PlantCategory? Category,
        NutrientDemand? NutrientDemand,
        Exposure? Exposure,
        int PlantCount);

    public List<Row> Rows { get; private set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public PlantCategory? Category { get; set; }

    public async Task OnGetAsync()
    {
        // Language of the current request: "de", "en" or "ru"
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        var query = db.PlantTypes.AsNoTracking();
        if (Category is not null)
        {
            query = query.Where(t => t.Category == Category);
        }

        var data = await query
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
                PlantCount = t.Plants.Count
            })
            .ToListAsync();

        // Fall back to the English name if there is no translation
        var rows = data.Select(d => new Row(
            d.TranslatedName ?? d.EnglishName,
            d.Category, d.NutrientDemand, d.Exposure, d.PlantCount));

        if (!string.IsNullOrWhiteSpace(Q))
        {
            rows = rows.Where(r => r.Name.Contains(Q.Trim(), StringComparison.CurrentCultureIgnoreCase));
        }

        Rows = rows
            .OrderBy(r => r.Name, StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true))
            .ToList();
    }
}
