using Microsoft.EntityFrameworkCore;
using ProcureFlow.Modules.SpendAnalysis.Domain.Entities;
using ProcureFlow.Modules.SpendAnalysis.Domain.Repositories;

namespace ProcureFlow.Modules.SpendAnalysis.Infrastructure.Database;

public sealed class SpendAnalysisDbContext : DbContext
{
    public DbSet<CategoryTaxonomy> Categories => Set<CategoryTaxonomy>();
    public DbSet<PoLineCategoryMapping> PoLineCategoryMappings => Set<PoLineCategoryMapping>();
    public DbSet<SpendCubeEntry> SpendCubeEntries => Set<SpendCubeEntry>();

    public SpendAnalysisDbContext(DbContextOptions<SpendAnalysisDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CategoryTaxonomy>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Code).HasMaxLength(50);
            e.Property(c => c.Name).HasMaxLength(200);
            e.HasIndex(c => new { c.TenantId, c.Code }).IsUnique();
            e.HasOne<CategoryTaxonomy>()
                .WithMany()
                .HasForeignKey(c => c.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PoLineCategoryMapping>(e =>
        {
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.PoLineId).IsUnique();
            e.HasOne<CategoryTaxonomy>()
                .WithMany()
                .HasForeignKey(m => m.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SpendCubeEntry>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.TenantId, s.Period });
            e.HasIndex(s => new { s.TenantId, s.VendorId, s.Period });
            e.HasIndex(s => new { s.TenantId, s.CategoryId, s.Period });
        });
    }
}
