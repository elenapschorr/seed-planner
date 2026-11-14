using System.Text.Json.Serialization;

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
    TooBigForBed,

    /// <summary>The variety grows in pots only (growing place "pot") and the chosen pots are not enough.
    /// It is not put anywhere else.</summary>
    NotEnoughPots,

    /// <summary>The times (sowing or planting out, harvest) of the variety are missing, so it cannot be put anywhere.</summary>
    MissingTimeData,

    /// <summary>Containers would fit, but their soil pH is outside the range of the variety.</summary>
    SoilMismatch
}

/// <summary>
/// Where a variety may be placed. Pot: only pots (never a bed). Bed: only beds. BedThenPot (open ground): first beds,
/// then the pots that are still free after the pot plants are placed. Either: no preference (pots first).
/// </summary>
public enum PlantPlace { Pot, Bed, Either, BedThenPot }

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
/// <see cref="RowSpacingCm"/> is the distance between rows; a bed narrower than that does not take the plant.
/// </summary>
public record AllocPlant(
    int PlantId, int PlantTypeId, string Name, int Count, PlantPlace Place,
    double? PotLitersMin, double? PotLitersMax, double? AreaM2, double? HeightCm,
    bool ColdFrame = false, double? RowSpacingCm = null, ulong Slots = OccupancyMask.AllYear,
    decimal? PhMin = null, decimal? PhMax = null, SoilType? SoilType = null, PlantCategory? Category = null,
    bool Required = false, IReadOnlyList<ContainerCategory>? Order = null,
    IReadOnlyList<ulong>? SlotOptions = null, PlantRole? Role = null)
{
    // Role: main plant or secondary plant of the plant type (suggestion or the user's own choice); null = not known.
    // A secondary plant is only placed with a main plant (under-planting); if there is none, it stands alone.
    // A main plant always gets a place of its own; it is never put under another plant. Unknown: both ways, as before.
    // SlotOptions: alternative times (see PlantOccupancy.SlotOptions), earliest first; Slots is the first one.
    // If the main plant does not fit with Slots, the later ones are tried, so the sowing is moved within its window.

    // Order: the kinds of container the user allows, first choice first (null = every kind).
    // Required: the user allowed only one kind of container (bed or pot) for the plant. It is never reduced and never
    // left out for lack of room: new pots or beds are added for it, whatever the strategy is.

    /// <summary>A tree (fruit tree). Vegetables and berries are never planted together with a tree.</summary>
    public bool IsTree => Category == PlantCategory.FruitTree;

    /// <summary>A vegetable or berry: everything except herbs, flowers and trees (unknown category does not count).
    /// Only herbs and flowers may stand under a tree.</summary>
    public bool IsVegetable => Category is not null
        and not (PlantCategory.Herb or PlantCategory.Flower or PlantCategory.FruitTree);

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
/// <see cref="MaxHeightCm"/> and <see cref="ExistingTypeIds"/>. <see cref="WidthCm"/> is the narrow side of a bed
/// (unknown = no limit).
/// </summary>
public record AllocContainerInput(
    ContainerKind Kind, int Id, string Name, double Capacity,
    double Used = 0, bool HasMain = false, double? MaxHeightCm = null,
    IReadOnlyCollection<int>? ExistingTypeIds = null, bool HasCover = false, double? WidthCm = null,
    decimal? SoilPh = null, SoilType? SoilType = null, bool HasTree = false, bool HasVegetable = false,
    ContainerCategory? Category = null);

/// <summary><see cref="Slots"/>: the time the plants take the place (thirds of months), all year if not limited.
/// <see cref="Moved"/>: the sowing was moved later within the sowing window to find a place.</summary>
public record PlacementResult(int PlantId, int PlantTypeId, PlantingRole Role, int Count, ulong Slots = OccupancyMask.AllYear,
    bool Moved = false);

/// <summary>A chosen container or a new one (<see cref="IsNew"/>, no id) with what was placed in it.
/// NeedsCover: an existing bed that has to get a cover (strategy RetrofitExistingBed).</summary>
public record ContainerResult(
    ContainerKind Kind, int? Id, string Name, bool IsNew, double Capacity, double Used,
    IReadOnlyList<PlacementResult> Placements, bool HasCover = false, bool NeedsCover = false,
    ContainerCategory? Category = null);

public record UnplacedResult(int PlantId, int Count, UnplacedReason Reason);

/// <summary>A variety that got fewer plants than wanted (strategy ShrinkProportionally).</summary>
public record ReductionResult(int PlantId, int Wanted, int Placed);

public record NewPotGroup(double Liters, int Count);

public class AllocationResult
{
    public IReadOnlyList<ContainerResult> Containers { get; init; } = Array.Empty<ContainerResult>();
    public IReadOnlyList<UnplacedResult> Unplaced { get; init; } = Array.Empty<UnplacedResult>();
    public IReadOnlyList<ReductionResult> Reductions { get; init; } = Array.Empty<ReductionResult>();

    /// <summary>How the distribution came about, step by step.</summary>
    public IReadOnlyList<TraceEntry> Trace { get; init; } = Array.Empty<TraceEntry>();

    /// <summary>Placed plants divided by wanted plants; 1 if nothing had to be reduced.</summary>
    public double ScaleFactor { get; init; } = 1;

    /// <summary>The pots that still have to be bought, grouped by size in litres.</summary>
    [JsonIgnore]
    public IReadOnlyList<NewPotGroup> NewPots => Containers
        .Where(c => c.IsNew && c.Kind == ContainerKind.Pot)
        .GroupBy(c => Math.Round(c.Capacity, 1))
        .OrderByDescending(g => g.Key)
        .Select(g => new NewPotGroup(g.Key, g.Count()))
        .ToList();

    /// <summary>The beds that have to be created; Used is the area needed.</summary>
    [JsonIgnore]
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

    // ---------------------------------------------------------------- internals

    private sealed class Box
    {
        public ContainerKind Kind;
        public int? Id;
        public string Name = "";
        public bool IsNew;
        public double Capacity;

        /// <summary>Area or litres in use in each third of the year.</summary>
        public double[] UsedAt = new double[OccupancyMask.Count];

        /// <summary>Peak use over the year.</summary>
        public double Used => UsedAt.Max();

        /// <summary>The thirds of the year in which a main plant stands here.</summary>
        public ulong MainMask;

        /// <summary>Main plants placed in this run (height and time), for the under-planting rule.</summary>
        public List<(ulong Slots, double Height)> MainHeights = new();

        /// <summary>Plant types here with the time they stay (what already stands there: all year).</summary>
        public List<(int TypeId, ulong Slots)> TypeTimes = new();

        /// <summary>A bed with a cover (cold frame, hood).</summary>
        public bool HasCover;

        /// <summary>An existing bed that gets its cover only through this plan.</summary>
        public bool NeedsCover;

        /// <summary>Number of cold frame plants placed here.</summary>
        public int ColdCount;
        public double? MaxHeightCm;
        public double? WidthCm;
        public decimal? SoilPh;
        public SoilType? SoilType;

        /// <summary>Bed, pot, bowl or grow bag.</summary>
        public ContainerCategory Category;

        /// <summary>A tree stands here / a vegetable stands here: the two never share a container.</summary>
        public bool HasTree;
        public bool HasVegetable;
        public HashSet<int> TypeIds = new();
        public List<(AllocPlant Plant, PlantingRole Role)> Placements = new();
        public double Free => Capacity - Used;

        /// <summary>Free space during all thirds of the year in <paramref name="slots"/>.</summary>
        public double FreeFor(ulong slots)
        {
            var peak = 0.0;
            for (var s = 0; s < OccupancyMask.Count; s++)
                if ((slots & (1UL << s)) != 0 && UsedAt[s] > peak) peak = UsedAt[s];
            return Capacity - peak;
        }

        /// <summary>Height of the highest main plant standing here at the same time.</summary>
        public double? TopAt(ulong slots)
        {
            double? top = MaxHeightCm;
            foreach (var (s, h) in MainHeights)
                if (OccupancyMask.Overlaps(s, slots)) top = Math.Max(top ?? 0, h);
            return top;
        }
    }

    // Rules of one run: how plant types get along, and whether other plants may use covered beds
    private sealed class Rules
    {
        private readonly Dictionary<(int, int), CompanionRelation> map = new();

        public bool AllowOthersInCovered { get; }

        /// <summary>Where the run writes down what it does; null for the test runs.</summary>
        public TraceLog? Trace;

        private readonly HashSet<(int, int)> partners = new();

        /// <summary>The secondary plant type is a partner of the main plant type (the user's list).</summary>
        public bool IsPartner(int mainType, int secondaryType) => partners.Contains((mainType, secondaryType));

        public Rules(IEnumerable<PlantTypeCompanion> pairs, bool allowOthersInCovered, IEnumerable<(int Main, int Secondary)>? partnerPairs = null)
        {
            AllowOthersInCovered = allowOthersInCovered;
            foreach (var (main, secondary) in partnerPairs ?? Array.Empty<(int, int)>()) partners.Add((main, secondary));
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
        MainMask = i.HasMain ? OccupancyMask.AllYear : 0,
        UsedAt = Enumerable.Repeat(i.Used, OccupancyMask.Count).ToArray(),
        TypeTimes = (i.ExistingTypeIds ?? Array.Empty<int>()).Select(t => (t, OccupancyMask.AllYear)).ToList(),
        HasCover = i.HasCover,
        MaxHeightCm = i.MaxHeightCm,
        WidthCm = i.WidthCm,
        SoilPh = i.SoilPh,
        SoilType = i.SoilType,
        Category = i.Category ?? (i.Kind == ContainerKind.Bed ? ContainerCategory.Bed : ContainerCategory.Pot),
        HasTree = i.HasTree,
        HasVegetable = i.HasVegetable,
        TypeIds = new HashSet<int>(i.ExistingTypeIds ?? Array.Empty<int>())
    };

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
