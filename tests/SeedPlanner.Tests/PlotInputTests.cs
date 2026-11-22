using SeedPlanner.Domain;

namespace SeedPlanner.Tests;

public class PlotInputTests
{
    [Fact]
    public void ParsePoints_ReadsPairsOfNumbers()
    {
        var points = PlotInput.ParsePoints("[[1,2],[3.5,4]]");

        Assert.NotNull(points);
        Assert.Equal(2, points.Count);
        Assert.Equal(3.5, points[1].X);
        Assert.Equal(4, points[1].Y);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[[1,2,3]]")]
    [InlineData("[[1]]")]
    [InlineData("[[1000000,2]]")]
    public void ParsePoints_RejectsWhatMakesNoSense(string json)
    {
        Assert.Null(PlotInput.ParsePoints(json));
    }

    [Fact]
    public void ParsePoints_EmptyTextIsNoPoints()
    {
        Assert.Empty(PlotInput.ParsePoints("")!);
    }

    [Fact]
    public void ParsePoints_RejectsTooManyCorners()
    {
        var json = "[" + string.Join(",", Enumerable.Repeat("[1,1]", 201)) + "]";

        Assert.Null(PlotInput.ParsePoints(json));
    }

    [Fact]
    public void ParseLengths_KeepsUsableLengthsAndFillsUp()
    {
        var lengths = PlotInput.ParseLengths("[12.34567, -3, null]", 4);

        Assert.Equal(4, lengths.Length);
        Assert.Equal(12.346, lengths[0]);
        Assert.Null(lengths[1]);
        Assert.Null(lengths[2]);
        Assert.Null(lengths[3]);
    }

    [Fact]
    public void ParseNeighbors_TrimsAndTakesAtMostTen()
    {
        var json = "[\"  Anna \"," + string.Join(",", Enumerable.Range(0, 14).Select(i => $"\"N{i}\"")) + "]";

        var neighbors = PlotInput.ParseNeighbors(json);

        Assert.Equal(PlotInput.MaxNeighbors, neighbors.Length);
        Assert.Equal("Anna", neighbors[0]);
    }

    [Fact]
    public void ParseCutLine_TakesTheOldFormatWithTwoPoints()
    {
        var line = PlotInput.ParseCutLine("[[0,0],[10,10]]", 0);

        Assert.NotNull(line);
        Assert.Equal(0, line.Owner1);
        Assert.Equal(10, line.B[0]);
    }

    [Fact]
    public void ParseCutLine_UnknownOwnerBecomesTheUser()
    {
        var line = PlotInput.ParseCutLine("{\"a\":[0,0],\"b\":[5,5],\"owner1\":3,\"owner2\":1}", 1);

        Assert.NotNull(line);
        Assert.Equal(0, line.Owner1);
        Assert.Equal(1, line.Owner2);
    }

    [Fact]
    public void ParsePlaced_KeepsKnownBedsOnceAndTurnsTheAngleIntoZeroTo360()
    {
        var json = "[{\"bedId\":1,\"x\":10.04,\"y\":20,\"angle\":-90},{\"bedId\":1,\"x\":5,\"y\":5,\"angle\":0},{\"bedId\":9,\"x\":1,\"y\":1,\"angle\":0}]";

        var placed = PlotInput.ParsePlaced(json, [1, 2]);

        var bed = Assert.Single(placed);
        Assert.Equal(1, bed.BedId);
        Assert.Equal(10, bed.X);
        Assert.Equal(270, bed.Angle);
    }

    [Fact]
    public void ContentTypeOf_RecognisesPictureByItsFirstBytes()
    {
        Assert.Equal("image/png", PlotInput.ContentTypeOf([0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0, 0]));
        Assert.Equal("image/jpeg", PlotInput.ContentTypeOf([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Null(PlotInput.ContentTypeOf([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13]));
    }

    [Fact]
    public void Pick_OnlyAcceptsKnownValues()
    {
        Assert.Equal("Sand", PlotInput.Pick("Sand", "Sand", "Clay"));
        Assert.Equal("", PlotInput.Pick("Gravel", "Sand", "Clay"));
        Assert.Equal("", PlotInput.Pick(null, "Sand", "Clay"));
    }
}
