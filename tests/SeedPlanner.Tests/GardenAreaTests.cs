using SeedPlanner.Domain;

namespace SeedPlanner.Tests;

public class GardenAreaTests
{
    // A square of 100 x 100 pixels with edges of 10 m: 100 m², 0.1 m per pixel
    private static Plot Square(params PlotZone[] zones) => new()
    {
        BoundaryJson = "[[0,0],[100,0],[100,100],[0,100]]",
        EdgeLengthsJson = "[10,10,10,10]",
        Zones = zones.ToList()
    };

    private static PlotZone Zone(PlotZoneKind kind, string points, int owner = 0) => new() { Name = kind.ToString(), Kind = kind, Owner = owner, PointsJson = points };

    private const string LeftHalf = "[[0,0],[50,0],[50,100],[0,100]]";
    private const string TopQuarter = "[[0,0],[100,0],[100,25],[0,25]]";

    [Fact]
    public void Without_a_garden_area_there_is_no_number()
    {
        Assert.Null(GardenArea.TotalSquareMeters(Square()));
        Assert.Null(GardenArea.TotalSquareMeters(Square(Zone(PlotZoneKind.House, LeftHalf))));
    }

    [Fact]
    public void Without_a_scale_there_is_no_number()
    {
        var plot = Square(Zone(PlotZoneKind.Garden, LeftHalf));
        plot.EdgeLengthsJson = "[]";
        Assert.Null(GardenArea.TotalSquareMeters(plot));
    }

    [Fact]
    public void The_known_area_gives_the_scale_if_there_are_no_edge_lengths()
    {
        var plot = Square(Zone(PlotZoneKind.Garden, LeftHalf));
        plot.EdgeLengthsJson = "[]";
        plot.KnownAreaM2 = 400;
        Assert.Equal(200, GardenArea.TotalSquareMeters(plot)!.Value, 6);
    }

    [Fact]
    public void A_garden_area_counts_with_its_size()
    {
        Assert.Equal(50, GardenArea.TotalSquareMeters(Square(Zone(PlotZoneKind.Garden, LeftHalf)))!.Value, 6);
    }

    [Fact]
    public void What_is_built_on_is_taken_off_the_garden_area()
    {
        var plot = Square(Zone(PlotZoneKind.Garden, LeftHalf), Zone(PlotZoneKind.Terrace, TopQuarter));
        // left half 50 m² minus the part of the terrace inside it (50 x 25 px = 12.5 m²)
        Assert.Equal(37.5, GardenArea.TotalSquareMeters(plot)!.Value, 6);
    }

    [Fact]
    public void Several_garden_areas_that_overlap_count_once()
    {
        var plot = Square(Zone(PlotZoneKind.Garden, LeftHalf), Zone(PlotZoneKind.Garden, TopQuarter));
        // 50 + 25 - overlap 12.5
        Assert.Equal(62.5, GardenArea.TotalSquareMeters(plot)!.Value, 6);
    }

    [Fact]
    public void The_free_area_is_the_garden_area_minus_the_existing_beds()
    {
        var plot = Square(Zone(PlotZoneKind.Garden, LeftHalf));
        var beds = new[] { new RaisedBed { WidthCm = 100, LengthCm = 200, Shape = BedShape.Rectangular } };

        var free = GardenArea.FreeSquareMeters(plot, beds);

        Assert.Equal(50 - beds[0].AreaSquareMeters, free!.Value, 6);
        Assert.Null(GardenArea.FreeSquareMeters(null, beds));
        Assert.Equal(0, GardenArea.FreeSquareMeters(plot, Enumerable.Repeat(new RaisedBed { WidthCm = 1000, LengthCm = 1000, Shape = BedShape.Rectangular }, 2))!.Value);
    }
}
