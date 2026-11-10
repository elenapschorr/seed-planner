using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages;

public class IndexModel(SeedPlannerDbContext db) : PageModel
{
    public int Varieties { get; private set; }
    public int PotGroups { get; private set; }
    public int PotsTotal { get; private set; }
    public int PotsPlanted { get; private set; }
    public int PotsFree => PotsTotal - PotsPlanted;

    // The query filters of the context limit all of this to the signed-in user
    public async Task OnGetAsync()
    {
        Varieties = await db.Plants.CountAsync();
        PotGroups = await db.PotGroups.CountAsync();
        PotsTotal = await db.Pots.CountAsync();
        PotsPlanted = await db.Pots.CountAsync(p => p.Plantings.Any());
    }
}
