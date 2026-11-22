namespace SeedPlanner.Domain;

/// <summary>
/// Spreads the planned plants over the pots and beds the user chose. No database, only calculation.
/// <list type="number">
/// <item>Main plants: the plants are taken from large to small. In a pot stands one main plant (the smallest pot
/// that is large enough); in a bed as many as fit by area. This goes on as long as a pot or bed is free.</item>
/// <item>Under-planting: the plants that are left are put to the main plants. Pots and beds need enough space
/// left, the neighbours must not be "bad", and the plant must be lower than the main plants. The plants that fit
/// to the fewest plant types are placed first.</item>
/// <item>What is still left is handled by the strategy: reduce in proportion, list pots to buy, or add beds.</item>
/// </list>
/// </summary>
public static partial class PlanAllocator
{
    private const double Eps = 1e-9;

    public static AllocationResult Allocate(
        IReadOnlyList<AllocPlant> plants,
        IReadOnlyList<AllocContainerInput> containers,
        IReadOnlyCollection<PlantTypeCompanion> companions,
        ShortageStrategy strategy,
        double maxBedAreaM2 = 0,
        NoCoverStrategy noCover = NoCoverStrategy.NewCoveredBed,
        bool allowOthersInCoveredBeds = false,
        IEnumerable<(int Main, int Secondary)>? partners = null)
    {
        var rel = new Rules(companions, allowOthersInCoveredBeds, partners);
        var opt = new Opt(strategy, maxBedAreaM2, noCover);
        var unplaced = new List<UnplacedResult>();
        var head = new List<TraceEntry>();

        // A plant that cannot be placed even alone (missing data, no suitable place) is reported and left out
        var usable = new List<AllocPlant>();
        foreach (var p in plants.Where(p => p.Count > 0))
        {
            if (p.Slots == 0)
            {
                unplaced.Add(new UnplacedResult(p.PlantId, p.Count, UnplacedReason.MissingTimeData));
                head.Add(new TraceEntry(TraceCode.Excluded, p.PlantId, Count: p.Count, Detail: nameof(UnplacedReason.MissingTimeData)));
                continue;
            }
            var probe = Execute(new[] { p with { Count = 1 } }, containers, rel, opt);
            if (probe.Left.Count == 0) usable.Add(p);
            else
            {
                var reason = ReasonFor(p, strategy, maxBedAreaM2);
                // Would it fit if the soil pH did not count? Then the pH is the reason.
                if (reason is UnplacedReason.NoSuitableContainer or UnplacedReason.NotEnoughPots && (p.PhMin is not null || p.PhMax is not null) &&
                    Execute(new[] { p with { Count = 1, PhMin = null, PhMax = null } }, containers, rel, opt).Left.Count == 0)
                    reason = UnplacedReason.SoilMismatch;
                unplaced.Add(new UnplacedResult(p.PlantId, p.Count, reason));
                head.Add(new TraceEntry(TraceCode.Excluded, p.PlantId, Count: p.Count, Detail: reason.ToString()));
            }
        }

        var counts = usable.Select(p => p.Count).ToArray();
        List<AllocPlant> Build() => usable.Select((p, i) => p with { Count = counts[i] }).ToList();

        var trace = new TraceLog();
        var run = Execute(Build(), containers, rel, opt, trace);
        var reductions = new List<ReductionResult>();
        var scale = 1.0;

        if (strategy == ShortageStrategy.ShrinkProportionally)
        {
            // Take one plant at a time from the variety that still has the largest share of its wanted number
            // (the smaller plant first when equal), until everything fits
            while (run.Left.Count > 0)
            {
                var i = Enumerable.Range(0, usable.Count)
                    .Where(k => counts[k] > 0)
                    .OrderByDescending(k => (double)counts[k] / usable[k].Count)
                    .ThenBy(k => usable[k].SpaceScore)
                    .ThenByDescending(k => usable[k].Count)
                    .ThenBy(k => k)
                    .First();
                counts[i]--;
                trace = new TraceLog();
                run = Execute(Build(), containers, rel, opt, trace);
            }

            for (var k = 0; k < usable.Count; k++)
                if (counts[k] < usable[k].Count)
                    reductions.Add(new ReductionResult(usable[k].PlantId, usable[k].Count, counts[k]));

            var wanted = usable.Sum(p => p.Count);
            if (wanted > 0) scale = (double)counts.Sum() / wanted;
            if (counts.Sum() < wanted)
            {
                trace.Add(new TraceEntry(TraceCode.PhaseShortage));
                trace.Add(new TraceEntry(TraceCode.ShrinkApplied, Count: wanted - counts.Sum(), Value: scale));
            }
        }
        else
        {
            foreach (var g in run.Left.GroupBy(p => p.PlantId))
                unplaced.Add(new UnplacedResult(g.Key, g.Count(), ReasonFor(g.First(), strategy, maxBedAreaM2)));
        }

        var result = run.Boxes.Select(b => new ContainerResult(
            b.Kind, b.Id, b.Name, b.IsNew, b.Capacity, b.Used,
            b.Placements
                .GroupBy(x => (x.Plant.PlantId, x.Plant.PlantTypeId, x.Role, x.Plant.Slots))
                .Select(g => new PlacementResult(g.Key.PlantId, g.Key.PlantTypeId, g.Key.Role, g.Count(), g.Key.Slots,
                    // the sowing was moved later within its window
                    Moved: g.First().Plant.SlotOptions is { Count: > 1 } o && g.Key.Slots != o[0]))
                .ToList(),
            b.HasCover, b.NeedsCover, b.Category)).ToList();

        return new AllocationResult
        {
            Containers = result,
            Unplaced = unplaced,
            Reductions = reductions,
            ScaleFactor = scale,
            Trace = head.Concat(trace.Entries).ToList()
        };
    }


    // One run with the given numbers: the boxes with their placements and the single plants that did not fit
    private static (List<Box> Boxes, List<AllocPlant> Left) Execute(
        IReadOnlyList<AllocPlant> plants, IReadOnlyList<AllocContainerInput> inputs, Rules rel, Opt opt, TraceLog? trace = null)
    {
        rel.Trace = trace;
        try
        {
            return ExecuteTraced(plants, inputs, rel, opt);
        }
        finally
        {
            rel.Trace = null;
        }
    }

    private static (List<Box> Boxes, List<AllocPlant> Left) ExecuteTraced(
        IReadOnlyList<AllocPlant> plants, IReadOnlyList<AllocContainerInput> inputs, Rules rel, Opt opt)
    {
        var boxes = inputs.Select(ToBox).ToList();
        var units = plants.SelectMany(p => Enumerable.Repeat(p with { Count = 1 }, Math.Max(0, p.Count))).ToList();
        var retrofit = EnsureCover(units, boxes, rel, opt);

        if (rel.Trace is { } log)
        {
            var position = 0;
            foreach (var u in Order(units).DistinctBy(u => u.PlantId))
                log.Add(new TraceEntry(u.ColdFrame ? TraceCode.PlantOrderColdFrame : TraceCode.PlantOrder, u.PlantId, Count: ++position));
        }

        var left = PlaceAll(units, boxes, rel);
        if (left.Count > 0 && opt.Strategy != ShortageStrategy.ShrinkProportionally)
            rel.Trace?.Add(new TraceEntry(TraceCode.PhaseShortage));
        if (opt.Strategy == ShortageStrategy.BuyPots) left = BuyPots(left, boxes, rel);
        else if (opt.Strategy == ShortageStrategy.AddBeds) left = AddBeds(left, boxes, rel, opt.MaxBed);

        // Plants that must go into one kind of container get new ones, also if the strategy would not add any
        left = AddForRequired(left, boxes, rel, opt);

        // A bed that was meant to get a cover but holds no cold frame plant after all stays as it was
        if (retrofit is not null && retrofit.ColdCount == 0)
        {
            retrofit.HasCover = false;
            retrofit.NeedsCover = false;
        }
        boxes.RemoveAll(b => b.IsNew && b.Kind == ContainerKind.Bed && b.Placements.Count == 0);
        return (boxes, left);
    }

    private static List<AllocPlant> AddForRequired(List<AllocPlant> left, List<Box> boxes, Rules rel, Opt opt)
    {
        if (!left.Any(p => p.Required)) return left;

        if (opt.Strategy != ShortageStrategy.BuyPots)
        {
            var pots = left.Where(p => p.Required && p.Place == PlantPlace.Pot).ToList();
            if (pots.Count > 0)
                left = left.Where(p => !(p.Required && p.Place == PlantPlace.Pot)).Concat(BuyPots(pots, boxes, rel)).ToList();
        }
        if (opt.Strategy != ShortageStrategy.AddBeds)
        {
            var beds = left.Where(p => p.Required && p.Place == PlantPlace.Bed).ToList();
            if (beds.Count > 0)
                left = left.Where(p => !(p.Required && p.Place == PlantPlace.Bed)).Concat(AddBeds(beds, boxes, rel, opt.MaxBed)).ToList();
        }
        return left;
    }

    // Cold frame plants are planned but no chosen bed has a cover: retrofit one bed or add new covered beds.
    // Returns the retrofitted bed, if there is one.
    private static Box? EnsureCover(List<AllocPlant> units, List<Box> boxes, Rules rel, Opt opt)
    {
        var cold = units.Where(u => u.ColdFrame && u.AreaM2 is not null).ToList();
        if (cold.Count == 0 || boxes.Any(b => b.Kind == ContainerKind.Bed && b.HasCover)) return null;

        var total = cold.Sum(u => u.AreaM2!.Value);
        var beds = boxes.Where(b => b.Kind == ContainerKind.Bed).ToList();
        // Only beds wide enough for the widest row spacing of the cold frame plants, if there are any
        var widest = cold.Max(u => u.RowSpacingCm ?? 0);
        var wideEnough = beds.Where(b => b.WidthCm is null || b.WidthCm.Value + Eps >= widest).ToList();
        if (wideEnough.Count > 0) beds = wideEnough;

        if (opt.NoCover == NoCoverStrategy.RetrofitExistingBed && beds.Count > 0)
        {
            // The smallest bed that takes all cold frame plants, otherwise the largest one
            var fitting = beds.Where(b => b.Free + Eps >= total).OrderBy(b => b.Free).ThenBy(b => b.Id ?? int.MaxValue).ToList();
            var bed = fitting.Count > 0
                ? fitting[0]
                : beds.OrderByDescending(b => b.Free).ThenBy(b => b.Id ?? int.MaxValue).First();
            bed.HasCover = true;
            bed.NeedsCover = true;
            rel.Trace?.Add(new TraceEntry(TraceCode.CoverRetrofit, Kind: bed.Kind, Container: bed.Name, Value: total));
            return bed;
        }

        // New covered beds (also when there is no bed to retrofit)
        var count = opt.MaxBed > 0 ? (int)Math.Ceiling(total / opt.MaxBed - Eps) : 1;
        var capacity = opt.MaxBed > 0 ? opt.MaxBed : total;
        for (var n = 0; n < Math.Max(1, count); n++)
            boxes.Add(new Box { Kind = ContainerKind.Bed, IsNew = true, Category = ContainerCategory.Bed, HasCover = true, Capacity = capacity });
        rel.Trace?.Add(new TraceEntry(TraceCode.NewCoveredBeds, Count: Math.Max(1, count), Value: capacity));
        return null;
    }
}
