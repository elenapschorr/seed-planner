namespace SeedPlanner.Domain;

// Does a plant fit into a container? The order of the plants and the rules about space, height, soil and neighbours.
public static partial class PlanAllocator
{
    // Cold frame plants first (they only use covered beds), then from large to small
    private static IEnumerable<AllocPlant> Order(IEnumerable<AllocPlant> units) => units
        .OrderByDescending(p => p.ColdFrame)
        .ThenBy(p => IsSecondary(p) ? 1 : 0)
        .ThenByDescending(p => p.SpaceScore)
        .ThenByDescending(p => OccupancyMask.Bits(p.Slots))
        .ThenByDescending(p => p.HeightCm ?? 0)
        .ThenBy(p => p.PlantId);

    private static bool IsSecondary(AllocPlant p) => p.Role == PlantRole.Secondary;

    // A main plant is never put under another plant
    private static bool MayBeCompanion(AllocPlant p) => p.Role != PlantRole.Main;

    // potFallback: the plants of the open ground may also use pots (only after the beds and the pot plants)
    private static bool Allowed(AllocPlant p, ContainerKind kind, bool potFallback) => p.Place switch
    {
        PlantPlace.Either => true,
        PlantPlace.Pot => kind == ContainerKind.Pot,
        PlantPlace.Bed => kind == ContainerKind.Bed,
        PlantPlace.BedThenPot => kind == ContainerKind.Bed || potFallback,
        _ => false
    };

    // Cold frame plants only go into covered beds. Other plants stay out of covered beds unless the user allowed it.
    private static bool Fits(AllocPlant p, Box b, Rules rel, bool potFallback) => FitsReject(p, b, rel, potFallback) is null;

    // Why the container is not suitable for the plant at all (null = it is)
    private static RejectCode? FitsReject(AllocPlant p, Box b, Rules rel, bool potFallback)
    {
        // A bed has to be at least as wide as the distance between the rows
        if (b.Kind == ContainerKind.Bed && b.WidthCm is { } width && p.RowSpacingCm is { } row && row > width + Eps) return RejectCode.BedTooNarrow;
        // The soil pH has to be inside the range the plant likes (with a small tolerance); unknown values do not restrict
        if (!PhOk(p, b)) return RejectCode.SoilPh;
        // Only the kinds of container the user allowed for the plant
        if (p.Order is { Count: > 0 } order && !order.Contains(b.Category)) return RejectCode.CategoryNotChosen;
        // No vegetables under trees, and no trees among vegetables
        if ((p.IsVegetable && b.HasTree) || (p.IsTree && b.HasVegetable)) return RejectCode.TreeAndVegetable;
        if (p.ColdFrame) return b.Kind == ContainerKind.Bed && b.HasCover ? null : RejectCode.NeedsCover;
        if (!Allowed(p, b.Kind, potFallback)) return RejectCode.WrongKind;
        return !b.HasCover || rel.AllowOthersInCovered ? null : RejectCode.CoveredBed;
    }

    /// <summary>How far the soil pH of a container may be outside the range of the plant.</summary>
    public const decimal PhTolerance = 0.5m;

    private static bool PhOk(AllocPlant p, Box b)
    {
        if (b.SoilPh is not { } ph) return true;
        if (p.PhMin is { } min && ph < min - PhTolerance) return false;
        if (p.PhMax is { } max && ph > max + PhTolerance) return false;
        return true;
    }

    // The place of the container kind in the user's order of choice (0 = first choice)
    private static int OrderRank(AllocPlant p, Box b) =>
        p.Order is { Count: > 0 } order ? Math.Max(0, order.ToList().IndexOf(b.Category)) : 0;

    // 0 = the soil is what the plant likes (or unknown), 1 = another kind of soil. Only used to choose between containers.
    private static int SoilRank(AllocPlant p, Box b) =>
        p.SoilType is { } want && b.SoilType is { } have && want != have ? 1 : 0;

    private static double? Need(AllocPlant p, ContainerKind kind) =>
        kind == ContainerKind.Pot ? p.PotLitersMin : p.AreaM2;

    private static bool NeighboursOk(Box c, AllocPlant p, Rules rel) =>
        c.TypeTimes.All(t => !OccupancyMask.Overlaps(t.Slots, p.Slots) || rel.Between(t.TypeId, p.PlantTypeId) != CompanionRelation.Bad);

    // A main plant of the container is a partner of the plant (the user's partner list), at the same time
    private static bool HasPartner(Box c, AllocPlant p, Rules rel) =>
        c.TypeTimes.Any(t => OccupancyMask.Overlaps(t.Slots, p.Slots) && rel.IsPartner(t.TypeId, p.PlantTypeId));

    private static bool HasGood(Box c, AllocPlant p, Rules rel) =>
        c.TypeTimes.Any(t => OccupancyMask.Overlaps(t.Slots, p.Slots) && rel.Between(t.TypeId, p.PlantTypeId) == CompanionRelation.Good);

    private static void Put(Box c, AllocPlant p, PlantingRole role)
    {
        var need = Need(p, c.Kind) ?? 0;
        for (var s = 0; s < OccupancyMask.Count; s++)
            if ((p.Slots & (1UL << s)) != 0) c.UsedAt[s] += need;
        c.TypeIds.Add(p.PlantTypeId);
        c.TypeTimes.Add((p.PlantTypeId, p.Slots));
        if (p.IsTree) c.HasTree = true;
        if (p.IsVegetable) c.HasVegetable = true;
        c.Placements.Add((p, role));
        if (p.ColdFrame) c.ColdCount++;
        if (role != PlantingRole.Main) return;
        c.MainMask |= p.Slots;
        if (p.HeightCm is not null) c.MainHeights.Add((p.Slots, p.HeightCm.Value));
    }
}
