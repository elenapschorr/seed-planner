using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Saucers;

// One saucer size: diameter, colour, how many of them there are
public class EditModel(SeedPlannerDbContext db, IStringLocalizer<SharedResource> L) : PageModel
{
    public class InputModel
    {
        public decimal? DiameterCm { get; set; }
        public string? Color { get; set; }
        public int Quantity { get; set; } = 1;
        public string? Note { get; set; }
    }

    public int? Id { get; private set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    // Pot groups that use this saucer (shown as information; they keep their pots when it is deleted)
    public int PotGroupCount { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Id = id;
        if (id is not null)
        {
            var saucer = await db.Saucers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
            if (saucer is null) return NotFound();
            Input = new InputModel
            {
                DiameterCm = saucer.DiameterCm,
                Color = saucer.Color,
                Quantity = saucer.Quantity,
                Note = saucer.Note
            };
            PotGroupCount = await db.PotGroups.CountAsync(g => g.SaucerId == id);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Id = id;
        Saucer? saucer = null;
        if (id is not null)
        {
            saucer = await db.Saucers.FirstOrDefaultAsync(s => s.Id == id);
            if (saucer is null) return NotFound();
        }

        if (Input.DiameterCm is null or <= 0) ModelState.AddModelError("Input.DiameterCm", L["PotError_Measure"]);
        if (Input.Quantity < 0) ModelState.AddModelError("Input.Quantity", L["PotError_Quantity"]);
        if (!ModelState.IsValid)
        {
            if (id is not null) PotGroupCount = await db.PotGroups.CountAsync(g => g.SaucerId == id);
            return Page();
        }

        if (saucer is null)
        {
            saucer = new Saucer();
            db.Saucers.Add(saucer);
        }
        saucer.DiameterCm = Input.DiameterCm!.Value;
        saucer.Color = Clean(Input.Color);
        saucer.Quantity = Input.Quantity;
        saucer.Note = Clean(Input.Note);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        // The pot groups stay; their saucer is set to empty by the database (SetNull)
        var saucer = await db.Saucers.FirstOrDefaultAsync(s => s.Id == id);
        if (saucer is not null)
        {
            db.Saucers.Remove(saucer);
            await db.SaveChangesAsync();
        }
        return RedirectToPage("Index");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
