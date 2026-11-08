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
    public List<PlantTypeTranslation> Translations { get; set; } = new();
}
