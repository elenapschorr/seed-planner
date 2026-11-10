using SeedPlanner.Domain;
using Xunit;

namespace SeedPlanner.Tests;

public class SeasonWindowPartsTests
{
    private static SeasonWindow Window(int startMonth, MonthPart startPart, int endMonth, MonthPart endPart) =>
        new()
        {
            Kind = WindowKind.Sowing,
            StartMonth = startMonth, StartPart = startPart,
            EndMonth = endMonth, EndPart = endPart
        };

    [Fact]
    public void WindowWithoutParts_IsWholeMonths()
    {
        var window = new SeasonWindow { StartMonth = 3, EndMonth = 5 };

        Assert.Equal(MonthPart.Whole, window.StartPart);
        Assert.Equal(MonthPart.Whole, window.EndPart);
        Assert.Equal(6, window.StartThird);    // early March
        Assert.Equal(14, window.EndThird);     // late May
        Assert.True(window.Contains(3, MonthPart.Early));
        Assert.True(window.Contains(5, MonthPart.Late));
        Assert.False(window.Contains(2, MonthPart.Late));
        Assert.False(window.Contains(6, MonthPart.Early));
    }

    [Theory]
    [InlineData(3, MonthPart.Early, false)]
    [InlineData(3, MonthPart.Mid, true)]
    [InlineData(3, MonthPart.Late, true)]
    [InlineData(5, MonthPart.Early, true)]
    [InlineData(5, MonthPart.Mid, true)]
    [InlineData(5, MonthPart.Late, false)]
    public void MidMarchToMidMay_ChecksTheParts(int month, MonthPart part, bool expected)
    {
        var window = Window(3, MonthPart.Mid, 5, MonthPart.Mid);

        Assert.Equal(expected, window.Contains(month, part));
    }

    [Fact]
    public void MonthLevelContains_IgnoresParts()
    {
        var window = Window(3, MonthPart.Late, 5, MonthPart.Early);

        Assert.True(window.Contains(3));
        Assert.True(window.Contains(4));
        Assert.True(window.Contains(5));
        Assert.False(window.Contains(6));
    }

    [Theory]
    [InlineData(11, MonthPart.Early, false)]
    [InlineData(11, MonthPart.Mid, true)]
    [InlineData(12, MonthPart.Late, true)]
    [InlineData(1, MonthPart.Early, true)]
    [InlineData(2, MonthPart.Early, true)]
    [InlineData(2, MonthPart.Mid, false)]
    public void WindowAcrossNewYear_MidNovemberToEarlyFebruary(int month, MonthPart part, bool expected)
    {
        var window = Window(11, MonthPart.Mid, 2, MonthPart.Early);

        Assert.Equal(expected, window.Contains(month, part));
    }

    [Fact]
    public void SingleMonthWindow_EarlyToMid()
    {
        var window = Window(4, MonthPart.Early, 4, MonthPart.Mid);

        Assert.True(window.StartThird <= window.EndThird);
        Assert.True(window.Contains(4, MonthPart.Early));
        Assert.True(window.Contains(4, MonthPart.Mid));
        Assert.False(window.Contains(4, MonthPart.Late));
    }

    [Fact]
    public void SingleMonthWindow_EndBeforeStart_IsDetectable()
    {
        // The edit page rejects this: within one month the end must not be before the start
        var window = Window(4, MonthPart.Late, 4, MonthPart.Early);

        Assert.True(window.StartThird > window.EndThird);
    }
}
