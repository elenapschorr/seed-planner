using SeedPlanner.Domain;
using Xunit;

namespace SeedPlanner.Tests;

public class ContainerTests
{
    private static Pot RoundPot(decimal top, decimal bottom, decimal height) =>
        new() { Shape = PotShape.Round, TopCm = top, BottomCm = bottom, HeightCm = height, Quantity = 1 };

    [Fact]
    public void Round_pot_volume_is_a_truncated_cone()
    {
        // 37 / 18 / 60 cm (rose pot): about 37 litres
        Assert.Equal(37.06, RoundPot(37, 18, 60).VolumeLiters!.Value, 2);

        // 34 / 25 / 28 cm: about 19.3 litres
        Assert.Equal(19.29, RoundPot(34, 25, 28).VolumeLiters!.Value, 2);
    }

    [Fact]
    public void Rectangular_pot_volume_is_a_truncated_pyramid()
    {
        var pot = new Pot { Shape = PotShape.Rectangular, TopCm = 34.5m, BottomCm = 28, HeightCm = 28 };

        Assert.Equal(27.44, pot.VolumeLiters!.Value, 2);
    }

    [Fact]
    public void Pot_without_all_measures_has_no_volume()
    {
        Assert.Null(new Pot { TopCm = 40 }.VolumeLiters);
    }

    [Fact]
    public void Free_pots_are_the_pots_that_hold_no_plant()
    {
        var pot = new Pot
        {
            Quantity = 7,
            Uses = { new PotUse { PlantTypeId = 1, Quantity = 2 } }
        };

        Assert.Equal(2, pot.OccupiedQuantity);
        Assert.Equal(5, pot.FreeQuantity);
        Assert.False(pot.IsOverbooked);
    }

    [Fact]
    public void Too_many_pots_in_use_are_reported()
    {
        var pot = new Pot
        {
            Quantity = 3,
            Uses = { new PotUse { PlantTypeId = 1, Quantity = 2 }, new PotUse { PlantTypeId = 2, Quantity = 2 } }
        };

        Assert.True(pot.IsOverbooked);
        Assert.Equal(0, pot.FreeQuantity);
    }

    [Fact]
    public void Stacked_pots_make_towers()
    {
        // 66 strawberry pots, three per tower
        Assert.Equal(22, new Pot { Quantity = 66, SetSize = 3 }.SetCount);
        Assert.Equal(7, new Pot { Quantity = 7 }.SetCount);
    }

    [Fact]
    public void Oval_bed_area_is_an_ellipse()
    {
        var bed = new RaisedBed { Shape = BedShape.Oval, WidthCm = 80, LengthCm = 160, HeightCm = 58 };

        Assert.Equal(1.005, bed.AreaSquareMeters, 3);
        Assert.Equal(583.1, bed.VolumeLiters, 1);
    }

    [Fact]
    public void Rectangular_bed_area_is_width_times_length()
    {
        var bed = new RaisedBed { Shape = BedShape.Rectangular, WidthCm = 100, LengthCm = 200, HeightCm = 40 };

        Assert.Equal(2.0, bed.AreaSquareMeters, 3);
        Assert.Equal(800, bed.VolumeLiters, 1);
    }
}
