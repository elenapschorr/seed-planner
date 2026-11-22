namespace SeedPlanner.Domain;

// What happens when there is not enough room: list pots to buy, add beds, and the reason for a plant that stays unplaced.
public static partial class PlanAllocator
{
    // New pots: the largest plant that is left gets a new pot (size from its pot range), the others are tried as under-planting
    private static List<AllocPlant> BuyPots(List<AllocPlant> left, List<Box> boxes, Rules rel)
    {
        while (true)
        {
            var seed = Order(left).FirstOrDefault(p => !p.ColdFrame && Allowed(p, ContainerKind.Pot, potFallback: true) && p.PotLitersMin is not null);
            if (seed is null) return left;

            // A pot bought before may be free again at the time this plant needs it
            if (TryPlaceMain(seed, boxes, rel, potFallback: true))
            {
                left.Remove(seed);
                left = PlaceCompanions(left, boxes, rel, potFallback: true);
                continue;
            }

            var pot = new Box
            {
                Kind = ContainerKind.Pot,
                IsNew = true,
                // New pots are of the first kind of pot the user allowed for the plant
                Category = seed.Order?.FirstOrDefault(c => c != ContainerCategory.Bed, ContainerCategory.Pot) ?? ContainerCategory.Pot,
                Capacity = Math.Max(seed.PotLitersMax ?? 0, seed.PotLitersMin!.Value)
            };
            boxes.Add(pot);
            rel.Trace?.Add(new TraceEntry(TraceCode.NewPot, seed.PlantId, ContainerKind.Pot, IsNew: true, Value: pot.Capacity));
            Put(pot, seed, PlantingRole.Main);
            rel.Trace?.Add(new TraceEntry(TraceCode.MainPlaced, seed.PlantId, ContainerKind.Pot, IsNew: true, Slots: seed.Slots));
            left.Remove(seed);
            left = PlaceCompanions(left, boxes, rel, potFallback: true);
        }
    }

    // New beds of at most maxBed m² until nothing more can be placed. As long as cold frame plants are left,
    // the new bed gets a cover; if that places nothing, the next try is a bed without a cover.
    private static List<AllocPlant> AddBeds(List<AllocPlant> left, List<Box> boxes, Rules rel, double maxBed)
    {
        var tryCover = left.Any(p => p.ColdFrame);
        while (left.Count > 0 && maxBed > 0)
        {
            var bed = new Box { Kind = ContainerKind.Bed, IsNew = true, Category = ContainerCategory.Bed, HasCover = tryCover, Capacity = maxBed };
            boxes.Add(bed);
            var mark = rel.Trace?.Entries.Count ?? 0;
            rel.Trace?.Add(new TraceEntry(tryCover ? TraceCode.NewBedCovered : TraceCode.NewBed, Kind: ContainerKind.Bed, IsNew: true, Value: maxBed));
            var next = PlaceAll(left, boxes, rel, top: false);
            if (next.Count == left.Count)
            {
                // nothing fitted into the new bed: take the notes of this try back
                if (rel.Trace is { } log) log.Entries.RemoveRange(mark, log.Entries.Count - mark);
                boxes.Remove(bed);
                if (!tryCover) break;
                tryCover = false;
                continue;
            }
            left = next;
            tryCover = left.Any(p => p.ColdFrame);
        }
        return left;
    }

    private static UnplacedReason ReasonFor(AllocPlant p, ShortageStrategy strategy, double maxBed)
    {
        var potPossible = !p.ColdFrame && p.Place != PlantPlace.Bed && p.PotLitersMin is not null;
        var bedPossible = (p.ColdFrame || p.Place != PlantPlace.Pot) && p.AreaM2 is not null;
        if (p.Slots == 0) return UnplacedReason.MissingTimeData;
        if (!potPossible && !bedPossible) return UnplacedReason.MissingSpaceData;
        // A pot plant is never put into a bed: the pots are not enough
        if (p.Place == PlantPlace.Pot) return UnplacedReason.NotEnoughPots;
        if ((strategy == ShortageStrategy.AddBeds || p.ColdFrame || p.Required) && !potPossible && p.AreaM2 > maxBed) return UnplacedReason.TooBigForBed;
        return UnplacedReason.NoSuitableContainer;
    }
}
