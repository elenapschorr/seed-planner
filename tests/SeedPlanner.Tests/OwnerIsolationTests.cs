using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SeedPlanner.Domain;
using SeedPlanner.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;

namespace SeedPlanner.Tests;

/// <summary>
/// Tests with a real PostgreSQL database (started in Docker): a user must never see or change the data of another user.
/// </summary>
public class OwnerIsolationTests
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
        new PostgreSqlBuilder().WithImage("postgres:17").Build();

    // Creates the schema, two users, one plant type and one plant (with a sowing window) for each user
    private static async Task PrepareAsync(string connectionString)
    {
        await using (var setup = CreateContext(connectionString, null))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Users.Add(new IdentityUser { Id = UserA, UserName = "a@example.com", NormalizedUserName = "A@EXAMPLE.COM" });
            setup.Users.Add(new IdentityUser { Id = UserB, UserName = "b@example.com", NormalizedUserName = "B@EXAMPLE.COM" });
            setup.PlantTypes.Add(new PlantType { Name = "Tomato" });
            await setup.SaveChangesAsync();
        }

        foreach (var (userId, plantName) in new[] { (UserA, "Money Maker"), (UserB, "Roma") })
        {
            await using var db = CreateContext(connectionString, userId);
            var type = await db.PlantTypes.SingleAsync();
            var plant = new Plant { Name = plantName, PlantTypeId = type.Id };
            plant.SeasonWindows.Add(new SeasonWindow { Kind = WindowKind.Sowing, StartMonth = 3, EndMonth = 5 });
            db.Plants.Add(plant);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task NewPlant_BelongsToTheSignedInUser()
    {
        await using var container = NewDatabase();
        await container.StartAsync();
        var connection = container.GetConnectionString();
        await PrepareAsync(connection);

        await using var db = CreateContext(connection, UserA);
        var plant = await db.Plants.SingleAsync();

        Assert.Equal(UserA, plant.OwnerId);
    }

    [Fact]
    public async Task EachUser_OnlySeesOwnPlants()
    {
        await using var container = NewDatabase();
        await container.StartAsync();
        var connection = container.GetConnectionString();
        await PrepareAsync(connection);

        await using var dbA = CreateContext(connection, UserA);
        await using var dbB = CreateContext(connection, UserB);

        Assert.Equal(new[] { "Money Maker" }, await dbA.Plants.Select(p => p.Name).ToListAsync());
        Assert.Equal(new[] { "Roma" }, await dbB.Plants.Select(p => p.Name).ToListAsync());
    }

    [Fact]
    public async Task NobodySignedIn_SeesNoPlantsAndNoSeasonWindows()
    {
        await using var container = NewDatabase();
        await container.StartAsync();
        var connection = container.GetConnectionString();
        await PrepareAsync(connection);

        await using var db = CreateContext(connection, null);

        Assert.Empty(await db.Plants.ToListAsync());
        Assert.Empty(await db.SeasonWindows.ToListAsync());
    }

    [Fact]
    public async Task User_CannotLoadPlantOrSeasonWindowsOfAnotherUser()
    {
        await using var container = NewDatabase();
        await container.StartAsync();
        var connection = container.GetConnectionString();
        await PrepareAsync(connection);

        int plantIdOfB;
        await using (var dbB = CreateContext(connection, UserB))
        {
            plantIdOfB = (await dbB.Plants.SingleAsync()).Id;
        }

        await using var dbA = CreateContext(connection, UserA);

        Assert.Null(await dbA.Plants.FirstOrDefaultAsync(p => p.Id == plantIdOfB));
        Assert.Empty(await dbA.SeasonWindows.Where(w => w.PlantId == plantIdOfB).ToListAsync());
        Assert.Single(await dbA.SeasonWindows.ToListAsync());   // only the window of A's own plant
    }

    [Fact]
    public async Task PlantTypes_AreSharedBetweenUsers()
    {
        await using var container = NewDatabase();
        await container.StartAsync();
        var connection = container.GetConnectionString();
        await PrepareAsync(connection);

        await using var dbA = CreateContext(connection, UserA);
        await using var dbB = CreateContext(connection, UserB);

        Assert.Single(await dbA.PlantTypes.ToListAsync());
        Assert.Single(await dbB.PlantTypes.ToListAsync());
    }

    [Fact]
    public async Task TwoUsers_CanHavePlantsWithTheSameName()
    {
        await using var container = NewDatabase();
        await container.StartAsync();
        var connection = container.GetConnectionString();
        await PrepareAsync(connection);

        await using var db = CreateContext(connection, UserB);
        var type = await db.PlantTypes.SingleAsync();
        db.Plants.Add(new Plant { Name = "Money Maker", PlantTypeId = type.Id });   // A already has this name

        await db.SaveChangesAsync();   // must not throw: names are unique per owner

        Assert.Equal(2, await db.Plants.CountAsync());
    }

    [Fact]
    public async Task SavingPlantWithoutUser_Throws()
    {
        await using var container = NewDatabase();
        await container.StartAsync();
        var connection = container.GetConnectionString();
        await PrepareAsync(connection);

        await using var db = CreateContext(connection, null);
        var type = await db.PlantTypes.SingleAsync();
        db.Plants.Add(new Plant { Name = "Orphan", PlantTypeId = type.Id });

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
}
