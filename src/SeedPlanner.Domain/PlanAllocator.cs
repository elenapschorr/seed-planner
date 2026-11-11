namespace SeedPlanner.Domain;

public enum ContainerKind { Pot, Bed }

public enum PlantingRole { Main, Companion }

/// <summary>What happens when the chosen pots and beds are not enough. One choice per plan.</summary>
public enum ShortageStrategy
{
    /// <summary>All varieties are reduced in proportion until everything fits.</summary>
    ShrinkProportionally,

    /// <summary>The missing pots are listed (sizes come from the plants that need them).</summary>
    BuyPots,

    /// <summary>New beds are added, each at most as large as the maximum bed area.</summary>
    AddBeds
}

public enum UnplacedReason
{
    /// <summary>No pot size (pots) or no spacing (beds) is known for the variety.</summary>
    MissingSpaceData,

    /// <summary>No chosen pot or bed fits, or the neighbours do not allow it.</summary>
    NoSuitableContainer,

    /// <summary>One plant needs more area than the maximum bed area.</summary>
    TooBigForBed
}

/// <summary>Where a variety may be placed.</summary>
public enum PlantPlace { Pot, Bed, Either }

/// <summary>What happens when plants for the cold frame are planned but no chosen bed has a cover.</summary>
public enum NoCoverStrategy
{
    /// <summary>One of the chosen beds gets a cover (the result says which one).</summary>
    RetrofitExistingBed,

    /// <summary>A completely new bed with a cover is created (at most the maximum bed area each).</summary>
    NewCoveredBed
}

/// <summary>
/// A variety with the number of plants to place. A pot needs <see cref="PotLitersMin"/> litres per plant,
/// a bed <see cref="AreaM2"/> square meters per plant (from the spacing, see <see cref="AreaFromSpacing"/>).
/// Cold frame plants (<see cref="ColdFrame"/>) only go into beds with a cover.
/// </summary>
public record AllocPlant(
    int PlantId, int PlantTypeId, string Name, int Count, PlantPlace Place,
    double? PotLitersMin, double? PotLitersMax, double? AreaM2, double? HeightCm,
    bool ColdFrame = false)
{
    /// <summary>
    /// How "large" a plant is, for the order of placing: litres of pot, otherwise 100 x square meters.
    /// A rough measure that only has to put the big plants before the small ones.
    /// </summary>
    public double SpaceScore => PotLitersMin ?? (AreaM2 is null ? 0 : AreaM2.Value * 100);

    /// <summary>Area per plant in m² from row and plant spacing; if only one is known, a square grid is assumed.</summary>
    public static double? AreaFromSpacing(int? rowCm, int? plantCm)
    {
        var plant = plantCm is > 0 ? plantCm : rowCm;
        var row = rowCm is > 0 ? rowCm : plantCm;
        if (plant is not > 0 || row is not > 0) return null;
        return (double)plant.Value * row.Value / 10_000;
    }
}

/// <summary>
/// A pot (capacity in litres) or a bed (capacity in m²) the user chose for the plan.
/// Something that already stands in it is given as <see cref="Used"/>, <see cref="HasMain"/>,
/// <see cref="MaxHeightCm"/> and <see cref="ExistingTypeIds"/>.
/// </summary>
public record AllocContainerInput(
    ContainerKind Kind, int Id, string Name, double Capacity,
    double Used = 0, bool HasMain = false, double? MaxHeightCm = null,
    IReadOnlyCollection<int>? ExistingTypeIds = null, bool HasCover = false);

public record PlacementResult(int PlantId, int PlantTypeId, PlantingRole Role, int Count);

/// <summary>A chosen container or a new one (<see cref="IsNew"/>, no id) with what was placed in it.
/// NeedsCover: an existing bed that has to get a cover (strategy RetrofitExistingBed).</summary>
public record ContainerResult(
    ContainerKind Kind, int? Id, string Name, bool IsNew, double Capacity, double Used,
    IReadOnlyList<PlacementResult> Placements, bool HasCover = false, bool NeedsCover = false);

public record UnplacedResult(int PlantId, int Count, UnplacedReason Reason);

/// <summary>A variety that got fewer plants than wanted (strategy ShrinkProportionally).</summary>
public record ReductionResult(int PlantId, int Wanted, int Placed);

public record NewPotGroup(double Liters, int Count);

public class AllocationResult
{
    public IReadOnlyList<ContainerResult> Containers { get; init; } = Array.Empty<ContainerResult>();
    public IReadOnlyList<UnplacedResult> Unplaced { get; init; } = Array.Empty<UnplacedResult>();
    public IReadOnlyList<ReductionResult> Reductions { get; init; } = Array.Empty<ReductionResult>();

    /// <summary>Placed plants divided by wanted plants; 1 if nothing had to be reduced.</summary>
    public double ScaleFactor { get; init; } = 1;

    /// <summary>The pots that still have to be bought, grouped by size in litres.</summary>
    public IReadOnlyList<NewPotGroup> NewPots => Containers
        .Where(c => c.IsNew && c.Kind == ContainerKind.Pot)
        .GroupBy(c => Math.Round(c.Capacity, 1))
        .OrderByDescending(g => g.Key)
        .Select(g => new NewPotGroup(g.Key, g.Count()))
        .ToList();

    /// <summary>The beds that have to be created; Used is the area needed.</summary>
    public IReadOnlyList<ContainerResult> NewBeds => Containers
        .Where(c => c.IsNew && c.Kind == ContainerKind.Bed)
        .ToList();
}

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
public static class PlanAllocator
{
    private const double Eps = 1e-9;

    public static AllocationResult Allocate(
        IReadOnlyList<AllocPlant> plants,
        IReadOnlyList<AllocContainerInput> containers,
        IReadOnlyCollection<PlantTypeCompanion> companions,
        ShortageStrategy strategy,
        double maxBedAreaM2 = 0,
        NoCoverStrategy noCover = NoCoverStrategy.NewCoveredBed,
        bool allowOthersInCoveredBeds = false)
    {
        var rel = new Rules(companions, allowOthersInCoveredBeds);
        var opt = new Opt(strategy, maxBedAreaM2, noCover);
        var unplaced = new List<UnplacedResult>();

        // A plant that cannot be placed even alone (missing data, no suitable place) is reported and left out
        var usable = new List<AllocPlant>();
        foreach (var p in plants.Where(p => p.Count > 0))
        {
            var probe = Execute(new[] { p with { Count = 1 } }, containers, rel, opt);
            if (probe.Left.Count == 0) usable.Add(p);
            else unplaced.Add(new UnplacedResult(p.PlantId, p.Count, ReasonFor(p, strategy, maxBedAreaM2)));
        }

        var counts = usable.Select(p => p.Count).ToArray();
        List<AllocPlant> Build() => usable.Select((p, i) => p with { Count = counts[i] }).ToList();

        var run = Execute(Build(), containers, rel, opt);
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
                run = Execute(Build(), containers, rel, opt);
            }

            for (var k = 0; k < usable.Count; k++)
                if (counts[k] < usable[k].Count)
                    reductions.Add(new ReductionResult(usable[k].PlantId, usable[k].Count, counts[k]));

            var wanted = usable.Sum(p => p.Count);
            if (wanted > 0) scale = (double)counts.Sum() / wanted;
        }
        else
        {
            foreach (var g in run.Left.GroupBy(p => p.PlantId))
                unplaced.Add(new UnplacedResult(g.Key, g.Count(), ReasonFor(g.First(), strategy, maxBedAreaM2)));
        }

        var result = run.Boxes.Select(b => new ContainerResult(
            b.Kind, b.Id, b.Name, b.IsNew, b.Capacity, b.Used,
            b.Placements
                .GroupBy(x => (x.Plant.PlantId, x.Plant.PlantTypeId, x.Role))
                .Select(g => new PlacementResult(g.Key.PlantId, g.Key.PlantTypeId, g.Key.Role, g.Count()))
                .ToList(),
            b.HasCover, b.NeedsCover)).ToList();

        return new AllocationResult
        {
            Containers = result,
            Unplaced = unplaced,
            Reductions = reductions,
            ScaleFactor = scale
        };
    }

    // ---------------------------------------------------------------- internals

    private sealed class Box
    {
        public ContainerKind Kind;
        public int? Id;
        public string Name = "";
        public bool IsNew;
        public double Capacity;
        public double Used;
        public bool HasMain;

        /// <summary>A bed with a cover (cold frame, hood).</summary>
        public bool HasCover;

        /// <summary>An existing bed that gets its cover only through this plan.</summary>
        public bool NeedsCover;

        /// <summary>Number of cold frame plants placed here.</summary>
        public int ColdCount;
        public double? MaxHeightCm;
        public HashSet<int> TypeIds = new();
        public List<(AllocPlant Plant, PlantingRole Role)> Placements = new();
        public double Free => Capacity - Used;
    }

    // Rules of one run: how plant types get along, and whether other plants may use covered beds
    private sealed class Rules
    {
        private readonly Dictionary<(int, int), CompanionRelation> map = new();

        public bool AllowOthersInCovered { get; }

        public Rules(IEnumerable<PlantTypeCompanion> pairs, bool allowOthersInCovered)
        {
            AllowOthersInCovered = allowOthersInCovered;
            foreach (var p in pairs)
                map[(Math.Min(p.PlantTypeId, p.OtherPlantTypeId), Math.Max(p.PlantTypeId, p.OtherPlantTypeId))] = p.Relation;
        }

        public CompanionRelation? Between(int a, int b)
        {
            if (a == b) return null;
            return map.TryGetValue((Math.Min(a, b), Math.Max(a, b)), out var r) ? (CompanionRelation?)r : null;
        }
    }

    private sealed record Opt(ShortageStrategy Strategy, double MaxBed, NoCoverStrategy NoCover);

    private static Box ToBox(AllocContainerInput i) => new()
    {
        Kind = i.Kind,
        Id = i.Id,
        Name = i.Name,
        Capacity = i.Capacity,
        Used = i.Used,
        HasMain = i.HasMain,
        HasCover = i.HasCover,
        MaxHeightCm = i.MaxHeightCm,
        TypeIds = new HashSet<int>(i.ExistingTypeIds ?? Array.Empty<int>())
    };

    // One run with the given numbers: the boxes with their placements and the single plants that did not fit
    private static (List<Box> Boxes, List<AllocPlant> Left) Execute(
        IReadOnlyList<AllocPlant> plants, IReadOnlyList<AllocContainerInput> inputs, Rules rel, Opt opt)
    {
        var boxes = inputs.Select(ToBox).ToList();
        var units = plants.SelectMany(p => Enumerable.Repeat(p with { Count = 1 }, Math.Max(0, p.Count))).ToList();
        var retrofit = EnsureCover(units, boxes, opt);

        var left = PlaceAll(units, boxes, rel);
        if (opt.Strategy == ShortageStrategy.BuyPots) left = BuyPots(left, boxes, rel);
        else if (opt.Strategy == ShortageStrategy.AddBeds) left = AddBeds(left, boxes, rel, opt.MaxBed);

        // A bed that was meant to get a cover but holds no cold frame plant after all stays as it was
        if (retrofit is not null && retrofit.ColdCount == 0)
        {
            retrofit.HasCover = false;
            retrofit.NeedsCover = false;
        }
        boxes.RemoveAll(b => b.IsNew && b.Kind == ContainerKind.Bed && b.Placements.Count == 0);
        return (boxes, left);
    }

    // Cold frame plants are planned but no chosen bed has a cover: retrofit one bed or add new covered beds.
    // Returns the retrofitted bed, if there is one.
    private static Box? EnsureCover(List<AllocPlant> units, List<Box> boxes, Opt opt)
    {
        var cold = units.Where(u => u.ColdFrame && u.AreaM2 is not null).ToList();
        if (cold.Count == 0 || boxes.Any(b => b.Kind == ContainerKind.Bed && b.HasCover)) return null;

        var total = cold.Sum(u => u.AreaM2!.Value);
        var beds = boxes.Where(b => b.Kind == ContainerKind.Bed).ToList();

        if (opt.NoCover == NoCoverStrategy.RetrofitExistingBed && beds.Count > 0)
        {
            // The smallest bed that takes all cold frame plants, otherwise the largest one
            var fitting = beds.Where(b => b.Free + Eps >= total).OrderBy(b => b.Free).ThenBy(b => b.Id ?? int.MaxValue).ToList();
            var bed = fitting.Count > 0
                ? fitting[0]
                : beds.OrderByDescending(b => b.Free).ThenBy(b => b.Id ?? int.MaxValue).First();
            bed.HasCover = true;
            bed.NeedsCover = true;
            return bed;
        }

        // New covered beds (also when there is no bed to retrofit)
        var count = opt.MaxBed > 0 ? (int)Math.Ceiling(total / opt.MaxBed - Eps) : 1;
        var capacity = opt.MaxBed > 0 ? opt.MaxBed : total;
        for (var n = 0; n < Math.Max(1, count); n++)
            boxes.Add(new Box { Kind = ContainerKind.Bed, IsNew = true, HasCover = true, Capacity = capacity });
        return null;
    }

    // Cold frame plants first (they only use covered beds), then from large to small
    private static IEnumerable<AllocPlant> Order(IEnumerable<AllocPlant> units) => units
        .OrderByDescending(p => p.ColdFrame)
        .ThenByDescending(p => p.SpaceScore)
        .ThenByDescending(p => p.HeightCm ?? 0)
        .ThenBy(p => p.PlantId);

    private static bool Allowed(AllocPlant p, ContainerKind kind) =>
        p.Place == PlantPlace.Either
        || (kind == ContainerKind.Pot ? p.Place == PlantPlace.Pot : p.Place == PlantPlace.Bed);

    // Cold frame plants only go into covered beds. Other plants stay out of covered beds unless the user allowed it.
    private static bool Fits(AllocPlant p, Box b, Rules rel)
    {
        if (p.ColdFrame) return b.Kind == ContainerKind.Bed && b.HasCover;
        return Allowed(p, b.Kind) && (!b.HasCover || rel.AllowOthersInCovered);
    }

    private static double? Need(AllocPlant p, ContainerKind kind) =>
        kind == ContainerKind.Pot ? p.PotLitersMin : p.AreaM2;

    private static bool NeighboursOk(Box c, AllocPlant p, Rules rel) =>
        c.TypeIds.All(t => rel.Between(t, p.PlantTypeId) != CompanionRelation.Bad);

    private static bool HasGood(Box c, AllocPlant p, Rules rel) =>
        c.TypeIds.Any(t => rel.Between(t, p.PlantTypeId) == CompanionRelation.Good);

    private static void Put(Box c, AllocPlant p, PlantingRole role)
    {
        c.Used += Need(p, c.Kind) ?? 0;
        c.TypeIds.Add(p.PlantTypeId);
        c.Placements.Add((p, role));
        if (p.ColdFrame) c.ColdCount++;
        if (role != PlantingRole.Main) return;
        c.HasMain = true;
        if (p.HeightCm is not null) c.MaxHeightCm = Math.Max(c.MaxHeightCm ?? 0, p.HeightCm.Value);
    }

    private static List<AllocPlant> PlaceAll(IEnumerable<AllocPlant> units, List<Box> boxes, Rules rel)
    {
        var left = new List<AllocPlant>();
        foreach (var u in Order(units))
            if (!TryPlaceMain(u, boxes, rel)) left.Add(u);
        return PlaceCompanions(left, boxes, rel);
    }

    // A pot gets one main plant (the smallest pot that is large enough); a bed as many as fit by area
    private static bool TryPlaceMain(AllocPlant p, List<Box> boxes, Rules rel)
    {
        if (!p.ColdFrame && p.PotLitersMin is { } liters)
        {
            var pot = boxes
                .Where(b => b.Kind == ContainerKind.Pot && Fits(p, b, rel) && !b.HasMain && b.Free + Eps >= liters)
                .OrderBy(b => b.Capacity)
                .ThenBy(b => b.Id ?? int.MaxValue)
                .FirstOrDefault();
            if (pot is not null)
            {
                Put(pot, p, PlantingRole.Main);
                return true;
            }
        }

        if (p.AreaM2 is { } area)
        {
            // Cold frame plants: stay in a bed that already has some, otherwise the bed with the most free area.
            // Other plants: uncovered beds first, then beds with good neighbours, then the tightest fit.
            var bed = boxes
                .Where(b => b.Kind == ContainerKind.Bed && Fits(p, b, rel) && b.Free + Eps >= area && NeighboursOk(b, p, rel))
                .OrderBy(b => p.ColdFrame ? (b.ColdCount > 0 ? 0 : 1) : (b.HasCover ? 1 : 0))
                .ThenBy(b => p.ColdFrame ? 0 : (HasGood(b, p, rel) ? 0 : 1))
                .ThenBy(b => p.ColdFrame ? -b.Free : b.Free)
                .ThenBy(b => b.Id ?? int.MaxValue)
                .FirstOrDefault();
            if (bed is not null)
            {
                Put(bed, p, PlantingRole.Main);
                return true;
            }
        }

        return false;
    }

    private static bool CanHost(Box c, AllocPlant p, Rules rel)
    {
        if (!c.HasMain || !Fits(p, c, rel)) return false;
        if (Need(p, c.Kind) is not { } need) return false;
        if (c.Free + Eps < need) return false;
        if (!NeighboursOk(c, p, rel)) return false;
        if (c.MaxHeightCm is { } top && p.HeightCm is { } height && height >= top) return false;
        return true;
    }

    // Under-planting. Every round the plant with the fewest possible host plant types goes first
    // (then the one with the fewest possible containers, then the larger one); the numbers are recalculated
    // after each placement. Returns the plants that did not fit.
    private static List<AllocPlant> PlaceCompanions(List<AllocPlant> left, List<Box> boxes, Rules rel)
    {
        var remaining = new List<AllocPlant>(left);
        var unplaced = new List<AllocPlant>();

        while (remaining.Count > 0)
        {
            AllocPlant? best = null;
            List<Box> bestHosts = new();
            (int, int, double) bestKey = default;
            var dead = new List<AllocPlant>();

            foreach (var p in remaining)
            {
                var hosts = boxes.Where(b => CanHost(b, p, rel)).ToList();
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
                unplaced.Add(d);
                remaining.Remove(d);
            }
            if (best is null) break;

            var host = bestHosts
                .OrderBy(h => !best.ColdFrame && h.HasCover ? 1 : 0)
                .ThenByDescending(h => HasGood(h, best, rel) ? 1 : 0)
                .ThenByDescending(h => h.Free)
                .ThenBy(h => h.Id ?? int.MaxValue)
                .First();
            Put(host, best, PlantingRole.Companion);
            remaining.Remove(best);
        }

        return unplaced;
    }

    // New pots: the largest plant that is left gets a new pot (size from its pot range), the others are tried as under-planting
    private static List<AllocPlant> BuyPots(List<AllocPlant> left, List<Box> boxes, Rules rel)
    {
        while (true)
        {
            var seed = Order(left).FirstOrDefault(p => !p.ColdFrame && Allowed(p, ContainerKind.Pot) && p.PotLitersMin is not null);
            if (seed is null) return left;

            var pot = new Box
            {
                Kind = ContainerKind.Pot,
                IsNew = true,
                Capacity = Math.Max(seed.PotLitersMax ?? 0, seed.PotLitersMin!.Value)
            };
            boxes.Add(pot);
            Put(pot, seed, PlantingRole.Main);
            left.Remove(seed);
            left = PlaceCompanions(left, boxes, rel);
        }
    }

    // New beds of at most maxBed m² until nothing more can be placed. As long as cold frame plants are left,
    // the new bed gets a cover; if that places nothing, the next try is a bed without a cover.
    private static List<AllocPlant> AddBeds(List<AllocPlant> left, List<Box> boxes, Rules rel, double maxBed)
    {
        var tryCover = left.Any(p => p.ColdFrame);
        while (left.Count > 0 && maxBed > 0)
        {
            var bed = new Box { Kind = ContainerKind.Bed, IsNew = true, HasCover = tryCover, Capacity = maxBed };
            boxes.Add(bed);
            var next = PlaceAll(left, boxes, rel);
            if (next.Count == left.Count)
            {
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
        if (!potPossible && !bedPossible) return UnplacedReason.MissingSpaceData;
        if ((strategy == ShortageStrategy.AddBeds || p.ColdFrame) && !potPossible && p.AreaM2 > maxBed) return UnplacedReason.TooBigForBed;
        return UnplacedReason.NoSuitableContainer;
    }
}
