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

    /// <summary>Search, filters, chips and the addresses of the column titles.</summary>
    public ListFilterView Filter { get; private set; } = null!;

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

        var data = await db.PlantTypes.AsNoTracking()
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

        var facets = new List<Facet<Row>>
        {
            new("category", "Filter_Group_Plant", L["Filter_Category"].Value, r => FilterOptions.One(r.Category), FilterOptions.Enum<PlantCategory>(L, "Category_")),
            new("lifecycle", "Filter_Group_Plant", L["Col_Lifecycle"].Value, r => FilterOptions.One(r.Lifecycle), FilterOptions.Enum<Lifecycle>(L, "Lifecycle_")),
            new("nutrient", "Filter_Group_Plant", L["Col_NutrientDemand"].Value, r => FilterOptions.One(r.NutrientDemand), FilterOptions.Enum<NutrientDemand>(L, "NutrientDemand_")),
            new("exposure", "Filter_Group_Plant", L["Col_Exposure"].Value, r => FilterOptions.One(r.Exposure), FilterOptions.Enum<Exposure>(L, "Exposure_")),
        };

        var filter = new ListFilter<Row>(Request, Q, Sort, Dir, defaultSort: "name");
        Filter = filter;
        // The English name is used if there is no translation
        var rows = filter.Apply(data.Select(d => new Row(
                d.Id, d.TranslatedName ?? d.EnglishName,
                d.Category, d.Lifecycle, d.NutrientDemand, d.Exposure, d.PlantCount)).ToList(),
            facets,
            (r, q) => r.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase));

        var byName = rows
            .OrderBy(r => r.Name, StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true))
            .ToList();

        Rows = (Sort?.ToLowerInvariant()) switch
        {
            "category" => ListSorting.Sort(byName, r => r.Category is null ? null : L["Category_" + r.Category].Value, Filter.Descending),
            "lifecycle" => ListSorting.Sort(byName, r => r.Lifecycle is null ? null : L["Lifecycle_" + r.Lifecycle].Value, Filter.Descending),
            "nutrient" => ListSorting.Sort(byName, r => ListSorting.Rank(r.NutrientDemand), Filter.Descending),
            "exposure" => ListSorting.Sort(byName, r => ListSorting.Rank(r.Exposure), Filter.Descending),
            "plants" => ListSorting.Sort(byName, r => r.PlantCount, Filter.Descending),
            "name" when Filter.Descending => byName.AsEnumerable().Reverse().ToList(),
            _ => byName
        };
    }
}
