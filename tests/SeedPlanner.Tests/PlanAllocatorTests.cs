using SeedPlanner.Domain;
using Xunit;

namespace SeedPlanner.Tests;

public class PlanAllocatorTests
{
    private static AllocPlant Plant(
        int id, int type, int count = 1, double? liters = null, double? height = null,
        double? litersMax = null, double? area = null, PlantPlace place = PlantPlace.Pot, bool cold = false,
        double? rowCm = null, ulong slots = OccupancyMask.AllYear) =>
        new(id, type, "P" + id, count, place, liters, litersMax, area, height, cold, rowCm, slots);

    // Thirds of months: Mar-May = 6..14, Jun-Aug = 15..23, Sep-Nov = 24..32
    private static readonly ulong Spring = OccupancyMask.Range(6, 14);
    private static readonly ulong Summer = OccupancyMask.Range(15, 23);
    private static readonly ulong Autumn = OccupancyMask.Range(24, 32);

    private static AllocContainerInput Pot(int id, double liters) =>
        new(ContainerKind.Pot, id, "Pot" + id, liters);

    private static AllocationResult Run(
        AllocPlant[] plants, AllocContainerInput[] containers,
        ShortageStrategy strategy = ShortageStrategy.ShrinkProportionally,
        double maxBed = 0, PlantTypeCompanion[]? relations = null,
        NoCoverStrategy noCover = NoCoverStrategy.NewCoveredBed, bool allowOthers = false) =>
        PlanAllocator.Allocate(
            plants, containers, relations ?? Array.Empty<PlantTypeCompanion>(), strategy, maxBed, noCover, allowOthers);

    private static AllocContainerInput Bed(int id, double squareMeters, bool cover = false, double? widthCm = null) =>
        new(ContainerKind.Bed, id, "Bed" + id, squareMeters, HasCover: cover, WidthCm: widthCm);

    private static int Count(ContainerResult c, int plantId) =>
        c.Placements.Where(x => x.PlantId == plantId).Sum(x => x.Count);

    private static ContainerResult Box(AllocationResult r, int id) => r.Containers.Single(c => c.Id == id);

    // ---------------------------------------------------------------- main plants

    [Fact]
    public void Large_plants_are_placed_first_into_the_pot_that_fits()
    {
        var r = Run(
            [Plant(1, 1, liters: 15), Plant(2, 2, liters: 5)],
            [Pot(1, 10), Pot(2, 20)]);

        Assert.Equal(1, Box(r, 2).Placements.Single(p => p.Role == PlantingRole.Main).PlantId);
        Assert.Equal(2, Box(r, 1).Placements.Single(p => p.Role == PlantingRole.Main).PlantId);
    }

    [Fact]
    public void A_plant_takes_the_smallest_pot_that_is_large_enough()
    {
        var r = Run([Plant(1, 1, liters: 8)], [Pot(2, 20), Pot(1, 10)]);

        Assert.Single(Box(r, 1).Placements);
        Assert.Empty(Box(r, 2).Placements);
    }

    [Fact]
    public void Plants_without_pot_size_are_reported()
    {
        var r = Run([Plant(1, 1)], [Pot(1, 10)]);

        var u = Assert.Single(r.Unplaced);
        Assert.Equal(UnplacedReason.MissingSpaceData, u.Reason);
        Assert.Empty(Box(r, 1).Placements);
    }

    // ---------------------------------------------------------------- under-planting

    [Fact]
    public void Small_plants_are_put_under_the_big_one_when_the_pots_are_used_up()
    {
        var r = Run(
            [Plant(1, 1, liters: 12, height: 100), Plant(2, 2, liters: 5, height: 20)],
            [Pot(1, 20)]);

        var placements = Box(r, 1).Placements;
        Assert.Equal(1, placements.Single(p => p.Role == PlantingRole.Main).PlantId);
        Assert.Equal(2, placements.Single(p => p.Role == PlantingRole.Companion).PlantId);
        Assert.Empty(r.Reductions);
    }

    [Fact]
    public void A_bad_neighbour_is_not_put_under_the_plant()
    {
        var r = Run(
            [Plant(1, 1, liters: 12, height: 100), Plant(2, 2, liters: 5, height: 20)],
            [Pot(1, 20)],
            relations: [PlantTypeCompanion.Between(1, 2, CompanionRelation.Bad)]);

        var reduction = Assert.Single(r.Reductions);
        Assert.Equal(2, reduction.PlantId);
        Assert.Equal(0, reduction.Placed);
    }

    [Fact]
    public void A_plant_that_is_not_lower_than_the_main_plant_is_not_under_planted()
    {
        var r = Run(
            [Plant(1, 1, liters: 12, height: 50), Plant(2, 2, liters: 5, height: 80)],
            [Pot(1, 20)]);

        Assert.Equal(2, Assert.Single(r.Reductions).PlantId);
    }

    [Fact]
    public void Plants_that_fit_to_the_fewest_plant_types_are_placed_first()
    {
        // Y does not go with type 3, so only the first pot is possible for it.
        // X fits both pots; if X took the first pot, Y would have no place.
        var r = Run(
            [
                Plant(1, 1, liters: 5, height: 100),
                Plant(3, 3, liters: 5, height: 100),
                Plant(10, 10, liters: 5, height: 20),
                Plant(11, 11, liters: 5, height: 20)
            ],
            [Pot(1, 10), Pot(2, 10)],
            relations: [PlantTypeCompanion.Between(3, 11, CompanionRelation.Bad)]);

        Assert.Empty(r.Reductions);
        Assert.Empty(r.Unplaced);
        Assert.Equal(11, Box(r, 1).Placements.Single(p => p.Role == PlantingRole.Companion).PlantId);
        Assert.Equal(10, Box(r, 2).Placements.Single(p => p.Role == PlantingRole.Companion).PlantId);
    }

    [Fact]
    public void An_occupied_pot_only_takes_under_planting()
    {
        var occupied = new AllocContainerInput(
            ContainerKind.Pot, 1, "Pot1", 20, Used: 10, HasMain: true, MaxHeightCm: 100, ExistingTypeIds: [1]);

        var r = Run([Plant(2, 2, liters: 5, height: 30)], [occupied]);

        Assert.Equal(PlantingRole.Companion, Assert.Single(Box(r, 1).Placements).Role);
    }

    // ---------------------------------------------------------------- shortage

    [Fact]
    public void Shrink_reduces_all_varieties_in_proportion()
    {
        // 3 pots of 10 l, 6 plants of 10 l wanted: A 4, B 2 -> A 2, B 1
        var r = Run(
            [Plant(1, 1, count: 4, liters: 10), Plant(2, 2, count: 2, liters: 10)],
            [Pot(1, 10), Pot(2, 10), Pot(3, 10)]);

        Assert.Equal(2, r.Reductions.Single(x => x.PlantId == 1).Placed);
        Assert.Equal(1, r.Reductions.Single(x => x.PlantId == 2).Placed);
        Assert.Equal(0.5, r.ScaleFactor, 3);
    }

    [Fact]
    public void BuyPots_lists_new_pots_with_the_size_from_the_plants()
    {
        var r = Run(
            [Plant(1, 1, count: 2, liters: 10, litersMax: 15)], [],
            ShortageStrategy.BuyPots);

        var group = Assert.Single(r.NewPots);
        Assert.Equal(15, group.Liters);
        Assert.Equal(2, group.Count);
    }

    [Fact]
    public void BuyPots_puts_small_plants_into_the_new_pots_first()
    {
        var r = Run(
            [
                Plant(1, 1, count: 2, liters: 10, litersMax: 15, height: 100),
                Plant(2, 2, count: 1, liters: 4, height: 20)
            ], [],
            ShortageStrategy.BuyPots);

        Assert.Equal(2, r.Containers.Count);
        Assert.Contains(r.Containers, c => c.Placements.Any(p => p.PlantId == 2 && p.Role == PlantingRole.Companion));
        Assert.Empty(r.Unplaced);
    }

    [Fact]
    public void AddBeds_creates_beds_up_to_the_maximum_area()
    {
        var r = Run(
            [Plant(1, 1, count: 5, area: 1.0, place: PlantPlace.Bed)], [],
            ShortageStrategy.AddBeds, maxBed: 2.0);

        Assert.Equal(3, r.NewBeds.Count);
        Assert.Equal(5.0, r.NewBeds.Sum(b => b.Used), 6);
        Assert.All(r.NewBeds, b => Assert.True(b.Used <= 2.0 + 1e-9));
    }

    [Fact]
    public void AddBeds_reports_a_plant_that_is_larger_than_a_bed()
    {
        var r = Run(
            [Plant(1, 1, area: 3.0, place: PlantPlace.Bed)], [],
            ShortageStrategy.AddBeds, maxBed: 2.0);

        Assert.Equal(UnplacedReason.TooBigForBed, Assert.Single(r.Unplaced).Reason);
        Assert.Empty(r.NewBeds);
    }

    [Theory]
    [InlineData(40, 50, 0.2)]
    [InlineData(null, 30, 0.09)]
    [InlineData(30, null, 0.09)]
    public void Area_per_plant_comes_from_the_spacing(int? row, int? plant, double expected)
    {
        Assert.Equal(expected, AllocPlant.AreaFromSpacing(row, plant)!.Value, 6);
    }

    [Fact]
    public void Area_per_plant_is_unknown_without_spacing()
    {
        Assert.Null(AllocPlant.AreaFromSpacing(null, null));
    }

    // ---------------------------------------------------------------- cold frame

    [Fact]
    public void Cold_frame_plants_only_go_into_covered_beds()
    {
        var r = Run(
            [Plant(1, 1, area: 0.5, place: PlantPlace.Bed, cold: true)],
            [Pot(1, 20), Bed(2, 2.0), Bed(3, 2.0, cover: true)]);

        Assert.Equal(1, Count(Box(r, 3), 1));
        Assert.Empty(Box(r, 2).Placements);
        Assert.Empty(Box(r, 1).Placements);
    }

    [Fact]
    public void Cold_frame_plants_are_kept_together_in_one_bed()
    {
        // The bed with the most free area is taken first and stays the only one as long as it has room
        var r = Run(
            [Plant(1, 1, count: 3, area: 1.0, place: PlantPlace.Bed, cold: true)],
            [Bed(1, 3.0, cover: true), Bed(2, 5.0, cover: true)]);

        Assert.Equal(3, Count(Box(r, 2), 1));
        Assert.Empty(Box(r, 1).Placements);
    }

    [Fact]
    public void Cold_frame_plants_fill_one_bed_after_the_other()
    {
        var r = Run(
            [Plant(1, 1, count: 3, area: 1.0, place: PlantPlace.Bed, cold: true)],
            [Bed(1, 2.0, cover: true), Bed(2, 2.0, cover: true)]);

        Assert.Equal(2, Count(Box(r, 1), 1));
        Assert.Equal(1, Count(Box(r, 2), 1));
    }

    [Fact]
    public void Other_plants_stay_out_of_covered_beds_by_default()
    {
        var r = Run(
            [Plant(1, 1, area: 1.0, place: PlantPlace.Bed)],
            [Bed(1, 3.0, cover: true)]);

        var u = Assert.Single(r.Unplaced);
        Assert.Equal(1, u.PlantId);
        Assert.Equal(UnplacedReason.NoSuitableContainer, u.Reason);
        Assert.Empty(Box(r, 1).Placements);
    }

    [Fact]
    public void Other_plants_may_use_a_covered_bed_if_allowed_but_uncovered_beds_come_first()
    {
        var r = Run(
            [Plant(1, 1, area: 1.0, place: PlantPlace.Bed)],
            [Bed(1, 3.0, cover: true), Bed(2, 3.0)],
            allowOthers: true);

        Assert.Equal(1, Count(Box(r, 2), 1));
        Assert.Empty(Box(r, 1).Placements);
    }

    [Fact]
    public void Other_plants_use_the_rest_of_a_covered_bed_after_the_cold_frame_plants()
    {
        // Bed of 2 m²: the cold frame plant has priority, one of the two other plants fits next to it
        var r = Run(
            [
                Plant(1, 1, count: 2, area: 1.0, place: PlantPlace.Bed),
                Plant(2, 2, area: 1.0, place: PlantPlace.Bed, cold: true)
            ],
            [Bed(1, 2.0, cover: true)],
            allowOthers: true);

        Assert.Equal(1, Count(Box(r, 1), 2));
        Assert.Equal(1, Count(Box(r, 1), 1));
        Assert.Equal(1, r.Reductions.Single(x => x.PlantId == 1).Placed);
    }

    [Fact]
    public void Without_a_cover_one_existing_bed_can_be_retrofitted_the_smallest_that_takes_all()
    {
        var r = Run(
            [Plant(1, 1, count: 3, area: 1.0, place: PlantPlace.Bed, cold: true)],
            [Bed(1, 10.0), Bed(2, 4.0), Bed(3, 2.0)],
            noCover: NoCoverStrategy.RetrofitExistingBed);

        var retrofitted = Assert.Single(r.Containers, c => c.NeedsCover);
        Assert.Equal(2, retrofitted.Id);
        Assert.True(retrofitted.HasCover);
        Assert.Equal(3, Count(retrofitted, 1));
    }

    [Fact]
    public void Retrofit_takes_the_largest_bed_if_none_takes_all_and_the_rest_is_reduced()
    {
        var r = Run(
            [Plant(1, 1, count: 5, area: 1.0, place: PlantPlace.Bed, cold: true)],
            [Bed(1, 2.0), Bed(2, 3.0)],
            noCover: NoCoverStrategy.RetrofitExistingBed);

        Assert.Equal(2, Assert.Single(r.Containers, c => c.NeedsCover).Id);
        var reduction = Assert.Single(r.Reductions);
        Assert.Equal(5, reduction.Wanted);
        Assert.Equal(3, reduction.Placed);
    }

    [Fact]
    public void Without_a_cover_a_new_covered_bed_is_created_up_to_the_maximum_area()
    {
        var r = Run(
            [Plant(1, 1, count: 5, area: 1.0, place: PlantPlace.Bed, cold: true)], [],
            maxBed: 2.0, noCover: NoCoverStrategy.NewCoveredBed);

        Assert.Equal(3, r.NewBeds.Count);
        Assert.All(r.NewBeds, b => Assert.True(b.HasCover));
        Assert.Equal(5.0, r.NewBeds.Sum(b => b.Used), 6);
        Assert.Empty(r.Unplaced);
        Assert.Empty(r.Reductions);
    }

    [Fact]
    public void No_cold_frame_plants_means_no_extra_beds_and_no_retrofit()
    {
        var r = Run(
            [Plant(1, 1, area: 1.0, place: PlantPlace.Bed)],
            [Bed(1, 2.0)],
            noCover: NoCoverStrategy.RetrofitExistingBed);

        Assert.Single(r.Containers);
        Assert.DoesNotContain(r.Containers, c => c.NeedsCover || c.HasCover);
    }

    [Fact]
    public void A_cold_frame_plant_without_spacing_is_reported()
    {
        var r = Run([Plant(1, 1, place: PlantPlace.Bed, cold: true)], [Bed(1, 2.0, cover: true)]);

        Assert.Equal(UnplacedReason.MissingSpaceData, Assert.Single(r.Unplaced).Reason);
    }

    [Fact]
    public void AddBeds_adds_covered_beds_for_cold_frame_plants_when_the_covered_bed_is_full()
    {
        var r = Run(
            [
                Plant(1, 1, count: 3, area: 1.0, place: PlantPlace.Bed, cold: true),
                Plant(2, 2, count: 1, area: 1.0, place: PlantPlace.Bed)
            ],
            [Bed(1, 1.0, cover: true)],
            ShortageStrategy.AddBeds, maxBed: 2.0);

        Assert.Empty(r.Unplaced);
        Assert.All(r.Containers.Where(c => c.Placements.Any(x => x.PlantId == 1)), c => Assert.True(c.HasCover));
        Assert.Equal(3, r.Containers.Sum(c => Count(c, 1)));
        Assert.Equal(1, r.Containers.Where(c => !c.HasCover).Sum(c => Count(c, 2)));
    }

    // ---------------------------------------------------------------- bed width

    [Fact]
    public void A_plant_does_not_go_into_a_bed_narrower_than_its_row_spacing()
    {
        var r = Run(
            [Plant(1, 1, area: 0.25, place: PlantPlace.Bed, rowCm: 50)],
            [Bed(1, 2, widthCm: 40), Bed(2, 2, widthCm: 120)]);

        Assert.Empty(Box(r, 1).Placements);
        Assert.Equal(1, Count(Box(r, 2), 1));
    }

    [Fact]
    public void A_plant_that_fits_no_bed_by_width_is_reported()
    {
        var r = Run(
            [Plant(1, 1, area: 0.25, place: PlantPlace.Bed, rowCm: 50)],
            [Bed(1, 2, widthCm: 40)]);

        Assert.Equal(UnplacedReason.NoSuitableContainer, Assert.Single(r.Unplaced).Reason);
    }

    [Fact]
    public void A_bed_without_width_has_no_limit()
    {
        var r = Run(
            [Plant(1, 1, area: 0.25, place: PlantPlace.Bed, rowCm: 50)],
            [Bed(1, 2)]);

        Assert.Equal(1, Count(Box(r, 1), 1));
    }

    [Fact]
    public void Retrofit_chooses_a_bed_wide_enough_for_the_cold_frame_plants()
    {
        var r = Run(
            [Plant(1, 1, area: 0.25, place: PlantPlace.Bed, cold: true, rowCm: 50)],
            [Bed(1, 1, widthCm: 40), Bed(2, 3, widthCm: 100)],
            noCover: NoCoverStrategy.RetrofitExistingBed);

        Assert.True(Box(r, 2).NeedsCover);
        Assert.False(Box(r, 1).NeedsCover);
        Assert.Equal(1, Count(Box(r, 2), 1));
    }

    // ---------------------------------------------------------------- growing place rules

    [Fact]
    public void A_pot_plant_is_never_put_into_a_bed_and_is_reported_when_the_pots_are_not_enough()
    {
        var r = Run(
            [Plant(1, 1, liters: 10, area: 0.25, place: PlantPlace.Pot)],
            [Bed(1, 5)],
            ShortageStrategy.AddBeds, maxBed: 2);

        Assert.Equal(UnplacedReason.NotEnoughPots, Assert.Single(r.Unplaced).Reason);
        Assert.Empty(Box(r, 1).Placements);
        Assert.Empty(r.NewBeds);
    }

    [Fact]
    public void Open_ground_plants_go_into_beds_first_even_if_a_pot_is_free()
    {
        var r = Run(
            [Plant(1, 1, liters: 5, area: 0.25, place: PlantPlace.BedThenPot)],
            [Pot(1, 10), Bed(2, 2)]);

        Assert.Equal(1, Count(Box(r, 2), 1));
        Assert.Empty(r.Containers.Single(c => c.Kind == ContainerKind.Pot).Placements);
    }

    [Fact]
    public void Open_ground_plants_use_free_pots_when_the_beds_are_full()
    {
        var r = Run(
            [Plant(1, 1, count: 2, liters: 5, area: 1, place: PlantPlace.BedThenPot)],
            [Pot(1, 10), Bed(1, 1)]);

        Assert.Equal(1, Count(r.Containers.Single(c => c.Kind == ContainerKind.Bed), 1));
        Assert.Equal(1, Count(r.Containers.Single(c => c.Kind == ContainerKind.Pot), 1));
        Assert.Empty(r.Unplaced);
    }

    [Fact]
    public void Pot_plants_get_the_pots_before_open_ground_plants_use_the_rest()
    {
        var r = Run(
            [Plant(1, 1, liters: 5, place: PlantPlace.Pot), Plant(2, 2, liters: 5, area: 1, place: PlantPlace.BedThenPot)],
            [Pot(1, 5)],
            ShortageStrategy.AddBeds);

        Assert.Equal(1, Count(r.Containers.Single(), 1));
        Assert.Equal(2, Assert.Single(r.Unplaced).PlantId);
    }

    // ---------------------------------------------------------------- time

    [Fact]
    public void A_bed_is_used_twice_when_the_crops_do_not_overlap_in_time()
    {
        var r = Run(
            [Plant(1, 1, area: 1, place: PlantPlace.Bed, slots: Spring), Plant(2, 2, area: 1, place: PlantPlace.Bed, slots: Autumn)],
            [Bed(1, 1)]);

        Assert.Empty(r.Unplaced);
        Assert.Equal(1, Count(Box(r, 1), 1));
        Assert.Equal(1, Count(Box(r, 1), 2));
        Assert.Equal(1, Box(r, 1).Used, 3);
    }

    [Fact]
    public void Crops_that_overlap_in_time_need_separate_space()
    {
        var r = Run(
            [Plant(1, 1, area: 1, place: PlantPlace.Bed, slots: Spring), Plant(2, 2, area: 1, place: PlantPlace.Bed, slots: Spring | Summer)],
            [Bed(1, 1)],
            ShortageStrategy.AddBeds);

        Assert.Single(r.Unplaced);
    }

    [Fact]
    public void A_pot_takes_a_second_main_plant_after_the_first_is_harvested()
    {
        var r = Run(
            [Plant(1, 1, liters: 5, slots: Spring), Plant(2, 2, liters: 5, slots: Autumn)],
            [Pot(1, 10)]);

        Assert.Empty(r.Unplaced);
        Assert.Equal(2, Box(r, 1).Placements.Count(p => p.Role == PlantingRole.Main));
    }

    [Fact]
    public void Under_planting_needs_a_main_plant_at_the_same_time()
    {
        var r = Run(
            [Plant(1, 1, area: 0.5, height: 100, place: PlantPlace.Bed, slots: Spring),
             Plant(2, 2, area: 0.1, height: 20, place: PlantPlace.Bed, slots: Autumn)],
            [Bed(1, 1)]);

        // Both fit as main plants (the bed is free in autumn), so neither is an under-planting
        Assert.All(Box(r, 1).Placements, p => Assert.Equal(PlantingRole.Main, p.Role));
    }

    [Fact]
    public void Bad_neighbours_only_matter_when_they_stand_together_in_time()
    {
        var bad = PlantTypeCompanion.Between(1, 2, CompanionRelation.Bad);
        var r = Run(
            [Plant(1, 1, area: 0.5, place: PlantPlace.Bed, slots: Spring), Plant(2, 2, area: 0.5, place: PlantPlace.Bed, slots: Autumn)],
            [Bed(1, 2)],
            relations: [bad]);

        Assert.Equal(2, Box(r, 1).Placements.Count);
    }

    [Fact]
    public void Plants_without_times_are_reported_and_not_placed()
    {
        var r = Run([Plant(1, 1, area: 1, place: PlantPlace.Bed, slots: 0)], [Bed(1, 5)]);

        var u = Assert.Single(r.Unplaced);
        Assert.Equal(UnplacedReason.MissingTimeData, u.Reason);
        Assert.Empty(Box(r, 1).Placements);
    }

    [Fact]
    public void Added_beds_are_reused_over_the_year()
    {
        var r = Run(
            [Plant(1, 1, count: 2, area: 1, place: PlantPlace.Bed, slots: Spring), Plant(2, 2, count: 2, area: 1, place: PlantPlace.Bed, slots: Autumn)],
            [],
            ShortageStrategy.AddBeds, maxBed: 2);

        Assert.Empty(r.Unplaced);
        var bed = Assert.Single(r.Containers);
        Assert.Equal(4, bed.Placements.Sum(p => p.Count));
    }
}
