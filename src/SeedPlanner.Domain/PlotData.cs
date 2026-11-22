namespace SeedPlanner.Domain;

// What the garden page sends to the server and gets back (as JSON): the areas of the plot, the beds on it and the line through the plot
public record ZoneData(string Name, string Kind, bool IsShape, double[][] Points, int Owner = 0, int SplitGroup = 0, int SplitPart = 0, double[][]? SplitParent = null, double[]? SplitLine = null,
string Use = "", string Sun = "", string Soil = "", string Slope = "", bool Waterlogged = false, bool WaterNearby = false, bool ColdFramePossible = false);

// A bed of the user (from the list of beds) and where it stands on the plot (centre in picture pixels, angle in degrees)
public record BedData(int Id, string Name, string Shape, double WidthCm, double LengthCm, bool HasCover);
public record PlacedBedData(int BedId, double X, double Y, double Angle);

// The straight line through the plot (two points) and who owns the part before and after it (0 = the user, k = neighbour k)
public record CutLineData(double[] A, double[] B, int Owner1 = 0, int Owner2 = 0);
