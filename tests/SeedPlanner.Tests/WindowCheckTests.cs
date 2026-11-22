using SeedPlanner.Domain;
using Xunit;

namespace SeedPlanner.Tests;

public class WindowCheckTests
{
    [Fact]
    public void EmptyWindowIsFine()
    {
        Assert.Equal(WindowProblem.None, WindowCheck.Check(null, MonthPart.Whole, null, MonthPart.Whole));
    }

    [Theory]
    [InlineData(3, null)]
    [InlineData(null, 5)]
    [InlineData(0, 5)]
    [InlineData(3, 13)]
    public void OneMonthOnlyOrMonthOutOfRangeIsIncomplete(int? from, int? to)
    {
        Assert.Equal(WindowProblem.Incomplete, WindowCheck.Check(from, MonthPart.Whole, to, MonthPart.Whole));
    }

    [Fact]
    public void WindowMayRunAcrossTheNewYear()
    {
        Assert.Equal(WindowProblem.None, WindowCheck.Check(11, MonthPart.Whole, 3, MonthPart.Whole));
    }

    [Fact]
    public void InOneMonthTheEndPartMustNotComeBeforeTheStartPart()
    {
        Assert.Equal(WindowProblem.Order, WindowCheck.Check(5, MonthPart.Late, 5, MonthPart.Early));
        Assert.Equal(WindowProblem.None, WindowCheck.Check(5, MonthPart.Early, 5, MonthPart.Late));
        Assert.Equal(WindowProblem.None, WindowCheck.Check(5, MonthPart.Whole, 5, MonthPart.Whole));
    }
}
