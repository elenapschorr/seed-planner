using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SeedPlanner.Domain;
using static SeedPlanner.Domain.PlotInput;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Garden;

// The plot of the garden: the user uploads a picture, marks the corners (a closed line), enters the length of some
// edges and gets the area; areas that cannot be used (house, paths) are drawn as well and taken off.
// The page works on the data as JSON: the browser draws and calculates while the user edits, the server checks and stores.
// The main page of the garden: the plot of the user with the beds on it. While no plot is defined the user is sent to the page to set it up.
public class IndexModel(SeedPlannerDbContext db, IStringLocalizer<SharedResource> L) : PageModel
{
    public const int MaxImageBytes = 5 * 1024 * 1024;

    // What the script on the page starts with
    public record PageData(double[][] Boundary, double?[] Lengths, List<ZoneData> Zones, double? KnownArea, string[] Neighbors, CutLineData? CutLine, List<BedData> Beds, List<PlacedBedData> Placed);

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

    private IActionResult Fail(string message)
    {
        TempData["GardenError"] = message;
        return RedirectToPage();
    }
}
