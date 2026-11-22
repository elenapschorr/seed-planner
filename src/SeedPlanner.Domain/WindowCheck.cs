namespace SeedPlanner.Domain;

/// <summary>What is wrong with a window the user entered (months and parts of months).</summary>
public enum WindowProblem
{
    None,
    /// <summary>Only one of the two months is set, or a month is not 1 to 12.</summary>
    Incomplete,
    /// <summary>Within one month the end part comes before the start part.</summary>
    Order
}

public static class WindowCheck
{
    /// <summary>
    /// Both months or none. A window may run across the new year, so a start month after the end month is fine;
    /// within one month the end part must not come before the start part. A window without months is empty and fine.
    /// </summary>
    public static WindowProblem Check(int? fromMonth, MonthPart fromPart, int? toMonth, MonthPart toPart)
    {
        if ((fromMonth is null) != (toMonth is null)) return WindowProblem.Incomplete;
        if (fromMonth is null || toMonth is null) return WindowProblem.None;
        if (fromMonth is < 1 or > 12 || toMonth is < 1 or > 12) return WindowProblem.Incomplete;

        var window = new SeasonWindow { StartMonth = fromMonth.Value, StartPart = fromPart, EndMonth = toMonth.Value, EndPart = toPart };
        return window.StartMonth == window.EndMonth && window.StartThird > window.EndThird ? WindowProblem.Order : WindowProblem.None;
    }
}
