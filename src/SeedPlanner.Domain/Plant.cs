namespace SeedPlanner.Domain;

public class Plant
{
    public int Id { get; set; }
    public int PlantTypeId { get; set; }
    public PlantType PlantType { get; set; } = null!;
    public required string Name { get; set; }

    /// <summary>Seed, young plant or cutting.</summary>
    public Propagation Propagation { get; set; } = Propagation.Seed;

    /// <summary>Open-pollinated or F1; only meaningful for seed, otherwise empty.</summary>
    public SeedType? SeedType { get; set; }

    public StockStatus StockStatus { get; set; }
    public SowingMethod? SowingMethod { get; set; }
    public GrowingPlace? GrowingPlace { get; set; }

    /// <summary>Overrides the placement of the plant type (see <see cref="Domain.PlaceOrders"/>). Empty = as the plant type.</summary>
    public string? PlaceOrder { get; set; }
    public Exposure? Exposure { get; set; }

    /// <summary>Main or secondary plant. Empty = as the plant type (its suggestion or the user's own role for it).</summary>
    public PlantRole? Role { get; set; }
    public Exposure? EffectiveExposure => Exposure ?? PlantType?.Exposure;

    public int? GerminationDaysMin { get; set; }
    public int? GerminationDaysMax { get; set; }

    /// <summary>Germination temperature range in °C.</summary>
    public int? GerminationTempMinC { get; set; }
    public int? GerminationTempMaxC { get; set; }

    public int? MaturityDaysMin { get; set; }
    public int? MaturityDaysMax { get; set; }

    /// <summary>Weeks from sowing to planting out (only for plants started indoors).</summary>
    public int? IndoorWeeksMin { get; set; }
    public int? IndoorWeeksMax { get; set; }

    /// <summary>Is planted out only after the last frost (see the plan setting "last frost").</summary>
    public bool FrostSensitive { get; set; }

    public int? RowSpacingCm { get; set; }
    public int? PlantSpacingCm { get; set; }
    public int? PotSizeMinLiters { get; set; }
    public int? PotSizeMaxLiters { get; set; }

    /// <summary>Plant height in cm (range). Used to decide which plants can stand under which.</summary>
    public int? HeightMinCm { get; set; }
    public int? HeightMaxCm { get; set; }

    /// <summary>Longest vine or shoot in cm; only for sprawling or climbing plants (melon, squash, ...).</summary>
    public int? VineLengthMaxCm { get; set; }

    public List<SeasonWindow> SeasonWindows { get; } = [];

    // Rule: buy seed only for plants that can be propagated from own seed.
    // Young plants and cuttings are not seed, so they are never marked "to buy".
    public bool ShouldBuy =>
        StockStatus == StockStatus.OutOfStock
        && Propagation == Propagation.Seed
        && SeedType == global::SeedPlanner.Domain.SeedType.OpenPollinated;

    public bool CanSowIn(int month) => SeasonWindows.Any(w => w.Kind == WindowKind.Sowing && w.Contains(month));
    public string OwnerId { get; set; } = "";
}
