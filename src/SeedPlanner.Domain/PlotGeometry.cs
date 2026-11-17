namespace SeedPlanner.Domain;

/// <summary>A corner in pixels of the plot picture.</summary>
public readonly record struct PlotPoint(double X, double Y);

/// <summary>
/// Calculations for the closed line of the plot: area, edge lengths, scale (meters per pixel, from the lengths the user
/// entered), angles at the corners and a check that the line does not cross itself.
/// </summary>
public static class PlotGeometry
{
    private static double SignedArea(IReadOnlyList<PlotPoint> p)
    {
        var sum = 0.0;
        for (var i = 0; i < p.Count; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return sum / 2;
    }

    /// <summary>Area in square pixels (shoelace formula); 0 for fewer than 3 corners.</summary>
    public static double Area(IReadOnlyList<PlotPoint> p) => p.Count < 3 ? 0 : Math.Abs(SignedArea(p));

    /// <summary>Length in pixels of the edge from corner <paramref name="i"/> to the next one (the last edge closes the line).</summary>
    public static double EdgeLength(IReadOnlyList<PlotPoint> p, int i)
    {
        var a = p[i];
        var b = p[(i + 1) % p.Count];
        return Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
    }

    /// <summary>
    /// Meters per pixel from the edge lengths the user knows (least squares over all of them, so a single measuring error
    /// does not decide). Null if there is no usable length.
    /// </summary>
    public static double? MetersPerPixel(IReadOnlyList<PlotPoint> p, IReadOnlyList<double?> lengthsM)
    {
        double num = 0, den = 0;
        for (var i = 0; i < Math.Min(p.Count, lengthsM.Count); i++)
        {
            if (lengthsM[i] is not { } meters || meters <= 0) continue;
            var pixels = EdgeLength(p, i);
            num += meters * pixels;
            den += pixels * pixels;
        }
        return den > 0 ? num / den : null;
    }

    /// <summary>Area in square meters, or null if the scale is not known.</summary>
    public static double? AreaM2(IReadOnlyList<PlotPoint> p, double? metersPerPixel) =>
        metersPerPixel is { } s ? Area(p) * s * s : null;

    /// <summary>How far the length measured in the picture is from the entered one: +0.1 = 10 % longer in the picture. Null without a length.</summary>
    public static double? Deviation(IReadOnlyList<PlotPoint> p, IReadOnlyList<double?> lengthsM, int i, double metersPerPixel)
    {
        if (i >= lengthsM.Count || lengthsM[i] is not { } meters || meters <= 0) return null;
        return (EdgeLength(p, i) * metersPerPixel - meters) / meters;
    }

    /// <summary>Interior angle at corner <paramref name="i"/> in degrees (0 to 360; more than 180 where the line bends inward).</summary>
    public static double InteriorAngle(IReadOnlyList<PlotPoint> p, int i)
    {
        var a = p[(i + p.Count - 1) % p.Count];
        var b = p[i];
        var c = p[(i + 1) % p.Count];
        var angle = Math.Atan2(
            Math.Abs((a.X - b.X) * (c.Y - b.Y) - (a.Y - b.Y) * (c.X - b.X)),
            (a.X - b.X) * (c.X - b.X) + (a.Y - b.Y) * (c.Y - b.Y)) * 180 / Math.PI;

        var turn = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
        var orientation = Math.Sign(SignedArea(p));
        if (turn == 0 || orientation == 0) return angle;
        return turn * orientation > 0 ? angle : 360 - angle;
    }

    /// <summary>The areas of a plot in square pixels: all of the boundary, the part cut out (not the user's), the part occupied, and what is free.</summary>
    public record PlotAreas(double Total, double Cut, double Occupied, double Free);

    /// <summary>
    /// Cut-out areas are taken from the plot, occupied areas only from what is left of it (so a cut-out and an occupied
    /// area that overlap are counted once, as cut out). Only the parts inside the boundary count.
    /// </summary>
    public static PlotAreas Areas(
        IReadOnlyList<PlotPoint> boundary, IReadOnlyList<IReadOnlyList<PlotPoint>> excluded, IReadOnlyList<IReadOnlyList<PlotPoint>> occupied)
    {
        var total = Area(boundary);
        var mine = AreaWithout(boundary, excluded);
        var free = AreaWithout(boundary, excluded.Concat(occupied));
        return new PlotAreas(total, total - mine, mine - free, free);
    }

    /// <summary>
    /// Area in square pixels of the boundary with the blocked areas cut out. Only the part of a blocked area inside the
    /// boundary counts, and where blocked areas overlap, the overlap is taken off once.
    /// The plane is cut into vertical strips at every corner and every crossing of two edges; inside a strip nothing
    /// crosses, so the length of the free part of a vertical line changes evenly and its value in the middle of the strip
    /// times the width of the strip is exact.
    /// </summary>
    public static double AreaWithout(IReadOnlyList<PlotPoint> boundary, IEnumerable<IReadOnlyList<PlotPoint>> zones)
    {
        if (boundary.Count < 3) return 0;
        var cut = zones.Where(z => z.Count >= 3).ToList();
        var polygons = new List<IReadOnlyList<PlotPoint>> { boundary };
        polygons.AddRange(cut);

        var segments = new List<(PlotPoint A, PlotPoint B)>();
        foreach (var poly in polygons)
            for (var i = 0; i < poly.Count; i++)
                segments.Add((poly[i], poly[(i + 1) % poly.Count]));

        var xs = new SortedSet<double>();
        foreach (var (a, b) in segments)
        {
            xs.Add(a.X);
            xs.Add(b.X);
        }
        for (var i = 0; i < segments.Count; i++)
            for (var j = i + 1; j < segments.Count; j++)
                if (CrossingX(segments[i], segments[j]) is { } x) xs.Add(x);

        var positions = xs.ToList();
        var area = 0.0;
        for (var k = 0; k + 1 < positions.Count; k++)
        {
            var width = positions[k + 1] - positions[k];
            if (width <= 1e-12) continue;
            var middle = positions[k] + width / 2;

            var inside = Intervals(boundary, middle);
            var blocked = Merge(cut.SelectMany(z => Intervals(z, middle)));
            var free = 0.0;
            foreach (var (from, to) in inside)
            {
                var length = to - from;
                foreach (var (c0, c1) in blocked)
                    length -= Math.Max(0, Math.Min(to, c1) - Math.Max(from, c0));
                free += length;
            }
            area += free * width;
        }
        return area;
    }

    // Where the vertical line at x is inside the polygon: the y values of the edges it crosses, paired up (even-odd rule)
    private static List<(double From, double To)> Intervals(IReadOnlyList<PlotPoint> p, double x)
    {
        var ys = new List<double>();
        for (var i = 0; i < p.Count; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Count];
            if ((a.X < x && b.X > x) || (b.X < x && a.X > x))
                ys.Add(a.Y + (b.Y - a.Y) * (x - a.X) / (b.X - a.X));
        }
        ys.Sort();
        var result = new List<(double, double)>();
        for (var k = 0; k + 1 < ys.Count; k += 2) result.Add((ys[k], ys[k + 1]));
        return result;
    }

    // Overlapping intervals become one
    private static List<(double From, double To)> Merge(IEnumerable<(double From, double To)> intervals)
    {
        var result = new List<(double From, double To)>();
        foreach (var (from, to) in intervals.OrderBy(i => i.From))
        {
            if (result.Count > 0 && from <= result[^1].To) result[^1] = (result[^1].From, Math.Max(result[^1].To, to));
            else result.Add((from, to));
        }
        return result;
    }

    // x of the point where two edges cross each other; null if they do not (touching at a corner is not a crossing)
    private static double? CrossingX((PlotPoint A, PlotPoint B) s, (PlotPoint A, PlotPoint B) t)
    {
        var d1 = Side(t.A, t.B, s.A);
        var d2 = Side(t.A, t.B, s.B);
        var d3 = Side(s.A, s.B, t.A);
        var d4 = Side(s.A, s.B, t.B);
        if (d1 * d2 >= 0 || d3 * d4 >= 0) return null;
        return t.A.X + (t.B.X - t.A.X) * (d3 / (d3 - d4));
    }

    /// <summary>True if two edges that do not touch each other cross (the order of the corners is wrong).</summary>
    public static bool SelfIntersects(IReadOnlyList<PlotPoint> p)
    {
        var n = p.Count;
        if (n < 4) return false;
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
            {
                if (j == i + 1 || (i == 0 && j == n - 1)) continue; // neighbours share a corner
                if (Crosses(p[i], p[(i + 1) % n], p[j], p[(j + 1) % n])) return true;
            }
        return false;
    }

    private static double Side(PlotPoint a, PlotPoint b, PlotPoint c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool Crosses(PlotPoint a, PlotPoint b, PlotPoint c, PlotPoint d) =>
        Side(c, d, a) * Side(c, d, b) < 0 && Side(a, b, c) * Side(a, b, d) < 0;
}
