namespace SeedPlanner.Domain;

/// <summary>Which part of a month a window starts or ends in.</summary>
public enum MonthPart
{
    /// <summary>The whole month: as a start it means the beginning, as an end the end of the month.</summary>
    Whole,
    Early,
    Mid,
    Late
}

public class SeasonWindow
{
    public int Id { get; set; }
    public int PlantId { get; set; }
    public WindowKind Kind { get; set; }
    public int StartMonth { get; set; }   // 1..12
    public int EndMonth { get; set; }     // 1..12

    // Part of the start and end month. Windows without parts are whole months.
    public MonthPart StartPart { get; set; } = MonthPart.Whole;
    public MonthPart EndPart { get; set; } = MonthPart.Whole;

    // A window may wrap over the new year, e.g. November (11) to March (3).
    public bool Contains(int month) =>
        StartMonth <= EndMonth
            ? month >= StartMonth && month <= EndMonth
            : month >= StartMonth || month <= EndMonth;

    // Position of the first and the last third of the window in the year: 0 = early January ... 35 = late December
    public int StartThird => (StartMonth - 1) * 3 + (StartPart is MonthPart.Mid ? 1 : StartPart is MonthPart.Late ? 2 : 0);
    public int EndThird => (EndMonth - 1) * 3 + (EndPart is MonthPart.Early ? 0 : EndPart is MonthPart.Mid ? 1 : 2);

    // Is that part of the month inside the window? (part must be Early, Mid or Late)
    public bool Contains(int month, MonthPart part)
    {
        var third = (month - 1) * 3 + (part is MonthPart.Mid ? 1 : part is MonthPart.Late ? 2 : 0);
        return StartThird <= EndThird
            ? third >= StartThird && third <= EndThird
            : third >= StartThird || third <= EndThird;
    }

    public Plant? Plant { get; set; }
}
