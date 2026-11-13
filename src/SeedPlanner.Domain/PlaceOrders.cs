namespace SeedPlanner.Domain;

/// <summary>
/// The kinds of container a plant type or variety may be put into, in the order of the user's choice
/// (first choice first). Stored as text, e.g. "Bed,GrowBag,Pot". Empty = not set.
/// </summary>
public static class PlaceOrders
{
    public static List<ContainerCategory> Parse(string? text)
    {
        var result = new List<ContainerCategory>();
        foreach (var part in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Enum.TryParse<ContainerCategory>(part, out var c) && !result.Contains(c)) result.Add(c);
        return result;
    }

    public static string? Format(IEnumerable<ContainerCategory> order)
    {
        var list = order.Distinct().ToList();
        return list.Count == 0 ? null : string.Join(',', list);
    }

    public static ContainerCategory CategoryOf(PotKind kind) => kind switch
    {
        PotKind.Bowl => ContainerCategory.Bowl,
        PotKind.GrowBag => ContainerCategory.GrowBag,
        _ => ContainerCategory.Pot
    };
}
