namespace SeedPlanner.Domain;

/// <summary>How much area of the garden is still free for new beds, from the plot the user drew.</summary>
public static class GardenArea
{
    /// <summary>The kinds of areas that are built on or taken (not garden area).</summary>
    public static readonly IReadOnlySet<PlotZoneKind> Built = new HashSet<PlotZoneKind>
    {
        PlotZoneKind.House, PlotZoneKind.Shed, PlotZoneKind.Terrace, PlotZoneKind.Garage,
        PlotZoneKind.Parking, PlotZoneKind.Pond, PlotZoneKind.Occupied
    };

    /// <summary>
    /// The area in m² of the garden areas the user marked on the plot (without what is built on or belongs to a neighbour).
    /// Null if no garden area is marked, there is no boundary, or the scale of the picture is not known.
    /// </summary>
    public static double? TotalSquareMeters(Plot plot)
    {
        var boundary = PlotInput.ParsePoints(plot.BoundaryJson) ?? [];
        if (boundary.Count < 3) return null;

        var metersPerPixel = PlotGeometry.MetersPerPixel(boundary, PlotInput.ParseLengths(plot.EdgeLengthsJson, boundary.Count));
        if (metersPerPixel is null && plot.KnownAreaM2 > 0) metersPerPixel = Math.Sqrt(plot.KnownAreaM2.Value / PlotGeometry.Area(boundary));
        if (metersPerPixel is not { } scale) return null;

        List<IReadOnlyList<PlotPoint>> Points(Func<PlotZone, bool> test) =>
            plot.Zones.Where(test).Select(z => (IReadOnlyList<PlotPoint>)(PlotInput.ParsePoints(z.PointsJson) ?? [])).Where(p => p.Count >= 3).ToList();

        var garden = Points(z => z.Owner == 0 && z.Kind == PlotZoneKind.Garden);
        if (garden.Count == 0) return null;
        var blocked = Points(z => z.Owner > 0).Concat(Points(z => z.Owner == 0 && Built.Contains(z.Kind))).ToList();

        var free = PlotGeometry.AreaWithout(boundary, blocked);
        var outsideGarden = PlotGeometry.AreaWithout(boundary, blocked.Concat(garden));
        return Math.Max(0, free - outsideGarden) * scale * scale;
    }

    /// <summary>
    /// The garden area that is still free for new beds: the garden area minus the area of the beds the user already has.
    /// Null if the garden area is not known (see <see cref="TotalSquareMeters"/>).
    /// </summary>
    public static double? FreeSquareMeters(Plot? plot, IEnumerable<RaisedBed> existingBeds) =>
        plot is null || TotalSquareMeters(plot) is not { } total ? null : Math.Max(0, total - existingBeds.Sum(b => b.AreaSquareMeters));
}
