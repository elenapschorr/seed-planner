using Xunit;
using SeedPlanner.Domain;

namespace SeedPlanner.Tests;

public class PlantPairsTests
{
    private static PlantTypeCompanion Good(int a, int b) => PlantTypeCompanion.Between(a, b, CompanionRelation.Good);
    private static PlantTypeCompanion Bad(int a, int b) => PlantTypeCompanion.Between(a, b, CompanionRelation.Bad);

    private static Dictionary<int, PlantRole?> Roles(params (int Id, PlantRole? Role)[] items) =>
        items.ToDictionary(i => i.Id, i => i.Role);

    [Fact]
    public void A_good_neighbour_of_the_other_role_is_a_partner_in_both_directions()
    {
        var roles = Roles((1, PlantRole.Main), (2, PlantRole.Secondary));

        var partners = PlantPairs.Partners(roles, [Good(1, 2)]);

        Assert.Equal(new[] { 2 }, partners[1]);
        Assert.Equal(new[] { 1 }, partners[2]);
    }

    [Fact]
    public void Two_plants_of_the_same_role_bad_neighbours_and_plants_without_a_role_are_no_partners()
    {
        var roles = Roles((1, PlantRole.Main), (2, PlantRole.Main), (3, PlantRole.Secondary), (4, null));

        var partners = PlantPairs.Partners(roles, [Good(1, 2), Bad(1, 3), Good(1, 4)]);

        Assert.Empty(partners[1]);
        Assert.Empty(partners[3]);
        Assert.False(partners.ContainsKey(4));
    }

    [Fact]
    public void The_partners_are_sorted_by_id()
    {
        var roles = Roles((1, PlantRole.Main), (5, PlantRole.Secondary), (3, PlantRole.Secondary), (4, PlantRole.Secondary));

        var partners = PlantPairs.Partners(roles, [Good(1, 5), Good(1, 3), Good(1, 4)]);

        Assert.Equal(new[] { 3, 4, 5 }, partners[1]);
    }

    [Fact]
    public void The_user_can_remove_a_suggested_partner_and_add_another_secondary_plant()
    {
        var roles = Roles((1, PlantRole.Main), (2, PlantRole.Secondary), (3, PlantRole.Secondary));

        var partners = PlantPairs.MainPartners(roles, [Good(1, 2)], [
            new PlantPairs.PartnerChange(1, 2, false),
            new PlantPairs.PartnerChange(1, 3, true)]);

        Assert.Equal(new[] { 3 }, partners[1]);
        Assert.False(partners.ContainsKey(2)); // only main plants have a list
    }

    [Fact]
    public void Changes_that_do_not_fit_the_roles_are_ignored()
    {
        var roles = Roles((1, PlantRole.Main), (2, PlantRole.Main), (3, PlantRole.Secondary));

        var partners = PlantPairs.MainPartners(roles, [], [
            new PlantPairs.PartnerChange(1, 2, true),   // a main plant is no partner
            new PlantPairs.PartnerChange(3, 1, true)]); // a secondary plant has no list

        Assert.Empty(partners[1]);
        Assert.False(partners.ContainsKey(3));
    }
}
