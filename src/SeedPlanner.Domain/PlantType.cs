namespace SeedPlanner.Domain;

public class PlantType
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public PlantCategory? Category { get; set; }
    public Lifecycle? Lifecycle { get; set; }
    public NutrientDemand? NutrientDemand { get; set; }
    public GerminationType? GerminationType { get; set; }
    public int? GrowthDaysMin { get; set; }
    public int? GrowthDaysMax { get; set; }
    public decimal? DemandPerPersonMin { get; set; }
    public decimal? DemandPerPersonMax { get; set; }
    public DemandUnit? DemandUnit { get; set; }
    public List<Plant> Plants { get; } = [];
    public Exposure? Exposure { get; set; }

    /// <summary>Suggested role (main or secondary plant); users can change it for themselves (<see cref="UserPlantRole"/>).</summary>
    public PlantRole? Role { get; set; }

    /// <summary>
    /// Where plants of this type go in the distribution: the allowed kinds of container, first choice first
    /// (see <see cref="Domain.PlaceOrders"/>). Empty = decided by the growing place of the variety (not set: into a bed).
    /// A variety can override it.
    /// </summary>
    public string? PlaceOrder { get; set; }

    /// <summary>Preferred soil pH range (e.g. 6.0 to 7.0).</summary>
    public decimal? PhMin { get; set; }
    public decimal? PhMax { get; set; }

    /// <summary>The soil the plant type likes best.</summary>
    public SoilType? SoilType { get; set; }
    public List<PlantTypeTranslation> Translations { get; set; } = new();
}
