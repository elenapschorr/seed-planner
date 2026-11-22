using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;

namespace SeedPlanner.Api.Pages.Plans;

/// <summary>
/// The distribution of a plan over the chosen pots and beds: collects the input from the database, runs the
/// <see cref="PlanAllocator"/>, keeps the result with the plan and calculates it again when something it was
/// calculated from has changed (plan numbers, chosen pots and beds, what stands in them, the varieties).
/// </summary>
public static class PlanAllocation
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Everything the allocator needs, and a fingerprint of it.</summary>
    public sealed record Prepared(
        List<AllocPlant> Plants,
        List<AllocContainerInput> Containers,
        List<PlantTypeCompanion> Companions,
        List<(int Main, int Secondary)> Partners,
        string Hash,
        double FreeGardenM2);

    // What the fingerprint is made of: every input of the calculation
    private sealed record Fingerprint(
        ShortageStrategy Shortage, NoCoverStrategy NoCover, double MaxBed, double FreeGarden, bool AllowOthers,
        List<AllocPlant> Plants, List<AllocContainerInput> Containers, List<string> Companions,
        List<string> Partners, int Version = 5); // raised when the result gets new content, so stored results are calculated again

    /// <summary>
    /// The garden area in m² that is still free for new beds (garden areas of the plot minus the existing beds); null if the user has
    /// not set a garden area on the plot (or not its scale).
    /// </summary>
    public static async Task<double?> FreeGardenAsync(SeedPlannerDbContext db)
    {
        var plot = await db.Plots.AsNoTracking().Include(p => p.Zones).FirstOrDefaultAsync();
        return GardenArea.FreeSquareMeters(plot, await db.RaisedBeds.AsNoTracking().ToListAsync());
    }

    // The size of one new bed: what the user set, otherwise as big as the free garden area
    private static double EffectiveMaxBed(Plan plan, double freeGarden) =>
        plan.MaxBedAreaSquareMeters > 0 ? Math.Min(plan.MaxBedAreaSquareMeters, freeGarden) : freeGarden;

    /// <summary>The plan has to be loaded with its items, their lines (with the varieties) and the chosen containers.</summary>
    public static async Task<Prepared> PrepareAsync(SeedPlannerDbContext db, Plan plan)
    {
        // Main or secondary plant: the suggestion of the plant type, overridden by the user's own choice
        var roles = await db.PlantTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Role);
        foreach (var own in await db.UserPlantRoles.AsNoTracking().ToListAsync())
            roles[own.PlantTypeId] = own.Role;

        var plants = plan.Items
            .SelectMany(i => i.Lines.Select(l => (Line: l, Life: i.PlantType?.Lifecycle, Type: i.PlantType, Timing: i.SowingTiming)))
            .Where(x => x.Line.Plants > 0 && x.Line.Plant is not null)
            .Select(x => ToPlant(x.Line.Plant!, x.Line.Plants, plan.SpacingPercent, x.Life, plan.LastFrostThird, x.Type, x.Timing,
                x.Line.Plant!.Role ?? roles.GetValueOrDefault(x.Line.Plant!.PlantTypeId)))
            .OrderBy(p => p.PlantId)
            .ToList();

        var containers = new List<AllocContainerInput>();
        var categories = await db.PlantTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Category);

        // What this plan planted earlier does not count as occupied: the plan can be transferred again.
        // Plantings of plans of other periods do not count either; plantings made by hand always do.
        var (validFrom, validTo) = (plan.ValidFrom, plan.ValidTo);
        var potIds = plan.Containers.Where(c => c.PotId is not null).Select(c => c.PotId!.Value).ToList();
        var pots = await db.Pots
            .Include(p => p.PotGroup).ThenInclude(g => g!.Pots)
            .Include(p => p.Plantings.Where(x => x.PlanId != plan.Id && (x.PlanId == null || (x.Plan!.ValidFrom <= validTo && x.Plan.ValidTo >= validFrom)))).ThenInclude(x => x.Plant)
            .Where(p => potIds.Contains(p.Id))
            .ToListAsync();
        foreach (var pot in pots.OrderBy(p => p.Id))
            containers.Add(ToPotInput(pot, categories));

        var bedIds = plan.Containers.Where(c => c.RaisedBedId is not null).Select(c => c.RaisedBedId!.Value).ToList();
        var beds = await db.RaisedBeds
            .Include(b => b.Plantings.Where(x => x.PlanId != plan.Id && (x.PlanId == null || (x.Plan!.ValidFrom <= validTo && x.Plan.ValidTo >= validFrom)))).ThenInclude(x => x.Plant)
            .Where(b => bedIds.Contains(b.Id))
            .ToListAsync();
        foreach (var bed in beds.OrderBy(b => b.Id))
            containers.Add(ToBedInput(bed, categories));

        var companions = await db.PlantTypeCompanions.AsNoTracking().OrderBy(c => c.Id).ToListAsync();

        // The partners of the main plants: the good neighbours with the other role, changed by the user
        var changes = (await db.UserPlantPartners.AsNoTracking().ToListAsync())
            .Select(c => new PlantPairs.PartnerChange(c.MainPlantTypeId, c.SecondaryPlantTypeId, c.Included));
        var partners = PlantPairs.MainPartners(roles, companions, changes)
            .SelectMany(m => m.Value.Select(s => (Main: m.Key, Secondary: s)))
            .OrderBy(x => x.Main).ThenBy(x => x.Secondary)
            .ToList();

        // New beds only go onto the free garden area of the plot; without one there are none
        var freeGarden = await FreeGardenAsync(db) ?? 0;

        var print = new Fingerprint(
            plan.Shortage, plan.NoCover, plan.MaxBedAreaSquareMeters, freeGarden, plan.AllowOthersInCoveredBeds,
            plants, containers,
            companions.Select(c => $"{c.PlantTypeId}:{c.OtherPlantTypeId}:{c.Relation}").ToList(),
            partners.Select(x => $"{x.Main}:{x.Secondary}").ToList());
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(print, Json))));

        return new Prepared(plants, containers, companions, partners, hash, freeGarden);
    }

    /// <summary>
    /// The distribution of the plan: the stored one if nothing has changed since, otherwise a new one (stored).
    /// Without chosen pots and beds there is none.
    /// </summary>
    public static async Task<AllocationResult?> EnsureAsync(SeedPlannerDbContext db, Plan plan)
    {
        if (plan.Containers.Count == 0)
        {
            if (plan.AllocationJson is not null)
            {
                plan.AllocationJson = null;
                plan.AllocationHash = null;
                plan.AllocatedAt = null;
                await db.SaveChangesAsync();
            }
            return null;
        }

        var prepared = await PrepareAsync(db, plan);
        if (plan.AllocationHash == prepared.Hash && plan.AllocationJson is not null)
            return Read(plan);

        var result = PlanAllocator.Allocate(
            prepared.Plants, prepared.Containers, prepared.Companions,
            plan.Shortage, EffectiveMaxBed(plan, prepared.FreeGardenM2), plan.NoCover, plan.AllowOthersInCoveredBeds, prepared.Partners,
            prepared.FreeGardenM2);

        plan.AllocationJson = JsonSerializer.Serialize(result, Json);
        plan.AllocationHash = prepared.Hash;
        plan.AllocatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return result;
    }

    /// <summary>The stored distribution, if there is one.</summary>
    public static AllocationResult? Read(Plan plan) =>
        string.IsNullOrEmpty(plan.AllocationJson) ? null : JsonSerializer.Deserialize<AllocationResult>(plan.AllocationJson, Json);

    /// <summary>
    /// Transfers the distribution as plantings into the existing pots and beds. What an earlier transfer of this plan
    /// planted is replaced. New pots and beds do not exist yet, so nothing is planted there. A bed that needs a cover
    /// gets it. Returns the number of plantings.
    /// </summary>
    public static async Task<int> ApplyAsync(SeedPlannerDbContext db, Plan plan, AllocationResult result)
    {
        var earlier = await db.Plantings.Where(x => x.PlanId == plan.Id).ToListAsync();
        db.Plantings.RemoveRange(earlier);

        var coverIds = result.Containers
            .Where(c => c.Kind == ContainerKind.Bed && c.Id is not null && c.NeedsCover)
            .Select(c => c.Id!.Value).ToList();
        if (coverIds.Count > 0)
            foreach (var bed in await db.RaisedBeds.Where(b => coverIds.Contains(b.Id)).ToListAsync())
                bed.HasCover = true;

        var created = 0;
        foreach (var container in result.Containers.Where(c => !c.IsNew && c.Id is not null))
        {
            // One row per variety in a pot or bed, main plants and under-planting together
            foreach (var group in container.Placements.GroupBy(p => (p.PlantId, p.PlantTypeId)))
            {
                db.Plantings.Add(new Planting
                {
                    PlantTypeId = group.Key.PlantTypeId,
                    PlantId = group.Key.PlantId,
                    PlantCount = group.Sum(p => p.Count),
                    PotId = container.Kind == ContainerKind.Pot ? container.Id : null,
                    RaisedBedId = container.Kind == ContainerKind.Bed ? container.Id : null,
                    PlanId = plan.Id,
                    Note = Period(group.First().Slots)
                });
                created++;
            }
        }

        await db.SaveChangesAsync();
        return created;
    }

    // ---------------------------------------------------------------- input

    // spacingPercent: the user may plant closer than the spacing of the variety; the area per plant shrinks with the square
    private static AllocPlant ToPlant(Plant p, int count, int spacingPercent, Lifecycle? life, int lastFrostThird, PlantType? type, SowingTiming timing, PlantRole? role)
    {
        var factor = Math.Clamp(spacingPercent, 50, 100) / 100.0;
        var place = p.GrowingPlace switch
        {
            GrowingPlace.Container => PlantPlace.Pot,
            GrowingPlace.ColdFrame => PlantPlace.Bed,
            _ => PlantPlace.Bed // not set: into a bed
        };
        // The user's choice (variety first, then plant type) decides where the variety goes, unless it is a cold frame plant:
        // the allowed kinds of container, first choice first. If all allowed kinds are beds or all are pots, the plant has
        // to be put there (new containers are added when there is no room).
        var order = PlaceOrders.Parse(p.PlaceOrder ?? type?.PlaceOrder);
        var onlyOne = false;
        if (p.GrowingPlace != GrowingPlace.ColdFrame && order.Count > 0)
        {
            var hasBed = order.Contains(ContainerCategory.Bed);
            var hasPot = order.Any(c => c != ContainerCategory.Bed);
            place = (hasBed, hasPot, order[0] == ContainerCategory.Bed) switch
            {
                (true, false, _) => PlantPlace.Bed,
                (false, _, _) => PlantPlace.Pot,
                (true, true, true) => PlantPlace.BedThenPot, // beds first, pots as the second choice
                _ => PlantPlace.Either                       // pots first, then beds
            };
            onlyOne = !(hasBed && hasPot);
        }
        else order.Clear();
        double? row = (p.RowSpacingCm ?? p.PlantSpacingCm) * factor;
        var times = PlantOccupancy.SlotOptions(p, life, lastFrostThird, timing);
        return new AllocPlant(
            p.Id, p.PlantTypeId, p.Name, count, place,
            p.PotSizeMinLiters, p.PotSizeMaxLiters,
            AllocPlant.AreaFromSpacing(p.RowSpacingCm, p.PlantSpacingCm) * factor * factor,
            HeightOf(p),
            ColdFrame: p.GrowingPlace == GrowingPlace.ColdFrame,
            RowSpacingCm: row,
            Slots: times[0],
            PhMin: type?.PhMin, PhMax: type?.PhMax, SoilType: type?.SoilType, Category: type?.Category,
            Required: onlyOne, Order: order.Count > 0 ? order : null,
            SlotOptions: times.Count > 1 ? times : null,
            Role: role);
    }

    /// <summary>"Mar–May" for display; empty if the plant takes the place all year.</summary>
    public static string? Period(ulong slots)
    {
        if (!OccupancyMask.TryGetRange(slots, out var s, out var e)) return null;
        var names = System.Globalization.CultureInfo.CurrentUICulture.DateTimeFormat;
        var from = names.GetAbbreviatedMonthName(s / 3 + 1);
        var to = names.GetAbbreviatedMonthName(e / 3 + 1);
        return from == to ? from : $"{from}–{to}";
    }

    private static double? HeightOf(Plant p) => p.HeightMaxCm ?? p.HeightMinCm;

    // Pot: litres of the group; plantings already in it use litres of their pot size
    private static AllocContainerInput ToPotInput(Pot pot, IReadOnlyDictionary<int, PlantCategory?> categories)
    {
        var group = pot.PotGroup!;
        var number = group.Pots.OrderBy(p => p.Id).ToList().FindIndex(p => p.Id == pot.Id) + 1;
        var name = group.Quantity > 1 ? $"{group.Name} #{number}" : group.Name;
        return new AllocContainerInput(
            ContainerKind.Pot, pot.Id, name, group.VolumeLiters ?? 0,
            Used: pot.Plantings.Sum(x => (double?)(x.Plant?.PotSizeMinLiters) ?? 0),
            HasMain: pot.Plantings.Count > 0,
            MaxHeightCm: LowestHeight(pot.Plantings),
            ExistingTypeIds: pot.Plantings.Select(x => x.PlantTypeId).Distinct().ToList(),
            SoilPh: group.SoilPh, SoilType: group.SoilType,
            HasTree: HasTree(pot.Plantings, categories), HasVegetable: HasVegetable(pot.Plantings, categories),
            Category: PlaceOrders.CategoryOf(group.Kind));
    }

    // Bed: area; plantings already in it use area by number of plants and spacing
    private static AllocContainerInput ToBedInput(RaisedBed bed, IReadOnlyDictionary<int, PlantCategory?> categories) =>
        new(ContainerKind.Bed, bed.Id, bed.Name, bed.AreaSquareMeters,
            Used: bed.Plantings.Sum(x => (x.PlantCount ?? 1) *
                (x.Plant is null ? 0 : AllocPlant.AreaFromSpacing(x.Plant.RowSpacingCm, x.Plant.PlantSpacingCm) ?? 0)),
            HasMain: bed.Plantings.Count > 0,
            MaxHeightCm: LowestHeight(bed.Plantings),
            ExistingTypeIds: bed.Plantings.Select(x => x.PlantTypeId).Distinct().ToList(),
            HasCover: bed.HasCover,
            WidthCm: (double)Math.Min(bed.WidthCm, bed.LengthCm),
            SoilPh: bed.SoilPh, SoilType: bed.SoilType,
            HasTree: HasTree(bed.Plantings, categories), HasVegetable: HasVegetable(bed.Plantings, categories),
            Category: ContainerCategory.Bed);

    private static bool HasTree(IEnumerable<Planting> plantings, IReadOnlyDictionary<int, PlantCategory?> categories) =>
        plantings.Any(x => categories.GetValueOrDefault(x.PlantTypeId) == PlantCategory.FruitTree);

    private static bool HasVegetable(IEnumerable<Planting> plantings, IReadOnlyDictionary<int, PlantCategory?> categories) =>
        plantings.Any(x => categories.GetValueOrDefault(x.PlantTypeId) is { } c
            && c is not (PlantCategory.Herb or PlantCategory.Flower or PlantCategory.FruitTree));

    // Under-planting has to be lower than every plant that is already there
    private static double? LowestHeight(IEnumerable<Planting> plantings)
    {
        var heights = plantings.Where(x => x.Plant is not null).Select(x => HeightOf(x.Plant!)).Where(h => h is not null).ToList();
        return heights.Count == 0 ? null : heights.Min();
    }
}
