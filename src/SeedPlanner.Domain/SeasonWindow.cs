namespace SeedPlanner.Domain;

public class SeasonWindow
{
    public int Id { get; set; }
    public int PlantId { get; set; }
    public WindowKind Kind { get; set; }
    public int StartMonth { get; set; }   // 1..12
    public int EndMonth { get; set; }     // 1..12

    // A window may wrap over the new year, e.g. November (11) to March (3).
    public bool Contains(int month) =>
        StartMonth <= EndMonth
            ? month >= StartMonth && month <= EndMonth
            : month >= StartMonth || month <= EndMonth;
}