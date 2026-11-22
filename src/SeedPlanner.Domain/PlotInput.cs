using System.Text.Json;

namespace SeedPlanner.Domain;

// Reads and checks what the garden page sends (JSON texts): everything that comes from the browser is checked here before it is stored.
// Anything that makes no sense gives null or an empty result, never an exception.
public static class PlotInput
{
    public const int MaxZones = 30;
    public const int MaxNeighbors = 10;
    private const int MaxPoints = 200;
    private const double MaxCoordinate = 100_000;
    private const int MaxPlacedBeds = 300;

    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static List<PlacedBedData> ParsePlaced(string? json, IEnumerable<int> allowedIds)
    {
        var result = new List<PlacedBedData>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        List<PlacedBedData>? list = null;
        try
        {
            list = JsonSerializer.Deserialize<List<PlacedBedData>>(json, Json);
        }
        catch (JsonException)
        {
        }
        var allowed = allowedIds.ToHashSet();
        foreach (var b in list ?? [])
        {
            if (result.Count >= MaxPlacedBeds) break;
            if (!allowed.Contains(b.BedId) || result.Any(r => r.BedId == b.BedId)) continue;
            if (!double.IsFinite(b.X) || !double.IsFinite(b.Y) || !double.IsFinite(b.Angle) || Math.Abs(b.X) > MaxCoordinate || Math.Abs(b.Y) > MaxCoordinate) continue;
            result.Add(new PlacedBedData(b.BedId, Math.Round(b.X, 1), Math.Round(b.Y, 1), Math.Round(((b.Angle % 360) + 360) % 360, 1)));
        }
        return result;
    }


    // "image/png", "image/jpeg" or "image/webp" from the first bytes; null for anything else
    public static string? ContentTypeOf(byte[] b)
    {
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "image/webp";
        return null;
    }

    // [[x,y],...] to points; null if it is not that, has too many corners or numbers that make no sense
    public static List<PlotPoint>? ParsePoints(string? json)
    {
        double[][]? raw;
        try
        {
            raw = JsonSerializer.Deserialize<double[][]>(string.IsNullOrWhiteSpace(json) ? "[]" : json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
        if (raw is null || raw.Length > MaxPoints) return null;
        var points = new List<PlotPoint>();
        foreach (var p in raw)
        {
            if (p is null || p.Length != 2) return null;
            if (p.Any(v => double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > MaxCoordinate)) return null;
            points.Add(new PlotPoint(p[0], p[1]));
        }
        return points;
    }

    // One entry per edge: the length in meters, or null if not known or not usable
    public static double?[] ParseLengths(string? json, int edges)
    {
        double?[]? raw = null;
        try
        {
            raw = JsonSerializer.Deserialize<double?[]>(string.IsNullOrWhiteSpace(json) ? "[]" : json, Json);
        }
        catch (JsonException)
        {
        }
        var result = new double?[edges];
        for (var i = 0; i < edges && raw is not null && i < raw.Length; i++)
            result[i] = raw[i] is { } v && v > 0 && v < MaxCoordinate && !double.IsNaN(v) ? Math.Round(v, 3) : null;
        return result;
    }

    // A text from the page is only taken if it is one of the known values
    public static string Pick(string? value, params string[] allowed) => value is not null && allowed.Contains(value) ? value : "";

    // The split of an area (see PlotZone.SplitGroup): only a complete and sensible one is kept
    public static bool HasSplit(ZoneData z) =>
        z.SplitGroup is > 0 and < 1000 && z.SplitPart is 1 or 2 && ParentOf(z.SplitParent) is not null && z.SplitLine is { Length: 3 } l && l.All(v => double.IsFinite(v) && Math.Abs(v) <= MaxCoordinate);

    public static int SplitGroupOf(ZoneData z) => HasSplit(z) ? z.SplitGroup : 0;
    public static int SplitPartOf(ZoneData z) => HasSplit(z) ? z.SplitPart : 0;
    public static string SplitParentOf(ZoneData z) => HasSplit(z) ? JsonSerializer.Serialize(z.SplitParent) : "";
    public static string SplitLineOf(ZoneData z) => HasSplit(z) ? JsonSerializer.Serialize(z.SplitLine) : "";

    public static double[][]? ParentOf(double[][]? points) => points is { Length: >= 3 } && ParsePoints(JsonSerializer.Serialize(points)) is not null ? points : null;

    public static double[][]? ParentOf(string? json) => ParentOf(ParseArrays(json));

    public static double[]? LineOf(string? json)
    {
        try
        {
            var v = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<double[]>(json, Json);
            return v is { Length: 3 } ? v : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static double[][]? ParseArrays(string? json)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<double[][]>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The straight line through the plot: two sensible points and owners that exist; null for anything else (older data had just the two points)
    public static CutLineData? ParseCutLine(string? json, int neighbors)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        CutLineData? line = null;
        try
        {
            line = JsonSerializer.Deserialize<CutLineData>(json, Json);
        }
        catch (JsonException)
        {
            if (ParseArrays(json) is { Length: 2 } legacy)
                line = new CutLineData(legacy[0], legacy[1]);
        }
        static bool Fine(double[]? p) => p is { Length: 2 } && p.All(v => double.IsFinite(v) && Math.Abs(v) <= MaxCoordinate);
        if (line is null || !Fine(line.A) || !Fine(line.B)) return null;
        static double[] Round(double[] p) => [Math.Round(p[0], 1), Math.Round(p[1], 1)];
        int Owner(int o) => o >= 0 && o <= neighbors ? o : 0;
        return new CutLineData(Round(line.A), Round(line.B), Owner(line.Owner1), Owner(line.Owner2));
    }

    // The names of the neighbours: at most 10, trimmed and not too long; an empty name stays (the page shows "Neighbour k" for it)
    public static string[] ParseNeighbors(string? json)
    {
        string[]? raw = null;
        try
        {
            raw = JsonSerializer.Deserialize<string[]>(string.IsNullOrWhiteSpace(json) ? "[]" : json, Json);
        }
        catch (JsonException)
        {
        }
        return (raw ?? []).Take(MaxNeighbors).Select(n => (n ?? "").Trim()).Select(n => n[..Math.Min(n.Length, 100)]).ToArray();
    }

    public static double[][] ToArrays(IEnumerable<PlotPoint> points) =>
        points.Select(p => new[] { Math.Round(p.X, 1), Math.Round(p.Y, 1) }).ToArray();
}
