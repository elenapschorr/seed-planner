using Microsoft.EntityFrameworkCore;
using SeedPlanner.Domain;

namespace SeedPlanner.Infrastructure;

public class SeedPlannerDbContext(DbContextOptions<SeedPlannerDbContext> options)
    : DbContext(options)
{
    public DbSet<PlantType> PlantTypes => Set<PlantType>();
    public DbSet<Plant> Plants => Set<Plant>();
    public DbSet<SeasonWindow> SeasonWindows => Set<SeasonWindow>();
    public DbSet<PlantTypeTranslation> PlantTypeTranslations => Set<PlantTypeTranslation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlantType>(e =>
        {
            e.HasIndex(t => t.Name).IsUnique();
            e.Property(t => t.Category).HasConversion<string>();
            e.Property(t => t.NutrientDemand).HasConversion<string>();
            e.Property(t => t.GerminationType).HasConversion<string>();
            e.Property(t => t.DemandUnit).HasConversion<string>();
            e.Property(t => t.Exposure).HasConversion<string>();
            e.HasMany(t => t.Translations).WithOne().HasForeignKey(t => t.PlantTypeId);
        });

        modelBuilder.Entity<Plant>(e =>
        {
            e.HasIndex(p => new { p.PlantTypeId, p.Name }).IsUnique();
            e.Property(p => p.SeedType).HasConversion<string>();
            e.Property(p => p.StockStatus).HasConversion<string>();
            e.Property(p => p.CultivationMethod).HasConversion<string>();
            e.Property(p => p.Exposure).HasConversion<string>();
            e.HasMany(p => p.SeasonWindows).WithOne().HasForeignKey(w => w.PlantId);
        });

        modelBuilder.Entity<SeasonWindow>()
            .Property(w => w.Kind).HasConversion<string>();

        modelBuilder.Entity<PlantTypeTranslation>()
            .HasIndex(t => new { t.PlantTypeId, t.Language })
            .IsUnique();
            
    }
}