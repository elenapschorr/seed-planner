namespace SeedPlanner.Domain;

// Placing the plants: main plants, under-planting (companions) and the log of why a plant did not fit.
public static partial class PlanAllocator
{
    // First everything is placed as far as the rules allow, the plants of the open ground only into beds.
    // What of the open ground is left then takes the pots that are still free (after the pot plants).
    // top: the run of the distribution itself (not the tries with a new bed); only there the failures are written down
    private static List<AllocPlant> PlaceAll(IEnumerable<AllocPlant> units, List<Box> boxes, Rules rel, bool top = true)
    {
        var left = new List<AllocPlant>();
        if (top) rel.Trace?.Add(new TraceEntry(TraceCode.PhaseMain));
        foreach (var u in Order(units))
        {
            // Secondary plants wait for a main plant
            if (IsSecondary(u)) left.Add(u);
            else if (!TryPlaceMain(u, boxes, rel, potFallback: false, logFailure: top && u.Place != PlantPlace.BedThenPot)) left.Add(u);
        }
        if (top) rel.Trace?.Add(new TraceEntry(TraceCode.PhaseCompanions));
        left = PlaceCompanions(left, boxes, rel, potFallback: false, logFailure: top);
        left = PlaceSecondaryAlone(left, boxes, rel, potFallback: false, top);

        if (left.Any(p => p.Place == PlantPlace.BedThenPot))
        {
            if (top) rel.Trace?.Add(new TraceEntry(TraceCode.PhasePotFallback));
            var rest = new List<AllocPlant>();
            foreach (var u in Order(left))
                if (u.Place != PlantPlace.BedThenPot || IsSecondary(u) || !TryPlaceMain(u, boxes, rel, potFallback: true, logFailure: top)) rest.Add(u);
            if (top) rel.Trace?.Add(new TraceEntry(TraceCode.PhaseCompanions));
            left = PlaceCompanions(rest, boxes, rel, potFallback: true, logFailure: top);
            left = PlaceSecondaryAlone(left, boxes, rel, potFallback: true, top);
        }
        return left;
    }

    // Secondary plants that found no main plant to stand with get a place of their own, like a main plant
    private static List<AllocPlant> PlaceSecondaryAlone(List<AllocPlant> left, List<Box> boxes, Rules rel, bool potFallback, bool top)
    {
        var rest = new List<AllocPlant>();
        var header = false;
        foreach (var u in Order(left))
        {
            if (!IsSecondary(u))
            {
                rest.Add(u);
                continue;
            }
            if (top && !header)
            {
                rel.Trace?.Add(new TraceEntry(TraceCode.PhaseSecondaryAlone));
                header = true;
            }
            if (!TryPlaceMain(u, boxes, rel, potFallback, logFailure: top && (u.Place != PlantPlace.BedThenPot || potFallback))) rest.Add(u);
        }
        return rest;
    }

    // The sowing is moved within its window to use a container that is already in use, before an empty one is taken:
    // first every time (earliest first) is tried in the containers that already hold plants, then every time in all of them.
    private static bool TryPlaceMain(AllocPlant p, List<Box> boxes, Rules rel, bool potFallback, bool logFailure = false)
    {
        Box? placed;
        if (p.SlotOptions is { Count: > 1 } options)
        {
            foreach (var slots in options)
            {
                var at = p with { Slots = slots };
                if ((placed = TryPlaceMainAt(at, boxes, rel, potFallback, InUse)) is not null)
                {
                    LogMain(rel, at, placed, moved: slots != options[0], emptyBox: false);
                    return true;
                }
            }
            foreach (var slots in options)
            {
                var at = p with { Slots = slots };
                if ((placed = TryPlaceMainAt(at, boxes, rel, potFallback, null)) is not null)
                {
                    LogMain(rel, at, placed, moved: slots != options[0], emptyBox: true);
                    return true;
                }
            }
        }
        else if ((placed = TryPlaceMainAt(p, boxes, rel, potFallback, null)) is not null)
        {
            LogMain(rel, p, placed, moved: false, emptyBox: false);
            return true;
        }

        if (logFailure && rel.Trace is { } log)
            log.Add(new TraceEntry(TraceCode.MainNotPlaced, p.PlantId, Reasons: Reasons(boxes, b => MainReject(b, p, rel, potFallback))));
        return false;
    }

    private static void LogMain(Rules rel, AllocPlant p, Box b, bool moved, bool emptyBox) =>
        rel.Trace?.Add(new TraceEntry(
            emptyBox ? TraceCode.MainPlacedEmptyBox : moved ? TraceCode.MainPlacedMoved : TraceCode.MainPlaced,
            p.PlantId, b.Kind, b.Name, b.IsNew, p.Slots));

    // Something stands in the container at some time (a plant of this run or one that was there before)
    private static bool InUse(Box b) => b.Placements.Count > 0 || b.UsedAt.Any(u => u > Eps) || b.MainMask != 0;

    // A pot gets one main plant (the smallest pot that is large enough); a bed as many as fit by area.
    // only: restricts the containers that are looked at (null = all)
    private static Box? TryPlaceMainAt(AllocPlant p, List<Box> boxes, Rules rel, bool potFallback, Func<Box, bool>? only)
    {
        if (!p.ColdFrame && p.PotLitersMin is { } liters)
        {
            var pot = boxes
                .Where(b => (only is null || only(b)) && b.Kind == ContainerKind.Pot && Fits(p, b, rel, potFallback)
                    && !OccupancyMask.Overlaps(b.MainMask, p.Slots) && b.FreeFor(p.Slots) + Eps >= liters && NeighboursOk(b, p, rel))
                .OrderBy(b => OrderRank(p, b))
                .ThenBy(b => SoilRank(p, b))
                .ThenBy(b => b.Capacity)
                .ThenBy(b => b.Id ?? int.MaxValue)
                .FirstOrDefault();
            if (pot is not null)
            {
                Put(pot, p, PlantingRole.Main);
                return pot;
            }
        }

        if (p.AreaM2 is { } area)
        {
            // Cold frame plants: stay in a bed that already has some, otherwise the bed with the most free area.
            // Other plants: uncovered beds first, then beds with good neighbours, then the tightest fit.
            var bed = boxes
                .Where(b => (only is null || only(b)) && b.Kind == ContainerKind.Bed && Fits(p, b, rel, potFallback) && b.FreeFor(p.Slots) + Eps >= area && NeighboursOk(b, p, rel))
                .OrderBy(b => p.ColdFrame ? (b.ColdCount > 0 ? 0 : 1) : (b.HasCover ? 1 : 0))
                .ThenBy(b => SoilRank(p, b))
                .ThenBy(b => p.ColdFrame ? 0 : (HasGood(b, p, rel) ? 0 : 1))
                .ThenBy(b => p.ColdFrame ? -b.FreeFor(p.Slots) : b.FreeFor(p.Slots))
                .ThenBy(b => b.Id ?? int.MaxValue)
                .FirstOrDefault();
            if (bed is not null)
            {
                Put(bed, p, PlantingRole.Main);
                return bed;
            }
        }

        return null;
    }

    private static bool CanHost(Box c, AllocPlant p, Rules rel, bool potFallback) => HostReject(c, p, rel, potFallback) is null;

    // Why the container cannot take the plant as under-planting (null = it can)
    private static RejectCode? HostReject(Box c, AllocPlant p, Rules rel, bool potFallback)
    {
        if (!OccupancyMask.Overlaps(c.MainMask, p.Slots)) return RejectCode.NoMainPlant;
        if (FitsReject(p, c, rel, potFallback) is { } fit) return fit;
        if (Need(p, c.Kind) is not { } need) return RejectCode.NoSizeData;
        if (c.FreeFor(p.Slots) + Eps < need) return RejectCode.NoRoom;
        if (!NeighboursOk(c, p, rel)) return RejectCode.BadNeighbour;
        if (c.TopAt(p.Slots) is { } top && p.HeightCm is { } height && height >= top) return RejectCode.TooTall;
        return null;
    }

    // Why the container did not take the plant as main plant (null = it would)
    private static RejectCode? MainReject(Box b, AllocPlant p, Rules rel, bool potFallback)
    {
        if (FitsReject(p, b, rel, potFallback) is { } fit) return fit;
        if (b.Kind == ContainerKind.Pot)
        {
            if (p.ColdFrame || p.PotLitersMin is not { } liters) return RejectCode.NoSizeData;
            if (OccupancyMask.Overlaps(b.MainMask, p.Slots)) return RejectCode.MainTaken;
            if (b.FreeFor(p.Slots) + Eps < liters) return RejectCode.NoRoom;
        }
        else
        {
            if (p.AreaM2 is not { } area) return RejectCode.NoSizeData;
            if (b.FreeFor(p.Slots) + Eps < area) return RejectCode.NoRoom;
        }
        return NeighboursOk(b, p, rel) ? null : RejectCode.BadNeighbour;
    }

    private static List<TraceReason> Reasons(IEnumerable<Box> boxes, Func<Box, RejectCode?> reject) =>
        boxes.Select(reject).Where(r => r is not null).GroupBy(r => r!.Value)
            .Select(g => new TraceReason(g.Key, g.Count()))
            .OrderByDescending(r => r.Count).ThenBy(r => r.Code)
            .ToList();

    // Under-planting. Every round the plant with the fewest possible host plant types goes first
    // (then the one with the fewest possible containers, then the larger one); the numbers are recalculated
    // after each placement. Returns the plants that did not fit.
    private static List<AllocPlant> PlaceCompanions(List<AllocPlant> left, List<Box> boxes, Rules rel, bool potFallback, bool logFailure = false)
    {
        // Main plants never go under other plants
        var remaining = left.Where(MayBeCompanion).ToList();
        var unplaced = left.Where(p => !MayBeCompanion(p)).ToList();

        while (remaining.Count > 0)
        {
            AllocPlant? best = null;
            List<Box> bestHosts = new();
            (int, int, double) bestKey = default;
            var dead = new List<AllocPlant>();

            foreach (var p in remaining)
            {
                var hosts = boxes.Where(b => CanHost(b, p, rel, potFallback)).ToList();
                if (hosts.Count == 0)
                {
                    dead.Add(p);
                    continue;
                }
                var key = (hosts.SelectMany(h => h.TypeIds).Distinct().Count(), hosts.Count, -p.SpaceScore);
                if (best is null || key.CompareTo(bestKey) < 0)
                {
                    best = p;
                    bestHosts = hosts;
                    bestKey = key;
                }
            }

            foreach (var d in dead)
            {
                // A plant of the open ground may still get a pot afterwards
                if (logFailure && rel.Trace is { } log && !(d.Place == PlantPlace.BedThenPot && !potFallback))
                    log.Add(new TraceEntry(TraceCode.CompanionNotPlaced, d.PlantId, Reasons: Reasons(boxes, b => HostReject(b, d, rel, potFallback))));
                unplaced.Add(d);
                remaining.Remove(d);
            }
            if (best is null) break;

            var host = bestHosts
                .OrderBy(h => !best.ColdFrame && h.HasCover ? 1 : 0)
                .ThenByDescending(h => HasPartner(h, best, rel) ? 1 : 0)
                .ThenByDescending(h => HasGood(h, best, rel) ? 1 : 0)
                .ThenByDescending(h => h.FreeFor(best.Slots))
                .ThenBy(h => h.Id ?? int.MaxValue)
                .First();
            Put(host, best, PlantingRole.Companion);
            rel.Trace?.Add(new TraceEntry(TraceCode.CompanionPlaced, best.PlantId, host.Kind, host.Name, host.IsNew, best.Slots));
            remaining.Remove(best);
        }

        return unplaced;
    }
}
