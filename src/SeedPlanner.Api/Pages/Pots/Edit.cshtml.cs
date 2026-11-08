using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Pots;

public class EditModel(SeedPlannerDbContext db, IStringLocalizer<SharedResource> L) : PageModel
{
    public class InputModel
    {
        public PotKind Kind { get; set; } = PotKind.Pot;
        public PotShape Shape { get; set; } = PotShape.Round;

        public decimal? TopCm { get; set; }
        public decimal? BottomCm { get; set; }
        public decimal? HeightCm { get; set; }

        public int Quantity { get; set; } = 1;
        public int SetSize { get; set; } = 1;

        public string? Color { get; set; }
        public string? Code { get; set; }
        public string? Note { get; set; }

        public int? SaucerId { get; set; }
    }

    public record UsePlantRow(int Id, string Name);

    public record UseRow(int Id, int Quantity, List<UsePlantRow> Plants);

    public int? Id { get; private set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<SelectListItem> KindOptions { get; private set; } = new();
    public List<SelectListItem> ShapeOptions { get; private set; } = new();
    public List<SelectListItem> SaucerOptions { get; private set; } = new();
    public List<SelectListItem> UseOptions { get; private set; } = new();
    public List<UseRow> Uses { get; private set; } = new();

    public int Occupied => Uses.Sum(u => u.Quantity);
    public int Free => Math.Max(0, Input.Quantity - Occupied);

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Id = id;
        if (id is not null)
        {
            var pot = await LoadAsync(id.Value);
            if (pot is null) return NotFound();
            Input = ToInput(pot);
        }
        await PrepareAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Id = id;
        Pot? pot = null;
        if (id is not null)
        {
            pot = await db.Pots.FirstOrDefaultAsync(p => p.Id == id);
            if (pot is null) return NotFound();
        }

        await ValidateAsync(id);
        if (!ModelState.IsValid)
        {
            await PrepareAsync(id);
            return Page();
        }

        if (pot is null)
        {
            pot = new Pot();
            db.Pots.Add(pot);
        }
        Apply(pot);
        await db.SaveChangesAsync();

        // A new group continues on the edit page, where the plants in it can be entered
        return id is null
            ? RedirectToPage("Edit", new { id = pot.Id })
            : RedirectToPage("Index");
    }

    // Marks some of the free pots as used, with a first plant (a plant type or a variety)
    public async Task<IActionResult> OnPostAddUseAsync(int id, string? choice, int quantity)
    {
        var pot = await db.Pots.Include(p => p.Uses).FirstOrDefaultAsync(p => p.Id == id);
        if (pot is null) return NotFound();

        var (typeId, plantId) = await ParseChoiceAsync(choice);
        string? error = null;
        if (typeId is null) error = L["PotError_UseInvalid"];
        else if (quantity < 1) error = L["PotError_UseQuantity"];
        else if (quantity > pot.FreeQuantity) error = L["PotError_UseTooMany", pot.FreeQuantity];

        if (error is not null) return await PageWithErrorAsync(pot, error);

        pot.Uses.Add(new PotUse
        {
            Quantity = quantity,
            Plants = { new PotUsePlant { PlantTypeId = typeId!.Value, PlantId = plantId } }
        });
        await db.SaveChangesAsync();
        return RedirectToPage("Edit", new { id });
    }

    // Another plant that grows in the same pots of an entry
    public async Task<IActionResult> OnPostAddPlantAsync(int id, int useId, string? choice)
    {
        var use = await db.PotUses.Include(u => u.Plants)
            .FirstOrDefaultAsync(u => u.Id == useId && u.PotId == id);
        if (use is null) return NotFound();

        var (typeId, plantId) = await ParseChoiceAsync(choice);
        if (typeId is null)
        {
            var pot = await db.Pots.FirstAsync(p => p.Id == id);
            return await PageWithErrorAsync(pot, L["PotError_UseInvalid"]);
        }

        if (!use.Plants.Any(x => x.PlantTypeId == typeId && x.PlantId == plantId))
        {
            use.Plants.Add(new PotUsePlant { PlantTypeId = typeId.Value, PlantId = plantId });
            await db.SaveChangesAsync();
        }
        return RedirectToPage("Edit", new { id });
    }

    // Removes one plant from an entry; an entry without plants is removed as well
    public async Task<IActionResult> OnPostRemovePlantAsync(int id, int useId, int plantRowId)
    {
        var use = await db.PotUses.Include(u => u.Plants)
            .FirstOrDefaultAsync(u => u.Id == useId && u.PotId == id);
        if (use is not null)
        {
            var plant = use.Plants.FirstOrDefault(x => x.Id == plantRowId);
            if (plant is not null) use.Plants.Remove(plant);
            if (use.Plants.Count == 0) db.PotUses.Remove(use);
            await db.SaveChangesAsync();
        }
        return RedirectToPage("Edit", new { id });
    }

    public async Task<IActionResult> OnPostRemoveUseAsync(int id, int useId)
    {
        var use = await db.PotUses.FirstOrDefaultAsync(u => u.Id == useId && u.PotId == id);
        if (use is not null)
        {
            db.PotUses.Remove(use);
            await db.SaveChangesAsync();
        }
        return RedirectToPage("Edit", new { id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        // The entries in the group are deleted with it (cascade)
        var pot = await db.Pots.FirstOrDefaultAsync(p => p.Id == id);
        if (pot is not null)
        {
            db.Pots.Remove(pot);
            await db.SaveChangesAsync();
        }
        return RedirectToPage("Index");
    }

    // ---------------------------------------------------------------- helpers

    private async Task<IActionResult> PageWithErrorAsync(Pot pot, string error)
    {
        Id = pot.Id;
        Input = ToInput(pot);
        ModelState.AddModelError(string.Empty, error);
        await PrepareAsync(pot.Id);
        return Page();
    }

    private Task<Pot?> LoadAsync(int id) =>
        db.Pots.AsNoTracking().Include(p => p.Uses).FirstOrDefaultAsync(p => p.Id == id);

    // "t:12" is a plant type, "p:34" a variety of the user
    private async Task<(int? TypeId, int? PlantId)> ParseChoiceAsync(string? choice)
    {
        if (string.IsNullOrEmpty(choice) || choice.Length < 3 || !int.TryParse(choice[2..], out var number))
            return (null, null);

        if (choice.StartsWith("t:"))
        {
            return await db.PlantTypes.AnyAsync(t => t.Id == number) ? (number, null) : (null, null);
        }
        if (choice.StartsWith("p:"))
        {
            // The query filter only finds the user's own plants
            var plant = await db.Plants.AsNoTracking().FirstOrDefaultAsync(p => p.Id == number);
            return plant is null ? (null, null) : (plant.PlantTypeId, plant.Id);
        }
        return (null, null);
    }

    private async Task ValidateAsync(int? id)
    {
        if (Input.Quantity < 0) ModelState.AddModelError("Input.Quantity", L["PotError_Quantity"]);
        if (Input.SetSize < 1) ModelState.AddModelError("Input.SetSize", L["PotError_SetSize"]);

        void Positive(string field, decimal? value)
        {
            if (value is not null && value <= 0)
                ModelState.AddModelError($"Input.{field}", L["PotError_Measure"]);
        }
        Positive(nameof(Input.TopCm), Input.TopCm);
        Positive(nameof(Input.BottomCm), Input.BottomCm);
        Positive(nameof(Input.HeightCm), Input.HeightCm);

        if (Input.SaucerId is not null && !await db.Saucers.AnyAsync(s => s.Id == Input.SaucerId))
            Input.SaucerId = null;

        if (id is not null)
        {
            var occupied = await db.PotUses.Where(u => u.PotId == id).SumAsync(u => u.Quantity);
            if (Input.Quantity < occupied)
                ModelState.AddModelError("Input.Quantity", L["PotError_QuantityBelowUse", occupied]);
        }
    }

    private void Apply(Pot pot)
    {
        pot.Kind = Input.Kind;
        pot.Shape = Input.Shape;
        pot.TopCm = Input.TopCm;
        pot.BottomCm = Input.BottomCm;
        pot.HeightCm = Input.HeightCm;
        pot.Quantity = Input.Quantity;
        pot.SetSize = Input.SetSize;
        pot.Color = Clean(Input.Color);
        pot.Code = Clean(Input.Code);
        pot.Note = Clean(Input.Note);
        pot.SaucerId = Input.SaucerId;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static InputModel ToInput(Pot pot) => new()
    {
        Kind = pot.Kind,
        Shape = pot.Shape,
        TopCm = pot.TopCm,
        BottomCm = pot.BottomCm,
        HeightCm = pot.HeightCm,
        Quantity = pot.Quantity,
        SetSize = pot.SetSize,
        Color = pot.Color,
        Code = pot.Code,
        Note = pot.Note,
        SaucerId = pot.SaucerId
    };

    // ---------------------------------------------------------------- dropdown lists and entries

    private async Task PrepareAsync(int? id)
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);

        KindOptions = Enum.GetValues<PotKind>()
            .Select(v => new SelectListItem(L["PotKind_" + v].Value, v.ToString())).ToList();
        ShapeOptions = Enum.GetValues<PotShape>()
            .Select(v => new SelectListItem(L["PotShape_" + v].Value, v.ToString())).ToList();

        var saucers = await db.Saucers.AsNoTracking().OrderBy(s => s.DiameterCm).ThenBy(s => s.Color).ToListAsync();
        SaucerOptions = saucers
            .Select(s => new SelectListItem(
                $"{s.DiameterCm.ToString("0.#", CultureInfo.CurrentCulture)} {s.Color}".Trim(), s.Id.ToString()))
            .ToList();
        SaucerOptions.Insert(0, new SelectListItem("–", ""));

        if (id is null) return;

        var types = await db.PlantTypes.AsNoTracking().Include(t => t.Translations).ToListAsync();
        var typeNames = types.ToDictionary(t => t.Id, t => TypeName(t, language));
        var plants = await db.Plants.AsNoTracking().ToListAsync();

        // One group per plant type: the type itself, then the user's varieties of it
        UseOptions = new List<SelectListItem> { new("–", "") };
        foreach (var type in types.OrderBy(t => typeNames[t.Id], comparer))
        {
            var group = new SelectListGroup { Name = typeNames[type.Id] };
            UseOptions.Add(new SelectListItem(
                $"{typeNames[type.Id]} ({L["PotUse_NoVariety"]})", $"t:{type.Id}") { Group = group });
            foreach (var plant in plants.Where(p => p.PlantTypeId == type.Id && p.Name != "(unnamed)")
                         .OrderBy(p => p.Name, comparer))
            {
                UseOptions.Add(new SelectListItem(
                    $"{typeNames[type.Id]} – {plant.Name}", $"p:{plant.Id}") { Group = group });
            }
        }

        var uses = await db.PotUses.AsNoTracking().Where(u => u.PotId == id)
            .Include(u => u.Plants).ThenInclude(x => x.Plant)
            .ToListAsync();
        Uses = uses
            .Select(u => new UseRow(
                u.Id,
                u.Quantity,
                u.Plants
                    .Select(x => new UsePlantRow(
                        x.Id,
                        x.Plant is not null && x.Plant.Name != "(unnamed)"
                            ? $"{typeNames[x.PlantTypeId]} – {x.Plant.Name}"
                            : typeNames[x.PlantTypeId]))
                    .OrderBy(x => x.Name, comparer)
                    .ToList()))
            .OrderBy(u => u.Plants.FirstOrDefault()?.Name, comparer)
            .ToList();
    }

    private static string TypeName(PlantType type, string language) =>
        type.Translations.FirstOrDefault(t => t.Language == language)?.Name ?? type.Name;
}
