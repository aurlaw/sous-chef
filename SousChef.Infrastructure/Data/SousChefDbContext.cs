using Microsoft.EntityFrameworkCore;

namespace SousChef.Infrastructure.Data;

public class SousChefDbContext(DbContextOptions<SousChefDbContext> options) : DbContext(options)
{
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<RecipeStep> RecipeSteps => Set<RecipeStep>();
    public DbSet<ExtractionJob> ExtractionJobs => Set<ExtractionJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<Recipe>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Tags).HasColumnType("text[]");
            e.Property(r => r.Embedding).HasColumnType("vector(1536)");
            e.Property(r => r.CreatedAt).HasDefaultValueSql("now()");
            e.HasMany(r => r.Ingredients).WithOne(i => i.Recipe).HasForeignKey(i => i.RecipeId);
            e.HasMany(r => r.Steps).WithOne(s => s.Recipe).HasForeignKey(s => s.RecipeId);
        });

        modelBuilder.Entity<ExtractionJob>(e =>
        {
            e.HasKey(j => j.Id);
            e.Property(j => j.Status)
                .HasConversion<string>()
                .HasDefaultValue(ExtractionJobStatus.Pending);
            e.Property(j => j.ExtractedText).HasColumnType("text");
            e.Property(j => j.ExtractedData).HasColumnType("jsonb");
            e.Property(j => j.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(j => j.Status);
        });
    }
}
