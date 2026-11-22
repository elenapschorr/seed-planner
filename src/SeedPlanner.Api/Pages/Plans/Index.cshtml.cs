using System.Globalization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Plans;

public class IndexModel(SeedPlannerDbContext db) : PageModel
{
    public record Row(int Id, string Name, DateOnly ValidFrom, DateOnly ValidTo, int Persons, DemandLevel Level, int CustomPercent,
        int Crops, int Plants, int ToBuy);

    public List<Row> Rows { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        var plans = await db.Plans.AsNoTracking()
            .Include(p => p.Items).ThenInclude(i => i.Lines)
            .ToListAsync();

        Rows = plans
            .OrderByDescending(p => p.ValidFrom)
            .ThenBy(p => p.Name, comparer)
            .Select(p => new Row(
                p.Id, p.Name, p.ValidFrom, p.ValidTo, p.Persons, p.Level, p.CustomPercent,
                p.Items.Count,
                p.Items.Sum(i => i.Lines.Sum(l => l.Plants)),
                p.ShoppingList.Count()))
            .ToList();
    }
}
