using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SeedPlanner.Domain;

namespace SeedPlanner.Infrastructure;

public class SeedPlannerDbContext(DbContextOptions<SeedPlannerDbContext> options, ICurrentUser currentUser)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<PlantType> PlantTypes => Set<PlantType>();
    public DbSet<Plant> Plants => Set<Plant>();
    public DbSet<SeasonWindow> SeasonWindows => Set<SeasonWindow>();
    public DbSet<PlantTypeTranslation> PlantTypeTranslations => Set<PlantTypeTranslation>();
    public DbSet<PlantTypeCompanion> PlantTypeCompanions => Set<PlantTypeCompanion>();

    // Used by the query filters. EF evaluates it for each context instance (= each request).
    // Nobody signed in gives null, and then the filters match no rows at all.
    private string? CurrentUserId => currentUser.UserId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);   // creates the Identity tables

        // Identity names its tables in PascalCase; the rest of the database uses snake_case
        modelBuilder.Entity<IdentityUser>().ToTable("asp_net_users");
        modelBuilder.Entity<IdentityRole>().ToTable("asp_net_roles");
        modelBuilder.Entity<IdentityUserRole<string>>().ToTable("asp_net_user_roles");
        modelBuilder.Entity<IdentityUserClaim<string>>().ToTable("asp_net_user_claims");
        modelBuilder.Entity<IdentityUserLogin<string>>().ToTable("asp_net_user_logins");
        modelBuilder.Entity<IdentityRoleClaim<string>>().ToTable("asp_net_role_claims");
        modelBuilder.Entity<IdentityUserToken<string>>().ToTable("asp_net_user_tokens");

        // Shared data: the same for all users
        modelBuilder.Entity<PlantType>(e =>
        {
            e.HasIndex(t => t.Name).IsUnique();
            e.Property(t => t.Category).HasConversion<string>();
            e.Property(t => t.NutrientDemand).HasConversion<string>();
            e.Property(t => t.GerminationType).HasConversion<string>();
            e.Property(t => t.DemandUnit).HasConversion<string>();
            e.Property(t => t.Exposure).HasConversion<string>();
        });

        modelBuilder.Entity<PlantTypeTranslation>()
            .HasIndex(t => new { t.PlantTypeId, t.Language })
            .IsUnique();

        modelBuilder.Entity<PlantType>()
            .HasMany(t => t.Translations)
            .WithOne()
            .HasForeignKey(t => t.PlantTypeId);

        // Neighbours: each pair is stored once, with the smaller id first
        modelBuilder.Entity<PlantTypeCompanion>(e =>
        {
            e.Property(c => c.Relation).HasConversion<string>();
            e.Property(c => c.Note).HasMaxLength(200);
            e.HasIndex(c => new { c.PlantTypeId, c.OtherPlantTypeId }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_plant_type_companions_order", "plant_type_id < other_plant_type_id"));
            e.HasOne<PlantType>().WithMany().HasForeignKey(c => c.PlantTypeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PlantType>().WithMany().HasForeignKey(c => c.OtherPlantTypeId).OnDelete(DeleteBehavior.Cascade);
        });

        // Personal data: every query only sees the rows of the signed-in user
        modelBuilder.Entity<Plant>(e =>
        {
            e.HasIndex(p => new { p.OwnerId, p.PlantTypeId, p.Name }).IsUnique();
            e.Property(p => p.SeedType).HasConversion<string>();
            e.Property(p => p.StockStatus).HasConversion<string>();
            e.Property(p => p.CultivationMethod).HasConversion<string>();
            e.Property(p => p.Exposure).HasConversion<string>();
            e.HasMany(p => p.SeasonWindows).WithOne(w => w.Plant).HasForeignKey(w => w.PlantId);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(p => p.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(p => p.OwnerId == CurrentUserId);
        });

        modelBuilder.Entity<SeasonWindow>(e =>
        {
            e.Property(w => w.Kind).HasConversion<string>();
            e.HasQueryFilter(w => w.Plant!.OwnerId == CurrentUserId);
        });
    }

    // New plants always belong to the signed-in user, so no page can forget to set the owner
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SetOwnerOfNewPlants();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SetOwnerOfNewPlants();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void SetOwnerOfNewPlants()
    {
        foreach (var entry in ChangeTracker.Entries<Plant>()
                     .Where(e => e.State == EntityState.Added && string.IsNullOrEmpty(e.Entity.OwnerId)))
        {
            entry.Entity.OwnerId = currentUser.UserId
                ?? throw new InvalidOperationException("A plant can only be saved by a signed-in user.");
        }
    }
}