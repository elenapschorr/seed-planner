namespace SeedPlanner.Domain;

/// <summary>Personal data that belongs to one user. The owner is set automatically when the row is saved.</summary>
public interface IOwnedByUser
{
    string OwnerId { get; set; }
}

public enum PotKind
{
    Pot,
    Bowl
}

/// <summary>Round pots are conical (wider at the top); rectangular pots are assumed to have a square cross section.</summary>
public enum PotShape
{
    Round,
    Rectangular
}

/// <summary>A raised bed has walls and a height; a ground bed is planted directly into the soil.</summary>
public enum BedKind
{
    Raised,
    Ground
}

public enum BedShape
{
    Oval,
    Rectangular
}

/// <summary>
/// A group of identical pots: the size, the colour and the saucer are stored once for the group.
/// The pots themselves are single rows (see <see cref="Pot"/>). Pots that are stacked into a tower
/// (e.g. strawberry pots) are counted as single pots; SetSize says how many pots make one tower.
/// </summary>
public class PotGroup : IOwnedByUser
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";

    public string Name { get; set; } = "";

    public PotKind Kind { get; set; } = PotKind.Pot;
    public PotShape Shape { get; set; } = PotShape.Round;

    /// <summary>Diameter (round) or edge length (rectangular) at the top, in cm.</summary>
    public decimal? TopCm { get; set; }

    /// <summary>Diameter (round) or edge length (rectangular) at the bottom, in cm.</summary>
    public decimal? BottomCm { get; set; }

    public decimal? HeightCm { get; set; }

    /// <summary>Pots per tower or set; 1 for single pots.</summary>
    public int SetSize { get; set; } = 1;

    public string? Color { get; set; }

    /// <summary>Number printed on the pot (manufacturer's size).</summary>
    public string? Code { get; set; }

    public string? Note { get; set; }

    public int? SaucerId { get; set; }
    public Saucer? Saucer { get; set; }

    public List<Pot> Pots { get; set; } = new();

    /// <summary>All pots of the group, free and in use.</summary>
    public int Quantity => Pots.Count;

    /// <summary>Pots that hold at least one plant.</summary>
    public int OccupiedQuantity => Pots.Count(p => p.Plantings.Count > 0);

    public int FreeQuantity => Quantity - OccupiedQuantity;

    /// <summary>Complete towers or sets.</summary>
    public int SetCount => Quantity / Math.Max(1, SetSize);

    /// <summary>
    /// Volume of one pot in litres. Round pots are a truncated cone, rectangular pots a truncated
    /// pyramid with a square cross section. Without all three measures there is no volume.
    /// </summary>
    public double? VolumeLiters
    {
        get
        {
            if (TopCm is null || BottomCm is null || HeightCm is null) return null;

            var top = (double)TopCm.Value;
            var bottom = (double)BottomCm.Value;
            var height = (double)HeightCm.Value;

            // Area of the top and the bottom surface in cm²
            var (topArea, bottomArea) = Shape == PotShape.Round
                ? (Math.PI * top * top / 4, Math.PI * bottom * bottom / 4)
                : (top * top, bottom * bottom);

            var cubicCm = height / 3 * (topArea + Math.Sqrt(topArea * bottomArea) + bottomArea);
            return cubicCm / 1000;
        }
    }
}

/// <summary>One single pot. It belongs to a group and holds no, one or several plantings.</summary>
public class Pot
{
    public int Id { get; set; }
    public int PotGroupId { get; set; }
    public PotGroup? PotGroup { get; set; }

    public string? Note { get; set; }

    public List<Planting> Plantings { get; set; } = new();

    public bool IsFree => Plantings.Count == 0;
}

/// <summary>
/// Something that grows: a plant type, optionally a concrete variety, and how many plants.
/// It stands in one pot or in one bed (or nowhere yet). Several plants in the same pot are several rows;
/// the same variety in several pots is a row per pot.
/// </summary>
public class Planting : IOwnedByUser
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";

    public int PlantTypeId { get; set; }
    public PlantType? PlantType { get; set; }

    /// <summary>The variety (one of the user's plants), if known. It always belongs to PlantTypeId.</summary>
    public int? PlantId { get; set; }
    public Plant? Plant { get; set; }

    /// <summary>Number of plants; empty if not counted.</summary>
    public int? PlantCount { get; set; }

    public string? Note { get; set; }

    public int? PotId { get; set; }
    public Pot? Pot { get; set; }

    public int? RaisedBedId { get; set; }
    public RaisedBed? RaisedBed { get; set; }
}

public class Saucer : IOwnedByUser
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";

    public decimal DiameterCm { get; set; }
    public string? Color { get; set; }
    public int Quantity { get; set; }
    public string? Note { get; set; }
}

/// <summary>One bed, raised or in the ground. Several beds of the same size are several rows.</summary>
public class RaisedBed : IOwnedByUser
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";

    public string Name { get; set; } = "";
    public BedKind Kind { get; set; } = BedKind.Raised;
    public BedShape Shape { get; set; } = BedShape.Oval;
    public decimal WidthCm { get; set; }
    public decimal LengthCm { get; set; }

    /// <summary>Height of the walls; empty for a ground bed.</summary>
    public decimal? HeightCm { get; set; }

    /// <summary>The bed has a cover (cold frame, hood). Plants for the cold frame only go into such beds.</summary>
    public bool HasCover { get; set; }
    public string? Note { get; set; }

    public List<Planting> Plantings { get; set; } = new();

    /// <summary>Planting area in m². An oval is an ellipse with the width and the length as its axes.</summary>
    public double AreaSquareMeters
    {
        get
        {
            var area = (double)WidthCm * (double)LengthCm;
            if (Shape == BedShape.Oval) area *= Math.PI / 4;
            return area / 10_000;
        }
    }

    /// <summary>Soil volume in litres, assuming straight walls. A ground bed has no volume.</summary>
    public double? VolumeLiters => HeightCm is null ? null : AreaSquareMeters * (double)HeightCm.Value * 10;
}
