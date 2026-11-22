namespace SeedPlanner.Domain;

// The containers of one run: the working copy of a pot or bed (Box), the rules about neighbours (Rules) and the options (Opt).
public static partial class PlanAllocator
{
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

    /// <param name="NewBedBudget">Total area in m² all new beds together may have; null = no limit.</param>
    private sealed record Opt(ShortageStrategy Strategy, double MaxBed, NoCoverStrategy NoCover, double? NewBedBudget = null);

    /// <summary>The area that is left for new beds in one run; every new bed takes its size from it.</summary>
    private sealed class BedBudget(double? total)
    {
        private double? _left = total;

        public bool Limited => _left is not null;

        public bool Exhausted => _left is { } left && left <= Eps;

        /// <summary>Gives back the area of a bed that was not kept.</summary>
        public void Refund(double size) => _left += size;

        /// <summary>The area a new bed may get when it would like <paramref name="wanted"/> m²; 0 if nothing is left.</summary>
        public double Take(double wanted)
        {
            if (_left is not { } left) return wanted;
            var given = Math.Max(0, Math.Min(wanted, left));
            _left = left - given;
            return given;
        }
    }

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
}
