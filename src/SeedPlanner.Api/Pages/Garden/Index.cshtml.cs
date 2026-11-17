using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Garden;

// The plot of the garden: the user uploads a picture, marks the corners (a closed line), enters the length of some
// edges and gets the area; areas that cannot be used (house, paths) are drawn as well and taken off.
// The page works on the data as JSON: the browser draws and calculates while the user edits, the server checks and stores.
// The main page of the garden: the plot of the user with the beds on it. While no plot is defined the user is sent to the page to set it up.
public class IndexModel(SeedPlannerDbContext db, IStringLocalizer<SharedResource> L) : PageModel
{
    public const int MaxImageBytes = 5 * 1024 * 1024;
    private const int MaxPoints = 200;
    private const int MaxZones = 30;
    private const double MaxCoordinate = 100_000;
    private const int MaxPlacedBeds = 300;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public record ZoneData(string Name, string Kind, bool IsShape, double[][] Points, int Owner = 0, int SplitGroup = 0, int SplitPart = 0, double[][]? SplitParent = null, double[]? SplitLine = null,
        string Use = "", string Sun = "", string Soil = "", string Slope = "", bool Waterlogged = false, bool WaterNearby = false, bool ColdFramePossible = false);

    // What the script on the page starts with
    public record PageData(double[][] Boundary, double?[] Lengths, List<ZoneData> Zones, double? KnownArea, string[] Neighbors, CutLineData? CutLine, List<BedData> Beds, List<PlacedBedData> Placed);

    // A bed of the user (from the list of beds) and where it stands on the plot (centre in picture pixels, angle in degrees)
    public record BedData(int Id, string Name, string Shape, double WidthCm, double LengthCm, bool HasCover);
    public record PlacedBedData(int BedId, double X, double Y, double Angle);

    // The straight line through the plot (two points) and who owns the part before and after it (0 = the user, k = neighbour k)
    public record CutLineData(double[] A, double[] B, int Owner1 = 0, int Owner2 = 0);

    public bool HasImage { get; private set; }
    // The plot is defined: a boundary, a known scale, and nothing of the neighbours left (the same rule as plotDefined() in the script)
    public bool PlotReady { get; private set; }
    // The main page needs a defined plot; the setup page does not
    protected virtual bool RequirePlot => true;
    public long ImageVersion { get; private set; }
    public string DataJson { get; private set; } = "{}";

    public string? Message => TempData["GardenMessage"] as string;
    public string? Error => TempData["GardenError"] as string;

    public async Task<IActionResult> OnGetAsync()
    {
        // The picture itself is not loaded here (it is served by its own handler)
        var plot = await db.Plots.AsNoTracking()
            .Select(p => new
            {
                p.BoundaryJson,
                p.EdgeLengthsJson,
                p.KnownAreaM2,
                p.NeighborsJson,
                p.CutLineJson,
                p.PlacedBedsJson,
                HasImage = p.Image != null,
                p.UpdatedAt,
                Zones = p.Zones.OrderBy(z => z.Id).Select(z => new { z.Name, z.Kind, z.IsShape, z.PointsJson, z.Owner, z.SplitGroup, z.SplitPart, z.SplitParentJson, z.SplitLineJson, z.Use, z.Sun, z.Soil, z.Slope, z.Waterlogged, z.WaterNearby, z.ColdFramePossible }).ToList()
            })
            .FirstOrDefaultAsync();

        var beds = await db.RaisedBeds.AsNoTracking().OrderBy(b => b.Name).ThenBy(b => b.Id)
            .Select(b => new BedData(b.Id, b.Name, b.Shape.ToString(), (double)b.WidthCm, (double)b.LengthCm, b.HasCover))
            .ToListAsync();
        var data = new PageData([], [], [], null, [], null, beds, []);
        if (plot is not null)
        {
            HasImage = plot.HasImage;
            ImageVersion = plot.UpdatedAt.Ticks;
            var boundary = ParsePoints(plot.BoundaryJson) ?? [];
            var lengths = ParseLengths(plot.EdgeLengthsJson, boundary.Count);
            var zones = plot.Zones.Select(z => new ZoneData(z.Name, z.Kind.ToString(), z.IsShape, ToArrays(ParsePoints(z.PointsJson) ?? []), z.Owner, z.SplitGroup, z.SplitPart, ParentOf(z.SplitParentJson), LineOf(z.SplitLineJson),
                z.Use, z.Sun, z.Soil, z.Slope, z.Waterlogged, z.WaterNearby, z.ColdFramePossible)).ToList();
            var cutLine = ParseCutLine(plot.CutLineJson, ParseNeighbors(plot.NeighborsJson).Length);
            data = new PageData(ToArrays(boundary), lengths, zones, plot.KnownAreaM2, ParseNeighbors(plot.NeighborsJson), cutLine, beds, ParsePlaced(plot.PlacedBedsJson, beds.Select(b => b.Id)));
            var scaleKnown = plot.KnownAreaM2 > 0 || lengths.Any(l => l > 0);
            PlotReady = HasImage && boundary.Count >= 3 && scaleKnown && !zones.Any(z => z.Owner > 0) && !(cutLine is { } c && (c.Owner1 > 0 || c.Owner2 > 0));
        }
        DataJson = JsonSerializer.Serialize(data, Json);
        if (RequirePlot && !PlotReady)
        {
            TempData["GardenMessage"] = L["Garden_NeedSetup"].Value;
            return RedirectToPage("/Garden/Setup");
        }
        return Page();
    }

    // The picture of the signed-in user
    public async Task<IActionResult> OnGetImageAsync()
    {
        var plot = await db.Plots.AsNoTracking().Where(p => p.Image != null)
            .Select(p => new { p.Image, p.ImageContentType })
            .FirstOrDefaultAsync();
        if (plot?.Image is null) return NotFound();
        Response.Headers.CacheControl = "private, max-age=3600";
        return File(plot.Image, plot.ImageContentType ?? "application/octet-stream");
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? image)
    {
        if (image is null || image.Length == 0)
            return Fail(L["Garden_ErrNoFile"]);
        if (image.Length > MaxImageBytes)
            return Fail(L["Garden_ErrSize"]);

        using var memory = new MemoryStream();
        await image.CopyToAsync(memory);
        var bytes = memory.ToArray();
        // The kind of picture is taken from the content, not from what the browser says
        var type = ContentTypeOf(bytes);
        if (type is null)
            return Fail(L["Garden_ErrType"]);

        var plot = await db.Plots.FirstOrDefaultAsync();
        if (plot is null)
        {
            plot = new Plot();
            db.Plots.Add(plot);
        }
        plot.Image = bytes;
        plot.ImageContentType = type;
        plot.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["GardenMessage"] = L["Garden_Uploaded"].Value;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveAsync(string? boundaryJson, string? lengthsJson, string? zonesJson, string? knownArea, string? neighborsJson, string? cutLineJson, string? placedBedsJson)
    {
        var plot = await db.Plots.Include(p => p.Zones).FirstOrDefaultAsync();
        if (plot?.Image is null)
            return Fail(L["Garden_ErrNoImage"]);

        var boundary = ParsePoints(boundaryJson);
        List<ZoneData>? zones = null;
        try
        {
            zones = JsonSerializer.Deserialize<List<ZoneData>>(zonesJson ?? "[]", Json);
        }
        catch (JsonException)
        {
        }
        if (boundary is null || zones is null || zones.Count > MaxZones)
            return Fail(L["Garden_ErrData"]);

        var neighbors = ParseNeighbors(neighborsJson);
        var checkedZones = new List<(string Name, PlotZoneKind Kind, bool IsShape, List<PlotPoint> Points, int Owner, int SplitGroup, int SplitPart, string SplitParent, string SplitLine, ZoneData Source)>();
        foreach (var z in zones)
        {
            var points = ParsePoints(JsonSerializer.Serialize(z.Points));
            if (points is null) return Fail(L["Garden_ErrData"]);
            var name = (z.Name ?? "").Trim();
            var kind = Enum.TryParse<PlotZoneKind>(z.Kind, true, out var parsed) ? parsed : PlotZoneKind.Occupied;
            checkedZones.Add((name.Length == 0 ? L["Garden_Zone"].Value : name[..Math.Min(name.Length, 100)], kind, z.IsShape, points, z.Owner >= 0 && z.Owner <= neighbors.Length ? z.Owner : 0, SplitGroupOf(z), SplitPartOf(z), SplitParentOf(z), SplitLineOf(z), z));
        }

        plot.BoundaryJson = JsonSerializer.Serialize(ToArrays(boundary));
        plot.EdgeLengthsJson = JsonSerializer.Serialize(ParseLengths(lengthsJson, boundary.Count));
        // Area instead of edge lengths: only a number that makes sense is kept
        plot.KnownAreaM2 = double.TryParse(knownArea, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var area)
            && area > 0 && area < 100_000_000 ? Math.Round(area, 2) : null;
        plot.NeighborsJson = JsonSerializer.Serialize(neighbors);
        plot.CutLineJson = ParseCutLine(cutLineJson, neighbors.Length) is { } cut ? JsonSerializer.Serialize(cut, Json) : "";
        // Only beds of the user are kept, each one once; the positions have to be sensible numbers
        var bedIds = await db.RaisedBeds.Select(b => b.Id).ToListAsync();
        plot.PlacedBedsJson = JsonSerializer.Serialize(ParsePlaced(placedBedsJson, bedIds), Json);
        plot.UpdatedAt = DateTime.UtcNow;
        db.PlotZones.RemoveRange(plot.Zones);
        plot.Zones = checkedZones
            .Select(z => new PlotZone { Name = z.Name, Kind = z.Kind, Owner = z.Owner, SplitGroup = z.SplitGroup, SplitPart = z.SplitPart, SplitParentJson = z.SplitParent, SplitLineJson = z.SplitLine,
                Use = Pick(z.Source.Use, "Beds", "Lawn", "Path", "Trees", "Compost"), Sun = Pick(z.Source.Sun, "Full", "Partial", "Shade"),
                Soil = Pick(z.Source.Soil, "Sand", "Loam", "Clay", "Humus"), Slope = Pick(z.Source.Slope, "Flat", "Gentle", "Steep"),
                Waterlogged = z.Source.Waterlogged, WaterNearby = z.Source.WaterNearby, ColdFramePossible = z.Source.ColdFramePossible, IsShape = z.IsShape, PointsJson = JsonSerializer.Serialize(ToArrays(z.Points)) })
            .ToList();
        await db.SaveChangesAsync();

        TempData["GardenMessage"] = L["Garden_Saved"].Value;
        return RedirectToPage();
    }

    private static List<PlacedBedData> ParsePlaced(string? json, IEnumerable<int> allowedIds)
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

    private IActionResult Fail(string message)
    {
        TempData["GardenError"] = message;
        return RedirectToPage();
    }

    // "image/png", "image/jpeg" or "image/webp" from the first bytes; null for anything else
    private static string? ContentTypeOf(byte[] b)
    {
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "image/webp";
        return null;
    }

    // [[x,y],...] to points; null if it is not that, has too many corners or numbers that make no sense
    private static List<PlotPoint>? ParsePoints(string? json)
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
    private static double?[] ParseLengths(string? json, int edges)
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

    private const int MaxNeighbors = 10;

    // A text from the page is only taken if it is one of the known values
    private static string Pick(string? value, params string[] allowed) => value is not null && allowed.Contains(value) ? value : "";

    // The split of an area (see PlotZone.SplitGroup): only a complete and sensible one is kept
    private static bool HasSplit(ZoneData z) =>
        z.SplitGroup is > 0 and < 1000 && z.SplitPart is 1 or 2 && ParentOf(z.SplitParent) is not null && z.SplitLine is { Length: 3 } l && l.All(v => double.IsFinite(v) && Math.Abs(v) <= MaxCoordinate);

    private static int SplitGroupOf(ZoneData z) => HasSplit(z) ? z.SplitGroup : 0;
    private static int SplitPartOf(ZoneData z) => HasSplit(z) ? z.SplitPart : 0;
    private static string SplitParentOf(ZoneData z) => HasSplit(z) ? JsonSerializer.Serialize(z.SplitParent) : "";
    private static string SplitLineOf(ZoneData z) => HasSplit(z) ? JsonSerializer.Serialize(z.SplitLine) : "";

    private static double[][]? ParentOf(double[][]? points) => points is { Length: >= 3 } && ParsePoints(JsonSerializer.Serialize(points)) is not null ? points : null;

    private static double[][]? ParentOf(string? json) => ParentOf(ParseArrays(json));

    private static double[]? LineOf(string? json)
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

    private static double[][]? ParseArrays(string? json)
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
    private static CutLineData? ParseCutLine(string? json, int neighbors)
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
    private static string[] ParseNeighbors(string? json)
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

    private static double[][] ToArrays(IEnumerable<PlotPoint> points) =>
        points.Select(p => new[] { Math.Round(p.X, 1), Math.Round(p.Y, 1) }).ToArray();
}
