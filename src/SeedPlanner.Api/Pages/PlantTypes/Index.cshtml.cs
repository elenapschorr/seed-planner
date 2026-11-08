using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.PlantTypes;

public class IndexModel(
    SeedPlannerDbContext db,
    IAuthorizationService auth,
    IStringLocalizer<SharedResource> L) : PageModel
{
    public record Row(
        int Id,
        string Name,
        PlantCategory? Category,
        Lifecycle? Lifecycle,
        NutrientDemand? NutrientDemand,
        Exposure? Exposure,
        int PlantCount);

    // Current address with filters and sort order, so that "Edit" can come back to this list
    public string CurrentUrl => Request.Path + Request.QueryString;

    public List<Row> Rows { get; private set; } = new();

    // Only administrators see the buttons for creating and editing plant types
    public bool CanEdit { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public PlantCategory? Category { get; set; }

    [BindProperty(Name = "lifecycle", SupportsGet = true)]
    public Lifecycle? LifecycleFilter { get; set; }

    [BindProperty(Name = "nutrient", SupportsGet = true)]
    public NutrientDemand? NutrientFilter { get; set; }

    [BindProperty(Name = "exposure", SupportsGet = true)]
    public Exposure? ExposureFilter { get; set; }

    // Column to sort by (name, category, lifecycle, nutrient, exposure, plants) and direction (asc, desc)
    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Dir { get; set; }

    public async Task OnGetAsync()
    {
        CanEdit = (await auth.AuthorizeAsync(User, "PlantTypeAdmin")).Succeeded;

        // Language of the current request: "de", "en" or "ru"
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        var query = db.PlantTypes.AsNoTracking();
        if (Category is not null) query = query.Where(t => t.Category == Category);
        if (LifecycleFilter is not null) query = query.Where(t => t.Lifecycle == LifecycleFilter);
        if (NutrientFilter is not null) query = query.Where(t => t.NutrientDemand == NutrientFilter);
        if (ExposureFilter is not null) query = query.Where(t => t.Exposure == ExposureFilter);

        var data = await query
            .Select(t => new
            {
                t.Id,
                EnglishName = t.Name,
                TranslatedName = t.Translations
                    .Where(x => x.Language == language)
                    .Select(x => x.Name)
                    .FirstOrDefault(),
                t.Category,
                t.Lifecycle,
                t.NutrientDemand,
                t.Exposure,
                PlantCount = t.Plants.Count
            })
            .ToListAsync();

        // Fall back to the English name if there is no translation
        var rows = data.Select(d => new Row(
            d.Id, d.TranslatedName ?? d.EnglishName,
            d.Category, d.Lifecycle, d.NutrientDemand, d.Exposure, d.PlantCount));

        if (!string.IsNullOrWhiteSpace(Q))
        {
            rows = rows.Where(r => r.Name.Contains(Q.Trim(), StringComparison.CurrentCultureIgnoreCase));
        }

        var byName = rows
            .OrderBy(r => r.Name, StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true))
            .ToList();

        Rows = (Sort?.ToLowerInvariant()) switch
        {
            "category" => ListSorting.Sort(byName, r => r.Category is null ? null : L["Category_" + r.Category].Value, Descending),
            "lifecycle" => ListSorting.Sort(byName, r => r.Lifecycle is null ? null : L["Lifecycle_" + r.Lifecycle].Value, Descending),
            "nutrient" => ListSorting.Sort(byName, r => ListSorting.Rank(r.NutrientDemand), Descending),
            "exposure" => ListSorting.Sort(byName, r => ListSorting.Rank(r.Exposure), Descending),
            "plants" => ListSorting.Sort(byName, r => r.PlantCount, Descending),
            "name" when Descending => byName.AsEnumerable().Reverse().ToList(),
            _ => byName
        };
    }

    // ---------------------------------------------------------------- sort links

    public bool Descending => string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase);

    private bool SortedBy(string column) =>
        string.Equals(Sort ?? "name", column, StringComparison.OrdinalIgnoreCase);

    // Arrow behind the column title
    public string Arrow(string column) => SortedBy(column) ? (Descending ? " ▼" : " ▲") : "";

    // Address of a column title: keeps the filters; a second click reverses the direction
    public Dictionary<string, string?> SortRoute(string column) => new()
    {
        ["q"] = Q,
        ["category"] = Category?.ToString(),
        ["lifecycle"] = LifecycleFilter?.ToString(),
        ["nutrient"] = NutrientFilter?.ToString(),
        ["exposure"] = ExposureFilter?.ToString(),
        ["sort"] = column,
        ["dir"] = SortedBy(column) && !Descending ? "desc" : "asc"
    };
}
