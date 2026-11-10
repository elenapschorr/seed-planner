using System.Globalization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages;

/// <summary>
/// The dropdown "what grows here" for pots and beds: a plant type without a variety ("t:12")
/// or one of the user's varieties ("p:34").
/// </summary>
public static class PlantChoices
{
    public record Lists(List<SelectListItem> Options, Dictionary<int, string> TypeNames);

    /// <summary>One option group per plant type: the type itself, then the user's varieties of it.</summary>
    public static async Task<Lists> LoadAsync(SeedPlannerDbContext db, IStringLocalizer L)
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);

        var types = await db.PlantTypes.AsNoTracking().Include(t => t.Translations).ToListAsync();
        var typeNames = types.ToDictionary(t => t.Id, t => TypeName(t, language));
        var plants = await db.Plants.AsNoTracking().ToListAsync();

        var options = new List<SelectListItem> { new("–", "") };
        foreach (var type in types.OrderBy(t => typeNames[t.Id], comparer))
        {
            var group = new SelectListGroup { Name = typeNames[type.Id] };
            options.Add(new SelectListItem(
                $"{typeNames[type.Id]} ({L["PotUse_NoVariety"]})", $"t:{type.Id}") { Group = group });
            foreach (var plant in plants.Where(p => p.PlantTypeId == type.Id && p.Name != "(unnamed)")
                         .OrderBy(p => p.Name, comparer))
            {
                options.Add(new SelectListItem(
                    $"{typeNames[type.Id]} – {plant.Name}", $"p:{plant.Id}") { Group = group });
            }
        }
        return new Lists(options, typeNames);
    }

    /// <summary>Turns "t:12" or "p:34" back into plant type and variety; (null, null) if invalid.</summary>
    public static async Task<(int? TypeId, int? PlantId)> ParseAsync(SeedPlannerDbContext db, string? choice)
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

    /// <summary>The dropdown value of an existing planting.</summary>
    public static string ValueOf(Planting x) => x.PlantId is not null ? $"p:{x.PlantId}" : $"t:{x.PlantTypeId}";

    /// <summary>"Tomato – Matina", or only the type if no variety is known.</summary>
    public static string NameOf(Planting x, Dictionary<int, string> typeNames) =>
        x.Plant is not null && x.Plant.Name != "(unnamed)"
            ? $"{typeNames[x.PlantTypeId]} – {x.Plant.Name}"
            : typeNames[x.PlantTypeId];

    /// <summary>The same option list with one value selected (for the row that is being edited).</summary>
    public static List<SelectListItem> WithSelected(List<SelectListItem> options, string? value) =>
        options.Select(o => new SelectListItem(o.Text, o.Value, o.Value == value) { Group = o.Group }).ToList();

    private static string TypeName(PlantType type, string language) =>
        type.Translations.FirstOrDefault(t => t.Language == language)?.Name ?? type.Name;
}
