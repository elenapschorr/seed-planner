using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;

namespace SeedPlanner.Api.Pages;

/// <summary>
/// The input for the order of the kinds of container (first choice, second choice, ...) of a plant type or variety.
/// A kind can be chosen only once; the stored value is <see cref="PlaceOrders"/>.
/// </summary>
public sealed class PlaceOrderField
{
    public const int Slots = 4;

    /// <summary>Name of the form field (the same for all selects).</summary>
    public required string Name { get; init; }
    public required List<string?> Values { get; init; }
    public required string FirstEmptyText { get; init; }
    public required string Label { get; init; }
    public required string Help { get; init; }
    public required string ChoiceText { get; init; }
    public required List<SelectListItem> Options { get; init; }

    public static List<SelectListItem> CategoryOptions(IStringLocalizer<SharedResource> L) =>
        Enum.GetValues<ContainerCategory>()
            .Select(c => new SelectListItem(L["ContainerCategory_" + c].Value, c.ToString())).ToList();

    /// <summary>The stored order as the values of the selects (empty strings for the unused ones).</summary>
    public static List<string?> ToValues(string? stored)
    {
        var values = PlaceOrders.Parse(stored).Select(c => (string?)c.ToString()).ToList();
        while (values.Count < Slots) values.Add("");
        return values;
    }

    /// <summary>The chosen kinds from the posted values; null if a kind was chosen twice.</summary>
    public static List<ContainerCategory>? FromValues(IEnumerable<string?>? values)
    {
        var order = new List<ContainerCategory>();
        foreach (var v in values ?? Array.Empty<string?>())
        {
            if (!Enum.TryParse<ContainerCategory>(v, out var c)) continue;
            if (order.Contains(c)) return null;
            order.Add(c);
        }
        return order;
    }

    /// <summary>"Bed → Grow bag" for display.</summary>
    public static string Describe(string? stored, IStringLocalizer<SharedResource> L) =>
        string.Join(" → ", PlaceOrders.Parse(stored).Select(c => L["ContainerCategory_" + c].Value));
}
