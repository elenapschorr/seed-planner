using Microsoft.Extensions.Localization;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Garden;

// The page to set up the plot: upload the plan, mark the boundary and areas, take the own part as the plot.
// It is shown first while no plot is defined; afterwards /Garden shows the plot with the beds. Same data and handlers as the main page.
public class SetupModel(SeedPlannerDbContext db, IStringLocalizer<SharedResource> L) : IndexModel(db, L)
{
    protected override bool RequirePlot => false;
}
