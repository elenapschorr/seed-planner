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
    public DbSet<PotGroup> PotGroups => Set<PotGroup>();
    public DbSet<Pot> Pots => Set<Pot>();
    public DbSet<Planting> Plantings => Set<Planting>();
    public DbSet<Saucer> Saucers => Set<Saucer>();
    public DbSet<RaisedBed> RaisedBeds => Set<RaisedBed>();
    public DbSet<UserDemand> UserDemands => Set<UserDemand>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PlanItem> PlanItems => Set<PlanItem>();
    public DbSet<PlanLine> PlanLines => Set<PlanLine>();

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
            e.Property(t => t.Lifecycle).HasConversion<string>();
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
            e.Property(p => p.Propagation).HasConversion<string>().HasDefaultValue(Propagation.Seed);
            e.Property(p => p.SeedType).HasConversion<string>();
            e.Property(p => p.StockStatus).HasConversion<string>();
            e.Property(p => p.SowingMethod).HasConversion<string>();
            e.Property(p => p.GrowingPlace).HasConversion<string>();
            e.Property(p => p.Exposure).HasConversion<string>();
            e.Property(p => p.HeightEvidence).HasConversion<string>();
            e.HasMany(p => p.SeasonWindows).WithOne(w => w.Plant).HasForeignKey(w => w.PlantId);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(p => p.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(p => p.OwnerId == CurrentUserId);
        });

        modelBuilder.Entity<SeasonWindow>(e =>
        {
            e.Property(w => w.Kind).HasConversion<string>();
            e.Property(w => w.StartPart).HasConversion<string>().HasMaxLength(10).HasDefaultValue(MonthPart.Whole);
            e.Property(w => w.EndPart).HasConversion<string>().HasMaxLength(10).HasDefaultValue(MonthPart.Whole);
            e.HasQueryFilter(w => w.Plant!.OwnerId == CurrentUserId);
        });

        // Pot groups, pots, saucers, beds and plantings: personal data like the plants
        modelBuilder.Entity<PotGroup>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(100);
            e.Property(p => p.Kind).HasConversion<string>();
            e.Property(p => p.Shape).HasConversion<string>();
            e.Property(p => p.TopCm).HasPrecision(6, 1);
            e.Property(p => p.BottomCm).HasPrecision(6, 1);
            e.Property(p => p.HeightCm).HasPrecision(6, 1);
            e.Property(p => p.Color).HasMaxLength(50);
            e.Property(p => p.Code).HasMaxLength(50);
            e.Property(p => p.Note).HasMaxLength(200);
            e.HasIndex(p => new { p.OwnerId, p.Name }).IsUnique();
            e.HasMany(p => p.Pots).WithOne(x => x.PotGroup).HasForeignKey(x => x.PotGroupId).OnDelete(DeleteBehavior.Cascade);
            // Deleting a saucer keeps the pots; they just have no saucer any more
            e.HasOne(p => p.Saucer).WithMany().HasForeignKey(p => p.SaucerId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(p => p.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_pot_groups_set_size", "set_size >= 1");
                t.HasCheckConstraint("ck_pot_groups_measures",
                    "(top_cm IS NULL OR top_cm > 0) AND (bottom_cm IS NULL OR bottom_cm > 0) AND (height_cm IS NULL OR height_cm > 0)");
            });
            e.HasQueryFilter(p => p.OwnerId == CurrentUserId);
        });

        modelBuilder.Entity<Pot>(e =>
        {
            e.Property(p => p.Note).HasMaxLength(200);
            e.HasMany(p => p.Plantings).WithOne(x => x.Pot).HasForeignKey(x => x.PotId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(p => p.PotGroup!.OwnerId == CurrentUserId);
        });

        modelBuilder.Entity<Planting>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(200);
            // A plant type that is planted somewhere cannot be deleted
            e.HasOne(x => x.PlantType).WithMany().HasForeignKey(x => x.PlantTypeId).OnDelete(DeleteBehavior.Restrict);
            // Deleting a variety keeps the planting; it is then only linked to the plant type
            e.HasOne(x => x.Plant).WithMany().HasForeignKey(x => x.PlantId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.PotId);
            e.HasIndex(x => x.RaisedBedId);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_plantings_count", "plant_count IS NULL OR plant_count >= 1");
                t.HasCheckConstraint("ck_plantings_one_place", "num_nonnulls(pot_id, raised_bed_id) <= 1");
            });
            e.HasQueryFilter(x => x.OwnerId == CurrentUserId);
        });

        modelBuilder.Entity<Saucer>(e =>
        {
            e.Property(s => s.DiameterCm).HasPrecision(6, 1);
            e.Property(s => s.Color).HasMaxLength(50);
            e.Property(s => s.Note).HasMaxLength(200);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(s => s.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_saucers_diameter", "diameter_cm > 0");
                t.HasCheckConstraint("ck_saucers_quantity", "quantity >= 0");
            });
            e.HasQueryFilter(s => s.OwnerId == CurrentUserId);
        });

        modelBuilder.Entity<RaisedBed>(e =>
        {
            e.Property(b => b.Kind).HasConversion<string>().HasDefaultValue(BedKind.Raised);
            e.Property(b => b.Shape).HasConversion<string>();
            e.Property(b => b.Name).HasMaxLength(100);
            e.Property(b => b.WidthCm).HasPrecision(6, 1);
            e.Property(b => b.LengthCm).HasPrecision(6, 1);
            e.Property(b => b.HeightCm).HasPrecision(6, 1);
            e.Property(b => b.Note).HasMaxLength(200);
            e.HasIndex(b => new { b.OwnerId, b.Name }).IsUnique();
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(b => b.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("ck_raised_beds_measures",
                "width_cm > 0 AND length_cm > 0 AND (height_cm IS NULL OR height_cm > 0)"));
            e.HasMany(b => b.Plantings).WithOne(x => x.RaisedBed).HasForeignKey(x => x.RaisedBedId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(b => b.OwnerId == CurrentUserId);
        });

        // Own demand per person and plans (settings and results): personal data
        modelBuilder.Entity<UserDemand>(e =>
        {
            e.Property(d => d.Min).HasPrecision(8, 2);
            e.Property(d => d.Max).HasPrecision(8, 2);
            e.Property(d => d.Unit).HasConversion<string>();
            e.HasIndex(d => new { d.OwnerId, d.PlantTypeId }).IsUnique();
            // Deleting a plant type takes the personal values for it along
            e.HasOne(d => d.PlantType).WithMany().HasForeignKey(d => d.PlantTypeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(d => d.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("ck_user_demands_range",
                "(\"min\" IS NULL OR \"min\" >= 0) AND (\"max\" IS NULL OR \"max\" >= 0)"));
            e.HasQueryFilter(d => d.OwnerId == CurrentUserId);
        });

        modelBuilder.Entity<Plan>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(100);
            e.Property(p => p.Level).HasConversion<string>();
            e.HasIndex(p => new { p.OwnerId, p.Year, p.Name }).IsUnique();
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(p => p.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Items).WithOne(i => i.Plan).HasForeignKey(i => i.PlanId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_plans_persons", "persons >= 1");
                t.HasCheckConstraint("ck_plans_custom_percent", "custom_percent BETWEEN 0 AND 100");
            });
            e.Ignore(p => p.ShoppingList);
            e.HasQueryFilter(p => p.OwnerId == CurrentUserId);
        });

        modelBuilder.Entity<PlanItem>(e =>
        {
            e.Property(i => i.DemandAmount).HasPrecision(10, 2);
            e.Property(i => i.Unit).HasConversion<string>();
            e.HasIndex(i => new { i.PlanId, i.PlantTypeId }).IsUnique();
            // Results can be calculated again, so a plant type that is deleted takes its plan items with it
            e.HasOne(i => i.PlantType).WithMany().HasForeignKey(i => i.PlantTypeId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(i => i.Lines).WithOne(l => l.PlanItem).HasForeignKey(l => l.PlanItemId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(i => i.Plants);
            e.Ignore(i => i.NeedsNumber);
            e.HasQueryFilter(i => i.Plan!.OwnerId == CurrentUserId);
        });

        modelBuilder.Entity<PlanLine>(e =>
        {
            e.HasIndex(l => new { l.PlanItemId, l.PlantId }).IsUnique();
            e.HasOne(l => l.Plant).WithMany().HasForeignKey(l => l.PlantId).OnDelete(DeleteBehavior.Cascade);
            e.Ignore(l => l.Plants);
            e.Ignore(l => l.ToBuy);
            e.HasQueryFilter(l => l.PlanItem!.Plan!.OwnerId == CurrentUserId);
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
        // Pots, saucers and raised beds
        foreach (var entry in ChangeTracker.Entries<IOwnedByUser>()
                     .Where(e => e.State == EntityState.Added && string.IsNullOrEmpty(e.Entity.OwnerId)))
        {
            entry.Entity.OwnerId = currentUser.UserId
                ?? throw new InvalidOperationException("Only a signed-in user can save personal data.");
        }

        foreach (var entry in ChangeTracker.Entries<Plant>()
                     .Where(e => e.State == EntityState.Added && string.IsNullOrEmpty(e.Entity.OwnerId)))
        {
            entry.Entity.OwnerId = currentUser.UserId
                ?? throw new InvalidOperationException("A plant can only be saved by a signed-in user.");
        }
    }
}
