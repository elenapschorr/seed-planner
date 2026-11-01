namespace SeedPlanner.Domain;

public class PlantTypeTranslation
{
    public int Id { get; set; }
    public int PlantTypeId { get; set; }
    public required string Language { get; set; }   // "de", "en", "ru"
    public required string Name { get; set; }
}