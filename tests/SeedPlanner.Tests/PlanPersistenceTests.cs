using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;

namespace SeedPlanner.Tests;

/// <summary>
/// Tests with a real PostgreSQL database (started in Docker): plans and the own demand are saved with their
/// results, calculated again without leftovers, and belong to one user only.
/// </summary>
public class PlanPersistenceTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";

    private sealed class FakeUser(string? userId) : ICurrentUser
    {
        public string? UserId { get; } = userId;
    }

    private static SeedPlannerDbContext CreateContext(string connectionString, string? userId)
    {
        var options = new DbContextOptionsBuilder<SeedPlannerDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new SeedPlannerDbContext(options, new FakeUser(userId));
    }

    private static PostgreSqlContainer NewDatabase() =>
        new PostgreSqlBuilder("postgres:17").Build();

    // Two users; a crop "Tomato" (2 to 4 plants per person) with three varieties for user A and one for user B
    private static async Task PrepareAsync(string connectionString)
    {
        await using (var setup = CreateContext(connectionString, null))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Users.Add(new IdentityUser { Id = UserA, UserName = "a@example.com", NormalizedUserName = "A@EXAMPLE.COM" });
            setup.Users.Add(new IdentityUser { Id = UserB, UserName = "b@example.com", NormalizedUserName = "B@EXAMPLE.COM" });
            setup.PlantTypes.Add(new PlantType
            {
                Name = "Tomato", Category = PlantCategory.FruitVegetable,
                DemandPerPersonMin = 2, DemandPerPersonMax = 4, DemandUnit = DemandUnit.Plants
            });
            await setup.SaveChangesAsync();
        }

        await using (var db = CreateContext(connectionString, UserA))
        {
            var type = await db.PlantTypes.SingleAsync();
            foreach (var name in new[] { "Money Maker", "Matina", "Roma" })
                db.Plants.Add(new Plant { Name = name, PlantTypeId = type.Id, StockStatus = StockStatus.InStock });
            await db.SaveChangesAsync();
        }
        await using (var db = CreateContext(connectionString, UserB))
        {
            var type = await db.PlantTypes.SingleAsync();
            db.Plants.Add(new Plant { Name = "Black Krim", PlantTypeId = type.Id, StockStatus = StockStatus.InStock });
            await db.SaveChangesAsync();
        }
    }

    // What the app does when it calculates a plan
    private static async Task RebuildAsync(SeedPlannerDbContext db, Plan plan)
    {
        var types = await db.PlantTypes.Include(t => t.Plants).ToListAsync();
        var own = await db.UserDemands.ToDictionaryAsync(d => d.PlantTypeId);
        PlanCalculator.Rebuild(plan, types, own);
    }

    private static async Task<int> CreatePlanAsync(string connectionString, string userId, string name = "Basic")
    {
        await using var db = CreateContext(connectionString, userId);
        var plan = new Plan { Name = name, ValidFrom = new DateOnly(2027, 1, 1), ValidTo = new DateOnly(2027, 12, 31), Persons = 2, Level = DemandLevel.Middle };
        db.Plans.Add(plan);
        await RebuildAsync(db, plan);
        await db.SaveChangesAsync();
        return plan.Id;
    }

    [Fact]
    public async Task Plan_is_saved_with_settings_and_result()
    {
        await using var container = NewDatabase();
        await container.StartAsync();
        var connection = container.GetConnectionString();
        await PrepareAsync(connection);

        var id = await CreatePlanAsync(connection, UserA);

        await using var db = CreateContext(connection, UserA);
        var plan = await db.Plans.Include(p => p.Items).ThenInclude(i => i.Lines).SingleAsync(p => p.Id == id);

        Assert.Equal(UserA, plan.OwnerId);
        Assert.Equal((2027, 2, DemandLevel.Middle), (plan.Year, plan.Persons, plan.Level));
        var item = Assert.Single(plan.Items);
        Assert.Equal(6m, item.DemandAmount);                 // 3 per person x 2 persons
        Assert.Equal(DemandUnit.Plants, item.Unit);
        Assert.Equal(6, item.Plants);
        Assert.Equal(new[] { 2, 2, 2 }, item.Lines.Select(l => l.Plants));
    }

    [Fact]
    public async Task Recalculating_keeps_own_numbers_and_removes_lines_of_retired_varieties()
    {
        await using var container = NewDatabase();
        await container.StartAsync();
        var connection = container.GetConnectionString();
        await PrepareAsync(connection);
        var id = await CreatePlanAsync(connection, UserA);

        // The user changes a number and switches a variety off
        await using (var db = CreateContext(connection, UserA))
        {
            var plan = await db.Plans.Include(p => p.Items).ThenInclude(i => i.Lines).ThenInclude(l => l.Plant).SingleAsync(p => p.Id == id);
            plan.Items[0].PlantsOverride = 9;
            plan.Items[0].Lines.Single(l => l.Plant!.Name == "Roma").Include = false;
            await RebuildAsync(db, plan);
            await db.SaveChangesAsync();
        }

        // A variety is retired and the plan is calculated again
        await using (var db = CreateContext(connection, UserA))
        {
            (await db.Plants.SingleAsync(p => p.Name == "Matina")).StockStatus = StockStatus.Retired;
            await db.SaveChangesAsync();
        }
        await using (var db = CreateContext(connection, UserA))
        {
            var plan = await db.Plans.Include(p => p.Items).ThenInclude(i => i.Lines).SingleAsync(p => p.Id == id);
            await RebuildAsync(db, plan);
            await db.SaveChangesAsync();
        }

        await using var check = CreateContext(connection, UserA);
        var item = await check.PlanItems.Include(i => i.Lines).ThenInclude(l => l.Plant).SingleAsync();
        Assert.Equal(9, item.PlantsOverride);
        Assert.Equal(new[] { "Money Maker", "Roma" }, item.Lines.Select(l => l.Plant!.Name).Order());
        Assert.Equal(2, await check.PlanLines.CountAsync());          // no line of the retired variety is left
        Assert.Equal(9, item.Lines.Sum(l => l.Plants));               // Roma is switched off, Money Maker gets everything
        Assert.False(item.Lines.Single(l => l.Plant!.Name == "Roma").Include);
    }

    [Fact]
    public async Task Own_demand_is_used_instead_of_the_default_and_belongs_to_one_user()
    {
        await using var container = NewDatabase();
        await container.StartAsync();
        var connection = container.GetConnectionString();
        await PrepareAsync(connection);

        await using (var db = CreateContext(connection, UserA))
        {
            var type = await db.PlantTypes.SingleAsync();
            db.UserDemands.Add(new UserDemand { PlantTypeId = type.Id, Min = 5, Max = 5, Unit = DemandUnit.Plants });
            await db.SaveChangesAsync();
        }

        var idA = await CreatePlanAsync(connection, UserA);
        var idB = await CreatePlanAsync(connection, UserB, "Mine");

        await using var dbA = CreateContext(connection, UserA);
        await using var dbB = CreateContext(connection, UserB);
        Assert.Equal(10m, (await dbA.PlanItems.SingleAsync(i => i.PlanId == idA)).DemandAmount);   // 5 x 2 persons
        Assert.Equal(6m, (await dbB.PlanItems.SingleAsync(i => i.PlanId == idB)).DemandAmount);    // default 3 x 2 persons
        Assert.Single(await dbA.UserDemands.ToListAsync());
        Assert.Empty(await dbB.UserDemands.ToListAsync());
    }

    [Fact]
    public async Task Nobody_sees_the_plans_of_another_user()
    {
        await using var container = NewDatabase();
        await container.StartAsync();
        var connection = container.GetConnectionString();
        await PrepareAsync(connection);
        await CreatePlanAsync(connection, UserA);

        await using var dbB = CreateContext(connection, UserB);
        await using var nobody = CreateContext(connection, null);

        Assert.Empty(await dbB.Plans.ToListAsync());
        Assert.Empty(await dbB.PlanItems.ToListAsync());
        Assert.Empty(await dbB.PlanLines.ToListAsync());
        Assert.Empty(await nobody.Plans.ToListAsync());
        Assert.Empty(await nobody.PlanLines.ToListAsync());
    }

    [Fact]
    public async Task Deleting_a_plan_deletes_its_result()
    {
        await using var container = NewDatabase();
        await container.StartAsync();
        var connection = container.GetConnectionString();
        await PrepareAsync(connection);
        var id = await CreatePlanAsync(connection, UserA);

        await using (var db = CreateContext(connection, UserA))
        {
            db.Plans.Remove(await db.Plans.SingleAsync(p => p.Id == id));
            await db.SaveChangesAsync();
        }

        await using var check = CreateContext(connection, UserA);
        Assert.Empty(await check.PlanItems.ToListAsync());
        Assert.Empty(await check.PlanLines.ToListAsync());
    }

    [Fact]
    public async Task Two_plans_of_one_year_need_different_names()
    {
        await using var container = NewDatabase();
        await container.StartAsync();
        var connection = container.GetConnectionString();
        await PrepareAsync(connection);
        await CreatePlanAsync(connection, UserA, "Basic");
        await CreatePlanAsync(connection, UserA, "With guests");

        await using var db = CreateContext(connection, UserA);
        db.Plans.Add(new Plan { Name = "Basic", ValidFrom = new DateOnly(2027, 1, 1), ValidTo = new DateOnly(2027, 12, 31) });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
