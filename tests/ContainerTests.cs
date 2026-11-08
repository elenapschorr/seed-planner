using SeedPlanner.Domain;
using Xunit;

namespace SeedPlanner.Tests;

public class ContainerTests
{
    private static PotGroup RoundPots(decimal top, decimal bottom, decimal height) =>
        new() { Shape = PotShape.Round, TopCm = top, BottomCm = bottom, HeightCm = height };

    private static PotGroup GroupOf(int pots)
    {
        var group = new PotGroup();
        for (var i = 0; i < pots; i++) group.Pots.Add(new Pot());
        return group;
    }

    [Fact]
    public void Round_pot_volume_is_a_truncated_cone()
    {
        // 37 / 18 / 60 cm (rose pot): about 37 litres
        Assert.Equal(37.06, RoundPots(37, 18, 60).VolumeLiters!.Value, 2);

        // 34 / 25 / 28 cm: about 19.3 litres
        Assert.Equal(19.29, RoundPots(34, 25, 28).VolumeLiters!.Value, 2);
    }

    [Fact]
    public void Rectangular_pot_volume_is_a_truncated_pyramid()
    {
        var group = new PotGroup { Shape = PotShape.Rectangular, TopCm = 34.5m, BottomCm = 28, HeightCm = 28 };

        Assert.Equal(27.44, group.VolumeLiters!.Value, 2);
    }

    [Fact]
    public void Pot_without_all_measures_has_no_volume()
    {
        Assert.Null(new PotGroup { TopCm = 40 }.VolumeLiters);
    }

    [Fact]
    public void The_pots_of_a_group_are_counted()
    {
        Assert.Equal(7, GroupOf(7).Quantity);
        Assert.Equal(0, GroupOf(0).Quantity);
    }

    [Fact]
    public void Free_pots_are_the_pots_that_hold_no_planting()
    {
        var group = GroupOf(7);
        group.Pots[0].Plantings.Add(new Planting { PlantTypeId = 1 });
        group.Pots[1].Plantings.Add(new Planting { PlantTypeId = 1 });

        Assert.Equal(2, group.OccupiedQuantity);
        Assert.Equal(5, group.FreeQuantity);
        Assert.False(group.Pots[0].IsFree);
        Assert.True(group.Pots[2].IsFree);
    }

    [Fact]
    public void Several_plantings_in_one_pot_occupy_only_that_pot()
    {
        // A bowl with cranberry and strawberry: one pot in use, not two
        var group = GroupOf(2);
        group.Pots[0].Plantings.Add(new Planting { PlantTypeId = 1 });
        group.Pots[0].Plantings.Add(new Planting { PlantTypeId = 2 });

        Assert.Equal(1, group.OccupiedQuantity);
        Assert.Equal(1, group.FreeQuantity);
    }

    [Fact]
    public void Stacked_pots_make_towers()
    {
        // 66 strawberry pots, three per tower
        Assert.Equal(22, new PotGroup { SetSize = 3, Pots = Enumerable.Range(0, 66).Select(_ => new Pot()).ToList() }.SetCount);
        Assert.Equal(7, GroupOf(7).SetCount);
    }

    [Fact]
    public void Oval_bed_area_is_an_ellipse()
    {
        var bed = new RaisedBed { Shape = BedShape.Oval, WidthCm = 80, LengthCm = 160, HeightCm = 58 };

        Assert.Equal(1.005, bed.AreaSquareMeters, 3);
        Assert.Equal(583.1, bed.VolumeLiters!.Value, 1);
    }

    [Fact]
    public void Rectangular_bed_area_is_width_times_length()
    {
        var bed = new RaisedBed { Shape = BedShape.Rectangular, WidthCm = 100, LengthCm = 200, HeightCm = 40 };

        Assert.Equal(2.0, bed.AreaSquareMeters, 3);
        Assert.Equal(800, bed.VolumeLiters!.Value, 1);
    }

    [Fact]
    public void A_ground_bed_has_an_area_but_no_volume()
    {
        var bed = new RaisedBed { Kind = BedKind.Ground, Shape = BedShape.Rectangular, WidthCm = 65, LengthCm = 270 };

        Assert.Equal(1.755, bed.AreaSquareMeters, 3);
        Assert.Null(bed.VolumeLiters);
    }
}
