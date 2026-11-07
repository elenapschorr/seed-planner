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

public enum BedShape
{
    Oval,
    Rectangular
}

/// <summary>
/// A group of identical pots. Pots that are stacked into a tower (e.g. strawberry pots)
/// are counted as single pots; SetSize says how many pots make one tower.
/// </summary>
public class Pot : IOwnedByUser
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";

    public PotKind Kind { get; set; } = PotKind.Pot;
    public PotShape Shape { get; set; } = PotShape.Round;

    /// <summary>Diameter (round) or edge length (rectangular) at the top, in cm.</summary>
    public decimal? TopCm { get; set; }

    /// <summary>Diameter (round) or edge length (rectangular) at the bottom, in cm.</summary>
    public decimal? BottomCm { get; set; }

    public decimal? HeightCm { get; set; }

    /// <summary>All pots of this group, free and in use.</summary>
    public int Quantity { get; set; }

    /// <summary>Pots per tower or set; 1 for single pots.</summary>
    public int SetSize { get; set; } = 1;

    public string? Color { get; set; }

    /// <summary>Number printed on the pot (manufacturer's size).</summary>
    public string? Code { get; set; }

    public string? Note { get; set; }

    public int? SaucerId { get; set; }
    public Saucer? Saucer { get; set; }

    public List<PotUse> Uses { get; set; } = new();

    /// <summary>Pots that hold a plant.</summary>
    public int OccupiedQuantity => Uses.Sum(u => u.Quantity);

    public int FreeQuantity => Math.Max(0, Quantity - OccupiedQuantity);

    /// <summary>More pots are recorded as in use than the group has.</summary>
    public bool IsOverbooked => OccupiedQuantity > Quantity;

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

/// <summary>Some of the pots of a group are in use for a plant type.</summary>
public class PotUse
{
    public int Id { get; set; }
    public int PotId { get; set; }
    public Pot? Pot { get; set; }

    public int PlantTypeId { get; set; }
    public PlantType? PlantType { get; set; }

    /// <summary>How many pots of the group hold this plant type (single pots, not towers).</summary>
    public int Quantity { get; set; }

    public string? Note { get; set; }
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

/// <summary>One raised bed. Several beds of the same size are several rows.</summary>
public class RaisedBed : IOwnedByUser
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";

    public string Name { get; set; } = "";
    public BedShape Shape { get; set; } = BedShape.Oval;
    public decimal WidthCm { get; set; }
    public decimal LengthCm { get; set; }
    public decimal HeightCm { get; set; }
    public string? Note { get; set; }

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

    /// <summary>Soil volume in litres, assuming straight walls.</summary>
    public double VolumeLiters => AreaSquareMeters * (double)HeightCm * 10;
}
