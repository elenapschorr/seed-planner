using SeedPlanner.Domain;
using Xunit;

namespace SeedPlanner.Tests;

public class PlantTypeCompanionTests
{
    [Fact]
    public void Between_puts_the_smaller_id_first()
    {
        var pair = PlantTypeCompanion.Between(7, 3, CompanionRelation.Bad);

        Assert.Equal(3, pair.PlantTypeId);
        Assert.Equal(7, pair.OtherPlantTypeId);
    }

    [Fact]
    public void Between_gives_the_same_pair_in_either_order()
    {
        var a = PlantTypeCompanion.Between(2, 9, CompanionRelation.Good);
        var b = PlantTypeCompanion.Between(9, 2, CompanionRelation.Good);

        Assert.Equal(a.PlantTypeId, b.PlantTypeId);
        Assert.Equal(a.OtherPlantTypeId, b.OtherPlantTypeId);
    }

    [Fact]
    public void Between_rejects_a_type_paired_with_itself()
    {
        Assert.Throws<ArgumentException>(() => PlantTypeCompanion.Between(4, 4, CompanionRelation.Good));
    }
}
