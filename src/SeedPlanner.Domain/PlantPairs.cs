namespace SeedPlanner.Domain;

/// <summary>Possible partners: good neighbours where one is a main plant and the other a secondary plant.</summary>
public static class PlantPairs
{
    /// <summary>
    /// For every plant type with a role the plant types of the other role that are good neighbours:
    /// the secondary plants for a main plant, the main plants for a secondary plant. Sorted by id.
    /// </summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<int>> Partners(
        IReadOnlyDictionary<int, PlantRole?> roles, IEnumerable<PlantTypeCompanion> companions)
    {
        var result = roles.Where(r => r.Value is not null).ToDictionary(r => r.Key, _ => new SortedSet<int>());
        foreach (var c in companions)
        {
            if (c.Relation != CompanionRelation.Good) continue;
            if (!roles.TryGetValue(c.PlantTypeId, out var a) || !roles.TryGetValue(c.OtherPlantTypeId, out var b)) continue;
            if (a is null || b is null || a == b) continue;
            result[c.PlantTypeId].Add(c.OtherPlantTypeId);
            result[c.OtherPlantTypeId].Add(c.PlantTypeId);
        }
        return result.ToDictionary(r => r.Key, r => (IReadOnlyList<int>)r.Value.ToList());
    }

    /// <summary>A change of the user: a secondary plant added to a main plant, or a suggested one removed.</summary>
    public record PartnerChange(int MainId, int SecondaryId, bool Included);

    /// <summary>
    /// For every main plant its secondary plants: the suggestion (good neighbours) without the ones the user removed,
    /// plus the ones the user added. Changes that do not fit the roles (any more) are ignored. Sorted by id.
    /// </summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<int>> MainPartners(
        IReadOnlyDictionary<int, PlantRole?> roles, IEnumerable<PlantTypeCompanion> companions, IEnumerable<PartnerChange> changes)
    {
        var suggested = Partners(roles, companions);
        var result = roles.Where(r => r.Value == PlantRole.Main)
            .ToDictionary(r => r.Key, r => new SortedSet<int>(suggested[r.Key]));
        foreach (var change in changes)
        {
            if (!result.TryGetValue(change.MainId, out var set)) continue;
            if (!roles.TryGetValue(change.SecondaryId, out var role) || role != PlantRole.Secondary) continue;
            if (change.Included) set.Add(change.SecondaryId);
            else set.Remove(change.SecondaryId);
        }
        return result.ToDictionary(r => r.Key, r => (IReadOnlyList<int>)r.Value.ToList());
    }
}
