namespace SeedPlanner.Domain;

/// <summary>Where between the minimum and the maximum of the demand per person a plan calculates.</summary>
public enum DemandLevel
{
    Min,
    Middle,
    Max,

    /// <summary>A position in percent between minimum (0) and maximum (100).</summary>
    Custom
}

/// <summary>Demand of one person per year: a range and its unit.</summary>
public record DemandRange(decimal? Min, decimal? Max, DemandUnit? Unit);

/// <summary>
/// The user's own demand for a crop. It replaces the default of the plant type completely
/// (minimum, maximum and unit), e.g. herbs in square meters instead of plants.
/// </summary>
public class UserDemand : IOwnedByUser
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";

    public int PlantTypeId { get; set; }
    public PlantType? PlantType { get; set; }

    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public DemandUnit? Unit { get; set; }

    public DemandRange ToRange() => new(Min, Max, Unit);
}

/// <summary>The user's own role of a plant type. It is stored only if it differs from the suggestion of the plant type.</summary>
public class UserPlantRole : IOwnedByUser
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";

    public int PlantTypeId { get; set; }
    public PlantType? PlantType { get; set; }

    public PlantRole Role { get; set; }
}

/// <summary>
/// The user's own change of the partners of a main plant (plant type): a secondary plant added (<see cref="Included"/>)
/// or a suggested one taken away. The suggestion itself comes from the table of good neighbours, see <see cref="PlantPairs"/>.
/// </summary>
public class UserPlantPartner : IOwnedByUser
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";

    public int MainPlantTypeId { get; set; }
    public PlantType? MainPlantType { get; set; }

    public int SecondaryPlantTypeId { get; set; }
    public PlantType? SecondaryPlantType { get; set; }

    /// <summary>True: the secondary plant was added by the user. False: it is a suggestion the user removed.</summary>
    public bool Included { get; set; }
}

/// <summary>
/// A plan for one year: the settings and the result, stored together. Several plans per year are possible,
/// e.g. a basic plan and one with guests.
/// </summary>
public class Plan : IOwnedByUser
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";

    public string Name { get; set; } = "";
    /// <summary>First day the plan is valid (default: the first day of this year).</summary>
    public DateOnly ValidFrom { get; set; } = new(DateTime.Today.Year, 1, 1);

    /// <summary>Last day the plan is valid; at most one year after <see cref="ValidFrom"/>, may run over the turn of the year.</summary>
    public DateOnly ValidTo { get; set; } = new(DateTime.Today.Year, 12, 31);

    /// <summary>The year the plan starts in (not stored).</summary>
    public int Year => ValidFrom.Year;

    /// <summary>Whether the periods of the two plans share at least one day.</summary>
    public bool Overlaps(Plan other) => ValidFrom <= other.ValidTo && other.ValidFrom <= ValidTo;

    // ---- settings
    public int Persons { get; set; } = 1;
    public DemandLevel Level { get; set; } = DemandLevel.Middle;

    /// <summary>Only used with <see cref="DemandLevel.Custom"/>: 0 = minimum, 100 = maximum.</summary>
    public int CustomPercent { get; set; } = 50;

    /// <summary>Whether varieties that are out of stock are included when a variety is added to the plan.</summary>
    public bool IncludeOutOfStock { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ---- settings of the distribution over pots and beds
    /// <summary>What happens when the chosen pots and beds are not enough.</summary>
    public ShortageStrategy Shortage { get; set; } = ShortageStrategy.ShrinkProportionally;

    /// <summary>What happens when plants for the cold frame are planned but no chosen bed has a cover.</summary>
    public NoCoverStrategy NoCover { get; set; } = NoCoverStrategy.NewCoveredBed;

    /// <summary>Largest area of a new bed in m²; 0 = no limit.</summary>
    public double MaxBedAreaSquareMeters { get; set; }

    /// <summary>Distances between the plants in percent of the spacing of the variety: 100 = as given, less = closer.</summary>
    public int SpacingPercent { get; set; } = 100;

    /// <summary>Last frost as a third of the year (0 = early January ... 35 = late December); default mid-May.</summary>
    public int LastFrostThird { get; set; } = PlantOccupancy.DefaultLastFrostThird;

    /// <summary>Other plants may use free space in covered beds (after the cold frame plants).</summary>
    public bool AllowOthersInCoveredBeds { get; set; }

    /// <summary>The pots and beds the user chose for this plan.</summary>
    public List<PlanContainer> Containers { get; set; } = new();

    // ---- result of the distribution (calculated again when the inputs change)
    /// <summary>The distribution as JSON (an <see cref="AllocationResult"/>).</summary>
    public string? AllocationJson { get; set; }

    /// <summary>Fingerprint of everything the distribution was calculated from.</summary>
    public string? AllocationHash { get; set; }
    public DateTime? AllocatedAt { get; set; }

    // ---- result
    public List<PlanItem> Items { get; set; } = new();

    public IEnumerable<PlanLine> ShoppingList => Items.SelectMany(i => i.Lines).Where(l => l.ToBuy);
}

/// <summary>A pot or a bed the user chose for a plan: exactly one of the two.</summary>
public class PlanContainer
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public Plan? Plan { get; set; }

    public int? PotId { get; set; }
    public Pot? Pot { get; set; }

    public int? RaisedBedId { get; set; }
    public RaisedBed? RaisedBed { get; set; }
}

/// <summary>The result for one crop: how much is needed, how many plants that makes, and the varieties they are spread over.</summary>
public class PlanItem
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public Plan? Plan { get; set; }

    public int PlantTypeId { get; set; }
    public PlantType? PlantType { get; set; }

    /// <summary>Demand of all persons together, in <see cref="Unit"/>. Empty if the crop has no demand.</summary>
    public decimal? DemandAmount { get; set; }
    public DemandUnit? Unit { get; set; }

    /// <summary>Plants calculated from the demand; empty if the spacing needed for the conversion is missing.</summary>
    public int? CalculatedPlants { get; set; }

    /// <summary>
    /// The amount the user entered instead of the demand, in <see cref="Unit"/> (square or row meters).
    /// The plants of it are worked out when the plan is calculated and put into <see cref="PlantsOverride"/>.
    /// </summary>
    public decimal? AmountOverride { get; set; }

    /// <summary>When within the sowing window the varieties of this crop are sown in this plan.</summary>
    public SowingTiming SowingTiming { get; set; } = SowingTiming.Start;

    /// <summary>The number of plants the user entered instead of the calculated one (or the plants of <see cref="AmountOverride"/>).</summary>
    public int? PlantsOverride { get; set; }

    public int Plants => PlantsOverride ?? CalculatedPlants ?? 0;

    public List<PlanLine> Lines { get; set; } = new();

    /// <summary>True if neither a calculation nor an own number exists, e.g. because the spacing is missing.</summary>
    public bool NeedsNumber => PlantsOverride is null && CalculatedPlants is null;

    // The choice is made per variety; the state of the crop follows from it
    /// <summary>Number of varieties that are part of the plan.</summary>
    public int IncludedVarieties => Lines.Count(l => l.Include);

    /// <summary>At least one variety is part of the plan.</summary>
    public bool IsIncluded => IncludedVarieties > 0;

    /// <summary>All varieties are part of the plan.</summary>
    public bool IsFullyIncluded => Lines.Count > 0 && IncludedVarieties == Lines.Count;

    /// <summary>Some, but not all, varieties are part of the plan.</summary>
    public bool IsPartlyIncluded => IsIncluded && !IsFullyIncluded;

    /// <summary>Spreads the plants of the crop evenly over the included varieties. Own numbers of a variety are kept.</summary>
    public void Spread()
    {
        var own = Lines.Where(l => l.Include && l.PlantsOverride is not null).Sum(l => l.PlantsOverride!.Value);
        var open = Lines.Where(l => l.Include && l.PlantsOverride is null).ToList();
        var shares = PlanCalculator.DistributeEvenly(Math.Max(0, Plants - own), open.Count);
        for (var i = 0; i < open.Count; i++) open[i].CalculatedPlants = shares[i];
        foreach (var line in Lines.Where(l => !l.Include || l.PlantsOverride is not null)) line.CalculatedPlants = 0;
    }
}

/// <summary>One variety of a plan item.</summary>
public class PlanLine
{
    public int Id { get; set; }
    public int PlanItemId { get; set; }
    public PlanItem? PlanItem { get; set; }

    public int PlantId { get; set; }
    public Plant? Plant { get; set; }

    /// <summary>The user decides per variety whether it is part of the plan.</summary>
    public bool Include { get; set; } = true;

    /// <summary>Stock status when the plan was calculated: the variety is not in stock and has to be bought.</summary>
    public bool OutOfStock { get; set; }

    public int CalculatedPlants { get; set; }
    public int? PlantsOverride { get; set; }

    public int Plants => Include ? PlantsOverride ?? CalculatedPlants : 0;

    // ---- progress: what the user has marked as done (see MonthlyTasks)
    /// <summary>When the sowing (indoors or directly) was marked as done.</summary>
    public DateOnly? SownOn { get; set; }

    /// <summary>When the plants started indoors were marked as planted out.</summary>
    public DateOnly? PlantedOutOn { get; set; }

    /// <summary>How many plants are harvested so far (a harvest may be partial).</summary>
    public int HarvestedPlants { get; set; }

    /// <summary>When the harvest was marked as finished (all plants harvested or the rest given up).</summary>
    public DateOnly? HarvestDoneOn { get; set; }

    public TaskProgress Progress => new(SownOn is not null, PlantedOutOn is not null, HarvestedPlants, HarvestDoneOn is not null);

    /// <summary>Goes on the shopping list.</summary>
    public bool ToBuy => Include && OutOfStock;
}

public static class PlanCalculator
{
    /// <summary>
    /// Demand of one person for the chosen level. With only one of the two bounds, that bound is used.
    /// Empty if there is no demand at all.
    /// </summary>
    public static decimal? AmountPerPerson(DemandRange demand, DemandLevel level, int customPercent)
    {
        var (min, max) = (demand.Min, demand.Max);
        if (min is null && max is null) return null;
        if (min is null) return max;
        if (max is null) return min;

        var low = Math.Min(min.Value, max.Value);
        var high = Math.Max(min.Value, max.Value);
        return level switch
        {
            DemandLevel.Min => low,
            DemandLevel.Max => high,
            DemandLevel.Middle => (low + high) / 2,
            _ => low + (high - low) * Math.Clamp(customPercent, 0, 100) / 100
        };
    }

    /// <summary>
    /// Converts an amount into a number of plants (rounded up). Row meters need the plant spacing in the row,
    /// square meters the row and plant spacing; if only one of them is known, a square grid is assumed.
    /// Empty if the spacing needed is missing.
    /// </summary>
    public static int? PlantsFor(decimal amount, DemandUnit unit, int? rowSpacingCm, int? plantSpacingCm)
    {
        if (amount <= 0) return 0;
        switch (unit)
        {
            case DemandUnit.Plants:
                return (int)Math.Ceiling(amount);

            case DemandUnit.RowMeters:
                if (plantSpacingCm is not > 0) return null;
                return (int)Math.Ceiling(amount * 100 / plantSpacingCm.Value);

            default: // SquareMeters
                var plant = plantSpacingCm is > 0 ? plantSpacingCm : rowSpacingCm;
                var row = rowSpacingCm is > 0 ? rowSpacingCm : plantSpacingCm;
                if (plant is not > 0 || row is not > 0) return null;
                var squareMetersPerPlant = (decimal)plant.Value * row.Value / 10_000m;
                return (int)Math.Ceiling(amount / squareMetersPerPlant);
        }
    }

    /// <summary>Average of the spacings that are known; empty if none is known.</summary>
    public static int? AverageSpacing(IEnumerable<int?> values)
    {
        var known = values.Where(v => v is > 0).Select(v => v!.Value).ToList();
        return known.Count == 0 ? null : (int)Math.Round(known.Average(), MidpointRounding.AwayFromZero);
    }

    /// <summary>Splits a number into the given number of parts that differ by at most 1; the first parts get the rest.</summary>
    public static int[] DistributeEvenly(int total, int parts)
    {
        if (parts <= 0) return Array.Empty<int>();
        var shares = new int[parts];
        for (var i = 0; i < parts; i++) shares[i] = total / parts + (i < total % parts ? 1 : 0);
        return shares;
    }

    /// <summary>
    /// Calculates the plan from the demand of the user (own value, otherwise the default of the crop) and the varieties.
    /// Existing items and varieties are kept with the user's own numbers and choices; new varieties follow the setting
    /// "include out of stock"; retired varieties and crops without demand disappear.
    /// The plant types must come with their varieties.
    /// </summary>
    public static void Rebuild(Plan plan, IEnumerable<PlantType> types, IReadOnlyDictionary<int, UserDemand> own)
    {
        var kept = new List<PlanItem>();
        foreach (var type in types.Where(t => t.Category != PlantCategory.Flower))
        {
            var demand = own.TryGetValue(type.Id, out var mine)
                ? mine.ToRange()
                : new DemandRange(type.DemandPerPersonMin, type.DemandPerPersonMax, type.DemandUnit);
            var perPerson = AmountPerPerson(demand, plan.Level, plan.CustomPercent);

            var varieties = type.Plants.Where(p => p.StockStatus != StockStatus.Retired).ToList();
            if (perPerson is null || demand.Unit is null || varieties.Count == 0) continue;

            var item = plan.Items.FirstOrDefault(i => i.PlantTypeId == type.Id) ?? new PlanItem { PlantTypeId = type.Id };
            item.PlantType = type;
            item.Unit = demand.Unit;
            item.DemandAmount = perPerson.Value * plan.Persons;

            // Spacing: average of the varieties that will be used, otherwise of all varieties of the crop
            var existing = item.Lines.ToDictionary(l => l.PlantId);
            var used = varieties.Where(p => existing.TryGetValue(p.Id, out var l)
                ? l.Include
                : p.StockStatus == StockStatus.InStock || plan.IncludeOutOfStock).ToList();
            var basis = used.Count > 0 ? used : varieties;
            var rowSpacing = AverageSpacing(basis.Select(p => p.RowSpacingCm));
            var plantSpacing = AverageSpacing(basis.Select(p => p.PlantSpacingCm));
            item.CalculatedPlants = PlantsFor(item.DemandAmount.Value, item.Unit.Value, rowSpacing, plantSpacing);

            // An own amount in square or row meters is converted with the same spacing
            if (item.AmountOverride is { } amount && item.Unit != DemandUnit.Plants)
                item.PlantsOverride = PlantsFor(amount, item.Unit.Value, rowSpacing, plantSpacing);

            var lines = new List<PlanLine>();
            foreach (var plant in varieties)
            {
                if (!existing.TryGetValue(plant.Id, out var line))
                {
                    line = new PlanLine
                    {
                        PlantId = plant.Id,
                        Include = plant.StockStatus == StockStatus.InStock || plan.IncludeOutOfStock
                    };
                }
                line.Plant = plant;
                line.OutOfStock = plant.StockStatus == StockStatus.OutOfStock;
                lines.Add(line);
            }
            // Change the list in place, so that a database context sees which lines were removed
            item.Lines.RemoveAll(l => !lines.Contains(l));
            item.Lines.AddRange(lines.Where(l => !item.Lines.Contains(l)));
            item.Spread();
            kept.Add(item);
        }
        plan.Items.RemoveAll(i => !kept.Contains(i));
        plan.Items.AddRange(kept.Where(i => !plan.Items.Contains(i)));
    }
}
