using SeedPlanner.Domain;
using Xunit;

namespace SeedPlanner.Tests;

public class PlotGeometryTests
{
    private static List<PlotPoint> Points(params (double X, double Y)[] p) => p.Select(x => new PlotPoint(x.X, x.Y)).ToList();

    private static readonly List<PlotPoint> Square = Points((0, 0), (100, 0), (100, 100), (0, 100));

    [Fact]
    public void The_area_is_the_same_for_either_direction()
    {
        Assert.Equal(10_000, PlotGeometry.Area(Square), 6);
        Assert.Equal(10_000, PlotGeometry.Area(Enumerable.Reverse(Square).ToList()), 6);
    }

    [Fact]
    public void Fewer_than_three_corners_have_no_area()
    {
        Assert.Equal(0, PlotGeometry.Area(Points((0, 0), (10, 0))));
    }

    [Fact]
    public void One_known_length_gives_the_scale_and_the_area_in_square_meters()
    {
        // 100 pixels are 10 meters: 1 pixel = 0.1 m, the square is 10 x 10 m
        var scale = PlotGeometry.MetersPerPixel(Square, [10, null, null, null]);

        Assert.Equal(0.1, scale!.Value, 9);
        Assert.Equal(100, PlotGeometry.AreaM2(Square, scale)!.Value, 6);
    }

    [Fact]
    public void Without_a_length_the_scale_is_not_known()
    {
        Assert.Null(PlotGeometry.MetersPerPixel(Square, [null, null, null, null]));
        Assert.Null(PlotGeometry.MetersPerPixel(Square, []));
        Assert.Null(PlotGeometry.AreaM2(Square, null));
    }

    [Fact]
    public void Several_lengths_are_averaged_and_the_deviation_shows_the_odd_one()
    {
        // Three edges say 10 m, one says 12 m: the scale is in between and the 12 stands out
        var lengths = new double?[] { 10, 10, 10, 12 };
        var scale = PlotGeometry.MetersPerPixel(Square, lengths)!.Value;

        Assert.InRange(scale, 0.1, 0.12);
        Assert.True(PlotGeometry.Deviation(Square, lengths, 3, scale)!.Value < -0.05);
        Assert.True(PlotGeometry.Deviation(Square, lengths, 0, scale)!.Value > 0);
        Assert.Null(PlotGeometry.Deviation(Square, new double?[] { null, 10, 10, 10 }, 0, scale));
    }

    [Fact]
    public void The_corners_of_a_rectangle_are_right_angles_in_either_direction()
    {
        foreach (var shape in new[] { Square, Enumerable.Reverse(Square).ToList() })
            for (var i = 0; i < 4; i++)
                Assert.Equal(90, PlotGeometry.InteriorAngle(shape, i), 6);
    }

    [Fact]
    public void A_corner_that_bends_inward_has_more_than_180_degrees()
    {
        // L-shape: the inner corner at (1,1)
        var l = Points((0, 0), (2, 0), (2, 1), (1, 1), (1, 2), (0, 2));

        Assert.Equal(270, PlotGeometry.InteriorAngle(l, 3), 6);
        Assert.Equal(90, PlotGeometry.InteriorAngle(l, 2), 6);
        Assert.Equal(720, Enumerable.Range(0, 6).Sum(i => PlotGeometry.InteriorAngle(l, i)), 6); // (n - 2) * 180
    }

    [Fact]
    public void A_line_that_crosses_itself_is_found()
    {
        var bowTie = Points((0, 0), (10, 10), (10, 0), (0, 10));

        Assert.True(PlotGeometry.SelfIntersects(bowTie));
        Assert.False(PlotGeometry.SelfIntersects(Square));
    }

    private static double Free(List<PlotPoint> boundary, params List<PlotPoint>[] zones) => PlotGeometry.AreaWithout(boundary, zones);

    [Fact]
    public void Without_blocked_areas_the_whole_area_is_free()
    {
        Assert.Equal(10_000, Free(Square), 6);
    }

    [Fact]
    public void A_blocked_area_inside_is_cut_out()
    {
        var garage = Points((10, 10), (30, 10), (30, 40), (10, 40)); // 20 x 30

        Assert.Equal(10_000 - 600, Free(Square, garage), 6);
    }

    [Fact]
    public void Only_the_part_of_a_blocked_area_inside_the_plot_counts()
    {
        // The garage sticks out of the plot on the right: 20 of its 40 pixels in width are inside
        var garage = Points((80, 10), (120, 10), (120, 30), (80, 30));

        Assert.Equal(10_000 - 20 * 20, Free(Square, garage), 6);
    }

    [Fact]
    public void Overlapping_blocked_areas_are_taken_off_once()
    {
        var a = Points((10, 10), (50, 10), (50, 50), (10, 50)); // 1600
        var b = Points((30, 30), (70, 30), (70, 70), (30, 70)); // 1600, overlap 20 x 20 = 400

        Assert.Equal(10_000 - (1600 + 1600 - 400), Free(Square, a, b), 6);
    }

    [Fact]
    public void A_blocked_area_outside_or_covering_everything_is_handled()
    {
        var outside = Points((200, 0), (300, 0), (300, 100), (200, 100));
        var everything = Points((-10, -10), (200, -10), (200, 200), (-10, 200));

        Assert.Equal(10_000, Free(Square, outside), 6);
        Assert.Equal(0, Free(Square, everything), 6);
    }

    [Fact]
    public void A_slanted_blocked_area_is_cut_exactly_at_the_edge_of_the_plot()
    {
        // Triangle (50,50), (150,50), (50,100) has the area 2500; the part right of x = 100 (a triangle of 50 x 25 / 2 = 625) is outside
        var triangle = Points((50, 50), (150, 50), (50, 100));

        Assert.Equal(10_000 - (2500 - 625), Free(Square, triangle), 6);
    }

    [Fact]
    public void A_bent_boundary_is_cut_correctly()
    {
        // L-shape with area 3 (2x2 without the corner square); the blocked square covers the corner and the empty part
        var l = Points((0, 0), (2, 0), (2, 1), (1, 1), (1, 2), (0, 2));
        var blocked = Points((1, 0), (3, 0), (3, 3), (1, 3));

        Assert.Equal(3, Free(l), 6);
        Assert.Equal(2, Free(l, blocked), 6);
    }

    [Fact]
    public void A_shared_garage_is_cut_out_for_the_neighbours_half_and_occupied_for_my_half()
    {
        // Garage 20 x 20 at the right edge inside the plot: left half (10 x 20) is mine, right half the neighbour's
        var mineHalf = Points((60, 10), (70, 10), (70, 30), (60, 30));     // 200
        var neighbourHalf = Points((70, 10), (80, 10), (80, 30), (70, 30)); // 200

        var areas = PlotGeometry.Areas(Square, [neighbourHalf], [mineHalf]);

        Assert.Equal(10_000, areas.Total, 6);
        Assert.Equal(200, areas.Cut, 6);
        Assert.Equal(200, areas.Occupied, 6);
        Assert.Equal(10_000 - 400, areas.Free, 6);
    }

    [Fact]
    public void An_occupied_area_that_lies_on_a_cut_out_area_is_counted_once()
    {
        var cut = Points((0, 0), (50, 0), (50, 100), (0, 100)); // 5000
        var occupied = Points((25, 0), (75, 0), (75, 100), (25, 100)); // 5000, 2500 of it on the cut-out part

        var areas = PlotGeometry.Areas(Square, [cut], [occupied]);

        Assert.Equal(5000, areas.Cut, 6);
        Assert.Equal(2500, areas.Occupied, 6);
        Assert.Equal(2500, areas.Free, 6);
    }
}
