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
    SoilMismatch,

    /// <summary>A new bed would be needed, but the free garden area of the plot is not enough (or no garden area is set).</summary>
    NoGardenArea
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
