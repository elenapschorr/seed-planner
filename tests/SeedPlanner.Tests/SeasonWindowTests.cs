using SeedPlanner.Domain;
using Xunit;

namespace SeedPlanner.Tests;

public class SeasonWindowTests
{
    private static SeasonWindow Window(int start, int end) =>
        new() { Kind = WindowKind.Sowing, StartMonth = start, EndMonth = end };

    [Theory]
    [InlineData(3, true)]   // first month
    [InlineData(4, true)]
    [InlineData(5, true)]   // last month
    [InlineData(2, false)]  // before
    [InlineData(6, false)]  // after
    public void Contains_NormalWindow(int month, bool expected)
    {
        Assert.Equal(expected, Window(3, 5).Contains(month));
    }

    [Theory]
    [InlineData(11, true)]
    [InlineData(12, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(10, false)]
    public void Contains_WindowAcrossNewYear(int month, bool expected)
    {
        Assert.Equal(expected, Window(11, 2).Contains(month));
    }

    [Theory]
    [InlineData(6, true)]
    [InlineData(5, false)]
    [InlineData(7, false)]
    public void Contains_SingleMonth(int month, bool expected)
    {
        Assert.Equal(expected, Window(6, 6).Contains(month));
    }

    [Fact]
    public void Contains_WholeYear()
    {
        var window = Window(1, 12);
        for (var month = 1; month <= 12; month++)
        {
            Assert.True(window.Contains(month));
        }
    }
}
