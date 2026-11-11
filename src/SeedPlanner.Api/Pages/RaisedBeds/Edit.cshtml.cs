using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.RaisedBeds;

// One bed (raised or in the ground): its measures and what grows in it
public class EditModel(SeedPlannerDbContext db, IStringLocalizer<SharedResource> L) : PageModel
{
    public class InputModel
    {
        [Required, StringLength(100)]
        public string Name { get; set; } = "";

        public BedKind Kind { get; set; } = BedKind.Raised;
        public BedShape Shape { get; set; } = BedShape.Rectangular;

        public decimal? WidthCm { get; set; }
        public decimal? LengthCm { get; set; }

        // Only for raised beds; a ground bed has no walls
        public decimal? HeightCm { get; set; }
        public bool HasCover { get; set; }

        public string? Note { get; set; }
    }

    public record PlantingRow(int Id, string Name, int? PlantCount, string? Note, string Choice);

    public int? Id { get; private set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<SelectListItem> KindOptions { get; private set; } = new();
    public List<SelectListItem> ShapeOptions { get; private set; } = new();
    public List<SelectListItem> PlantOptions { get; private set; } = new();

    public List<PlantingRow> Plantings { get; private set; } = new();

    // Message for the planting section (shown right there, not at the top of the page)
    public string? PlantingError { get; private set; }

    // The planting that is being edited right now (its row shows a form instead of the text)
    public int? EditingId { get; private set; }
    public List<SelectListItem> EditOptions { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id, int? editPlanting)
    {
        Id = id;
        EditingId = editPlanting;
        if (id is not null)
        {
            var bed = await db.RaisedBeds.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
            if (bed is null) return NotFound();
            Input = ToInput(bed);
        }
        await PrepareAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Id = id;
        RaisedBed? bed = null;
        if (id is not null)
        {
            bed = await db.RaisedBeds.FirstOrDefaultAsync(b => b.Id == id);
            if (bed is null) return NotFound();
        }

        await ValidateAsync(id);
        if (!ModelState.IsValid)
        {
            await PrepareAsync(id);
            return Page();
        }

        if (bed is null)
        {
            bed = new RaisedBed();
            db.RaisedBeds.Add(bed);
        }
        bed.Name = Input.Name;
        bed.Kind = Input.Kind;
        bed.Shape = Input.Shape;
        bed.WidthCm = Input.WidthCm!.Value;
        bed.LengthCm = Input.LengthCm!.Value;
        bed.HeightCm = Input.Kind == BedKind.Ground ? null : Input.HeightCm;
        bed.HasCover = Input.HasCover;
        bed.Note = Clean(Input.Note);
        await db.SaveChangesAsync();

        // A new bed continues on the edit page, where the plants in it can be entered
        return id is null
            ? RedirectToPage("Edit", new { id = bed.Id })
            : RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostAddPlantingAsync(int id, string? choice, int? plantCount, string? note)
    {
        var bed = await LoadWithPlantingsAsync(id);
        if (bed is null) return NotFound();

        var (typeId, plantId) = await PlantChoices.ParseAsync(db, choice);
        string? error = null;
        if (typeId is null) error = L["PotError_UseInvalid"];
        else if (plantCount is < 1) error = L["PotError_PlantCount"];
        else if (bed.Plantings.Any(x => x.PlantTypeId == typeId && x.PlantId == plantId)) error = L["PotError_AlreadyThere"];
        if (error is not null) return await PageWithErrorAsync(bed, error);

        bed.Plantings.Add(new Planting { PlantTypeId = typeId!.Value, PlantId = plantId, PlantCount = plantCount, Note = Clean(note) });
        await db.SaveChangesAsync();
        return RedirectToPage("Edit", new { id });
    }

    public async Task<IActionResult> OnPostUpdatePlantingAsync(int id, int plantingId, string? choice, int? plantCount, string? note)
    {
        var bed = await LoadWithPlantingsAsync(id);
        var planting = bed?.Plantings.FirstOrDefault(x => x.Id == plantingId);
        if (bed is null || planting is null) return NotFound();

        var (typeId, plantId) = await PlantChoices.ParseAsync(db, choice);
        string? error = null;
        if (typeId is null) error = L["PotError_UseInvalid"];
        else if (plantCount is < 1) error = L["PotError_PlantCount"];
        else if (bed.Plantings.Any(x => x.Id != plantingId && x.PlantTypeId == typeId && x.PlantId == plantId))
            error = L["PotError_AlreadyThere"];
        if (error is not null)
        {
            EditingId = plantingId;
            return await PageWithErrorAsync(bed, error);
        }

        planting.PlantTypeId = typeId!.Value;
        planting.PlantId = plantId;
        planting.PlantCount = plantCount;
        planting.Note = Clean(note);
        await db.SaveChangesAsync();
        return RedirectToPage("Edit", new { id });
    }

    public async Task<IActionResult> OnPostRemovePlantingAsync(int id, int plantingId)
    {
        // The query filter only finds the user's own plantings; the planting must be in this bed
        var planting = await db.Plantings.FirstOrDefaultAsync(x => x.Id == plantingId && x.RaisedBedId == id);
        if (planting is not null)
        {
            db.Plantings.Remove(planting);
            await db.SaveChangesAsync();
        }
        return RedirectToPage("Edit", new { id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        // The plantings of the bed are deleted with it (cascade)
        var bed = await db.RaisedBeds.FirstOrDefaultAsync(b => b.Id == id);
        if (bed is not null)
        {
            db.RaisedBeds.Remove(bed);
            await db.SaveChangesAsync();
        }
        return RedirectToPage("Index");
    }

    // ---------------------------------------------------------------- helpers

    private Task<RaisedBed?> LoadWithPlantingsAsync(int id) =>
        db.RaisedBeds.Include(b => b.Plantings).FirstOrDefaultAsync(b => b.Id == id);

    private async Task<IActionResult> PageWithErrorAsync(RaisedBed bed, string error)
    {
        Id = bed.Id;
        Input = ToInput(bed);
        PlantingError = error;
        await PrepareAsync(bed.Id);
        return Page();
    }

    private async Task ValidateAsync(int? id)
    {
        Input.Name = Input.Name?.Trim() ?? "";
        if (Input.Name.Length > 0 &&
            await db.RaisedBeds.AnyAsync(b => b.Name == Input.Name && b.Id != (id ?? 0)))
        {
            ModelState.AddModelError("Input.Name", L["BedError_NameExists"]);
        }

        // Width and length are required, the height is optional
        if (Input.WidthCm is null or <= 0) ModelState.AddModelError("Input.WidthCm", L["PotError_Measure"]);
        if (Input.LengthCm is null or <= 0) ModelState.AddModelError("Input.LengthCm", L["PotError_Measure"]);
        if (Input.Kind == BedKind.Raised && Input.HeightCm is <= 0)
            ModelState.AddModelError("Input.HeightCm", L["PotError_Measure"]);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static InputModel ToInput(RaisedBed bed) => new()
    {
        Name = bed.Name,
        Kind = bed.Kind,
        Shape = bed.Shape,
        WidthCm = bed.WidthCm,
        LengthCm = bed.LengthCm,
        HeightCm = bed.HeightCm,
        HasCover = bed.HasCover,
        Note = bed.Note
    };

    private async Task PrepareAsync(int? id)
    {
        KindOptions = Enum.GetValues<BedKind>()
            .Select(v => new SelectListItem(L["BedKind_" + v].Value, v.ToString())).ToList();
        ShapeOptions = Enum.GetValues<BedShape>()
            .Select(v => new SelectListItem(L["BedShape_" + v].Value, v.ToString())).ToList();

        if (id is null) return;

        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        var (options, typeNames) = await PlantChoices.LoadAsync(db, L);
        PlantOptions = options;

        var plantings = await db.Plantings.AsNoTracking()
            .Where(x => x.RaisedBedId == id)
            .Include(x => x.Plant)
            .ToListAsync();
        Plantings = plantings
            .Select(x => new PlantingRow(x.Id, PlantChoices.NameOf(x, typeNames), x.PlantCount, x.Note, PlantChoices.ValueOf(x)))
            .OrderBy(x => x.Name, comparer)
            .ToList();

        if (EditingId is not null)
        {
            var current = Plantings.FirstOrDefault(x => x.Id == EditingId)?.Choice;
            EditOptions = PlantChoices.WithSelected(PlantOptions, current);
        }
    }
}
