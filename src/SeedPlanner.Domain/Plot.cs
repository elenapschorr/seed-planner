namespace SeedPlanner.Domain;

/// <summary>
/// The plot of a user's garden: a picture (aerial photo or sketch), the boundary marked on it and areas that cannot be
/// used. One per user. The points are in pixels of the picture, the real size comes from the edge lengths the user enters.
/// </summary>
public class Plot : IOwnedByUser
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";

    public byte[]? Image { get; set; }
    public string? ImageContentType { get; set; }

    /// <summary>The corners of the boundary in the order of the closed line: <c>[[x,y],...]</c>.</summary>
    public string BoundaryJson { get; set; } = "[]";

    /// <summary>Length in meters of every edge (corner i to corner i+1, the last one closes the line), empty = not known: <c>[12.5,null,...]</c>.</summary>
    public string EdgeLengthsJson { get; set; } = "[]";

    /// <summary>Area of the whole plot in square meters, if the user entered it instead of edge lengths (it then gives the scale of the picture).</summary>
    public double? KnownAreaM2 { get; set; }

    /// <summary>Names of the neighbours the user defined: <c>["Meyer","Schmidt"]</c>. An area owned by neighbour k (<see cref="PlotZone.Owner"/> = k) is cut out of the user's plot and counted for that neighbour.</summary>
    public string NeighborsJson { get; set; } = "[]";

    /// <summary>The straight line the user drew through the plot to see the size of both parts: <c>[[x,y],[x,y]]</c>, empty = none.</summary>
    public string CutLineJson { get; set; } = "";

    /// <summary>The beds the user placed on the plot: <c>[{"bedId":3,"x":120.5,"y":80,"angle":15}]</c> (position of the centre in pixels of the picture, angle in degrees). Each bed is placed at most once.</summary>
    public string PlacedBedsJson { get; set; } = "[]";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<PlotZone> Zones { get; set; } = new();
}

/// <summary>What a drawn area means for the plot.</summary>
public enum PlotZoneKind
{
    /// <summary>Other area that is taken (built on or used otherwise) and belongs to the user: counts to the plot, not to the garden area. The first kind there was, kept for stored areas.</summary>
    Occupied,

    /// <summary>Does not belong to the user (neighbour's half of a shared garage): cut out of the plot.</summary>
    Excluded,

    /// <summary>Belongs to the user; only marks the area, the numbers do not change.</summary>
    Mine,

    /// <summary>House: built on, not garden area.</summary>
    House,

    /// <summary>Terrace: built on, not garden area.</summary>
    Terrace,

    /// <summary>Garage: built on, not garden area.</summary>
    Garage,

    /// <summary>Parking space: not garden area.</summary>
    Parking,

    /// <summary>Garden area (there can be several separate ones): once there is one, only garden areas count as available.</summary>
    Garden,

    /// <summary>Pond: taken, not garden area.</summary>
    Pond,

    /// <summary>Garden house or shed: built on, not garden area.</summary>
    Shed
}

/// <summary>An area drawn into the plot that is cut out of it or marked as occupied (see <see cref="PlotZoneKind"/>).</summary>
public class PlotZone
{
    public int Id { get; set; }
    public int PlotId { get; set; }
    public Plot? Plot { get; set; }
    public required string Name { get; set; }
    public PlotZoneKind Kind { get; set; } = PlotZoneKind.Occupied;

    /// <summary>Who owns the area: 0 = the user, k = the k-th neighbour of <see cref="Plot.NeighborsJson"/> (counted from 1).</summary>
    public int Owner { get; set; }

    /// <summary>Areas that came from splitting one area by a line share a number (0 = not split). Together with the other fields below the split stays editable: the line can be moved and the split undone.</summary>
    public int SplitGroup { get; set; }

    /// <summary>1 = the part on the side of the line's start (below), 2 = the other part; 0 = not split.</summary>
    public int SplitPart { get; set; }

    /// <summary>The area before it was split: <c>[[x,y],...]</c>, empty = not split.</summary>
    public string SplitParentJson { get; set; } = "";

    /// <summary>The line of the split: <c>[nx,ny,t0]</c> (unit normal and position, as in the page script), empty = not split.</summary>
    public string SplitLineJson { get; set; } = "";

    // What the user knows about the area, to place beds on it later. Empty = not said.

    /// <summary>What the area is used for: Beds, Lawn, Path, Trees or Compost.</summary>
    public string Use { get; set; } = "";

    /// <summary>Light: Full, Partial or Shade.</summary>
    public string Sun { get; set; } = "";

    /// <summary>Soil: Sand, Loam, Clay or Humus.</summary>
    public string Soil { get; set; } = "";

    /// <summary>Slope: Flat, Gentle or Steep.</summary>
    public string Slope { get; set; } = "";

    /// <summary>Water stands on the area after rain.</summary>
    public bool Waterlogged { get; set; }

    /// <summary>A water tap or similar is close.</summary>
    public bool WaterNearby { get; set; }

    /// <summary>A cold frame (covered bed) can be put here.</summary>
    public bool ColdFramePossible { get; set; }

    /// <summary>A ready-made shape (rectangle, circle, ...): it is moved as a whole, its corners are not changed one by one.</summary>
    public bool IsShape { get; set; }

    /// <summary>The corners in pixels of the picture: <c>[[x,y],...]</c>.</summary>
    public string PointsJson { get; set; } = "[]";
}
