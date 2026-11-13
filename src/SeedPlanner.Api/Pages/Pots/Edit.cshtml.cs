using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Pots;

// One group of pots: the data of the group, the number of pots and the plants in the single pots
public class EditModel(SeedPlannerDbContext db, IStringLocalizer<SharedResource> L) : PageModel
{
    public class InputModel
    {
        [Required, StringLength(100)]
        public string Name { get; set; } = "";

        public PotKind Kind { get; set; } = PotKind.Pot;
        public PotShape Shape { get; set; } = PotShape.Round;

        public decimal? TopCm { get; set; }
        public decimal? BottomCm { get; set; }
        public decimal? HeightCm { get; set; }

        // Number of single pots in the group
        public int Quantity { get; set; } = 1;
        public int SetSize { get; set; } = 1;

        public string? Color { get; set; }
        public string? Code { get; set; }
        public string? Note { get; set; }

        [System.ComponentModel.DataAnnotations.Range(0, 14)]
        public decimal? SoilPh { get; set; }
        public SoilType? SoilType { get; set; }

        public int? SaucerId { get; set; }
    }

    public record PlantingRow(int Id, string Name, int? PlantCount, string? Note, string Choice);

    // Number is the position of the pot in the group (1, 2, 3 ...)
    public record PotRow(int Id, int Number, List<PlantingRow> Plantings);

    public int? Id { get; private set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public List<SelectListItem> KindOptions { get; private set; } = new();
    public List<SelectListItem> ShapeOptions { get; private set; } = new();
    public List<SelectListItem> SoilOptions { get; private set; } = new();
    public List<SelectListItem> SaucerOptions { get; private set; } = new();
    public List<SelectListItem> PlantOptions { get; private set; } = new();

    // Pots with plants (listed one by one); the free pots are only counted
    public List<PotRow> UsedPots { get; private set; } = new();
    public int Quantity { get; private set; }
    public int Free { get; private set; }
    public int Occupied => Quantity - Free;

    // Message for the planting section (shown right there, not at the top of the page)
    public string? PlantingError { get; private set; }

    // The planting that is being edited right now (its row shows a form instead of the text)
    public int? EditingId { get; private set; }

    // Plant list with the current plant of the edited planting selected
    public List<SelectListItem> EditOptions { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id, int? editPlanting)
    {
        Id = id;
        EditingId = editPlanting;
        if (id is not null)
        {
            var group = await db.PotGroups.AsNoTracking().Include(g => g.Pots).FirstOrDefaultAsync(g => g.Id == id);
            if (group is null) return NotFound();
            Input = ToInput(group);
        }
        await PrepareAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Id = id;
        PotGroup? group = null;
        if (id is not null)
        {
            group = await db.PotGroups.Include(g => g.Pots).ThenInclude(p => p.Plantings)
                .FirstOrDefaultAsync(g => g.Id == id);
            if (group is null) return NotFound();
        }

        await ValidateAsync(id, group);
        if (!ModelState.IsValid)
        {
            await PrepareAsync(id);
            return Page();
        }

        if (group is null)
        {
            group = new PotGroup();
            db.PotGroups.Add(group);
        }
        Apply(group);
        SetQuantity(group, Input.Quantity);
        await db.SaveChangesAsync();

        // A new group continues on the edit page, where the plants in it can be entered
        return id is null
            ? RedirectToPage("Edit", new { id = group.Id })
            : RedirectToPage("Index");
    }

    // Plants some of the free pots, e.g. "2 free pots with blueberry"
    public async Task<IActionResult> OnPostAddToFreePotsAsync(int id, string? choice, int quantity, int? plantCount)
    {
        var group = await LoadWithPotsAsync(id);
        if (group is null) return NotFound();

        var (typeId, plantId) = await ParseChoiceAsync(choice);
        var free = group.Pots.Where(p => p.IsFree).OrderBy(p => p.Id).ToList();

        string? error = null;
        if (typeId is null) error = L["PotError_UseInvalid"];
        else if (quantity < 1) error = L["PotError_UseQuantity"];
        else if (quantity > free.Count) error = L["PotError_UseTooMany", free.Count];
        else if (plantCount is < 1) error = L["PotError_PlantCount"];
        if (error is not null) return await PageWithErrorAsync(group, error);

        foreach (var pot in free.Take(quantity))
        {
            pot.Plantings.Add(NewPlanting(typeId!.Value, plantId, plantCount));
        }
        await db.SaveChangesAsync();
        return RedirectToPage("Edit", new { id });
    }

    // Another plant in a pot that is already planted
    public async Task<IActionResult> OnPostAddToPotAsync(int id, int potId, string? choice, int? plantCount)
    {
        var group = await LoadWithPotsAsync(id);
        var pot = group?.Pots.FirstOrDefault(p => p.Id == potId);
        if (group is null || pot is null) return NotFound();

        var (typeId, plantId) = await ParseChoiceAsync(choice);
        string? error = null;
        if (typeId is null) error = L["PotError_UseInvalid"];
        else if (plantCount is < 1) error = L["PotError_PlantCount"];
        else if (pot.Plantings.Any(x => x.PlantTypeId == typeId && x.PlantId == plantId)) error = L["PotError_AlreadyThere"];
        if (error is not null) return await PageWithErrorAsync(group, error);

        pot.Plantings.Add(NewPlanting(typeId!.Value, plantId, plantCount));
        await db.SaveChangesAsync();
        return RedirectToPage("Edit", new { id });
    }

    // Changes plant, number of plants and note of an existing planting
    public async Task<IActionResult> OnPostUpdatePlantingAsync(int id, int plantingId, string? choice, int? plantCount, string? note)
    {
        var group = await LoadWithPotsAsync(id);
        var planting = group?.Pots.SelectMany(p => p.Plantings).FirstOrDefault(x => x.Id == plantingId);
        if (group is null || planting is null) return NotFound();

        var (typeId, plantId) = await ParseChoiceAsync(choice);
        var pot = group.Pots.First(p => p.Plantings.Contains(planting));
        string? error = null;
        if (typeId is null) error = L["PotError_UseInvalid"];
        else if (plantCount is < 1) error = L["PotError_PlantCount"];
        else if (pot.Plantings.Any(x => x.Id != plantingId && x.PlantTypeId == typeId && x.PlantId == plantId))
            error = L["PotError_AlreadyThere"];
        if (error is not null)
        {
            EditingId = plantingId;
            return await PageWithErrorAsync(group, error);
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
        // The query filter only finds the user's own plantings; the pot must belong to this group
        var planting = await db.Plantings.FirstOrDefaultAsync(x => x.Id == plantingId && x.Pot!.PotGroupId == id);
        if (planting is not null)
        {
            db.Plantings.Remove(planting);
            await db.SaveChangesAsync();
        }
        return RedirectToPage("Edit", new { id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        // The single pots and their plantings are deleted with the group (cascade)
        var group = await db.PotGroups.FirstOrDefaultAsync(g => g.Id == id);
        if (group is not null)
        {
            db.PotGroups.Remove(group);
            await db.SaveChangesAsync();
        }
        return RedirectToPage("Index");
    }

    // ---------------------------------------------------------------- helpers

    private static Planting NewPlanting(int typeId, int? plantId, int? plantCount) =>
        new() { PlantTypeId = typeId, PlantId = plantId, PlantCount = plantCount };

    private Task<PotGroup?> LoadWithPotsAsync(int id) =>
        db.PotGroups.Include(g => g.Pots).ThenInclude(p => p.Plantings).FirstOrDefaultAsync(g => g.Id == id);

    private async Task<IActionResult> PageWithErrorAsync(PotGroup group, string error)
    {
        Id = group.Id;
        Input = ToInput(group);
        PlantingError = error;
        await PrepareAsync(group.Id);
        return Page();
    }

    private Task<(int? TypeId, int? PlantId)> ParseChoiceAsync(string? choice) => PlantChoices.ParseAsync(db, choice);

    private async Task ValidateAsync(int? id, PotGroup? group)
    {
        Input.Name = Input.Name?.Trim() ?? "";
        if (Input.Name.Length > 0 &&
            await db.PotGroups.AnyAsync(g => g.Name == Input.Name && g.Id != (id ?? 0)))
        {
            ModelState.AddModelError("Input.Name", L["PotError_NameExists"]);
        }

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

        // Fewer pots: only free pots can be taken away
        if (group is not null && Input.Quantity < group.Pots.Count)
        {
            var removable = group.Pots.Count(p => p.IsFree);
            var toRemove = group.Pots.Count - Input.Quantity;
            if (toRemove > removable)
                ModelState.AddModelError("Input.Quantity", L["PotError_QuantityBelowUse", group.Pots.Count - removable]);
        }
    }

    private void Apply(PotGroup group)
    {
        group.Name = Input.Name;
        group.Kind = Input.Kind;
        group.Shape = Input.Shape;
        group.TopCm = Input.TopCm;
        group.BottomCm = Input.BottomCm;
        group.HeightCm = Input.HeightCm;
        group.SetSize = Input.SetSize;
        group.Color = Clean(Input.Color);
        group.Code = Clean(Input.Code);
        group.Note = Clean(Input.Note);
        group.SoilPh = Input.SoilPh;
        group.SoilType = Input.SoilType;
        group.SaucerId = Input.SaucerId;
    }

    // Adds single pots, or removes free ones (the newest first)
    private void SetQuantity(PotGroup group, int quantity)
    {
        while (group.Pots.Count < quantity)
        {
            group.Pots.Add(new Pot());
        }
        if (group.Pots.Count > quantity)
        {
            var remove = group.Pots.Where(p => p.IsFree).OrderByDescending(p => p.Id)
                .Take(group.Pots.Count - quantity).ToList();
            foreach (var pot in remove)
            {
                group.Pots.Remove(pot);
                db.Pots.Remove(pot);
            }
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static InputModel ToInput(PotGroup group) => new()
    {
        Name = group.Name,
        Kind = group.Kind,
        Shape = group.Shape,
        TopCm = group.TopCm,
        BottomCm = group.BottomCm,
        HeightCm = group.HeightCm,
        Quantity = group.Pots.Count,
        SetSize = group.SetSize,
        Color = group.Color,
        Code = group.Code,
        Note = group.Note,
        SoilPh = group.SoilPh,
        SoilType = group.SoilType,
        SaucerId = group.SaucerId
    };

    // ---------------------------------------------------------------- dropdown lists and pots

    private async Task PrepareAsync(int? id)
    {
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);

        KindOptions = Enum.GetValues<PotKind>()
            .Select(v => new SelectListItem(L["PotKind_" + v].Value, v.ToString())).ToList();
        ShapeOptions = Enum.GetValues<PotShape>()
            .Select(v => new SelectListItem(L["PotShape_" + v].Value, v.ToString())).ToList();
        SoilOptions = Enum.GetValues<SoilType>()
            .Select(v => new SelectListItem(L["SoilType_" + v].Value, v.ToString())).ToList();
        SoilOptions.Insert(0, new SelectListItem("–", ""));

        var saucers = await db.Saucers.AsNoTracking().OrderBy(s => s.DiameterCm).ThenBy(s => s.Color).ToListAsync();
        SaucerOptions = saucers
            .Select(s => new SelectListItem(
                $"{s.DiameterCm.ToString("0.#", CultureInfo.CurrentCulture)} {s.Color}".Trim(), s.Id.ToString()))
            .ToList();
        SaucerOptions.Insert(0, new SelectListItem("–", ""));

        if (id is null) return;

        var (options, typeNames) = await PlantChoices.LoadAsync(db, L);
        PlantOptions = options;

        var pots = await db.Pots.AsNoTracking()
            .Where(p => p.PotGroupId == id)
            .Include(p => p.Plantings).ThenInclude(x => x.Plant)
            .OrderBy(p => p.Id)
            .ToListAsync();
        Quantity = pots.Count;
        Free = pots.Count(p => p.IsFree);
        UsedPots = pots
            .Select((p, index) => new PotRow(
                p.Id,
                index + 1,
                p.Plantings
                    .Select(x => new PlantingRow(
                        x.Id, PlantChoices.NameOf(x, typeNames), x.PlantCount, x.Note, PlantChoices.ValueOf(x)))
                    .OrderBy(x => x.Name, comparer)
                    .ToList()))
            .Where(p => p.Plantings.Count > 0)
            .ToList();

        if (EditingId is not null)
        {
            var current = UsedPots.SelectMany(p => p.Plantings).FirstOrDefault(x => x.Id == EditingId)?.Choice;
            EditOptions = PlantChoices.WithSelected(PlantOptions, current);
        }
    }
}
