namespace SeedPlanner.Domain;

/// <summary>
/// How two plant types get along as neighbours. Each pair is stored once, and
/// PlantTypeId is always the smaller of the two ids, so the order carries no meaning.
/// Pairs that are not stored are neutral or unknown.
/// </summary>
public class PlantTypeCompanion
{
    public int Id { get; set; }
    public int PlantTypeId { get; set; }
    public int OtherPlantTypeId { get; set; }
    public CompanionRelation Relation { get; set; }
    public string? Note { get; set; }

    /// <summary>Creates a pair in the right order, whichever type is named first.</summary>
    public static PlantTypeCompanion Between(
        int firstTypeId, int secondTypeId, CompanionRelation relation, string? note = null)
    {
        if (firstTypeId == secondTypeId)
            throw new ArgumentException("A plant type cannot be its own neighbour.");

        return new PlantTypeCompanion
        {
            PlantTypeId = Math.Min(firstTypeId, secondTypeId),
            OtherPlantTypeId = Math.Max(firstTypeId, secondTypeId),
            Relation = relation,
            Note = note
        };
    }
}
