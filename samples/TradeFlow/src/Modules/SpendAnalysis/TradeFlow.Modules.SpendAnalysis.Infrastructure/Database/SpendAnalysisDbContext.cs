using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using TradeFlow.Modules.SpendAnalysis.Application;
using TradeFlow.Modules.SpendAnalysis.Domain.Constants;
using TradeFlow.Modules.SpendAnalysis.Domain.Entities;
using TradeFlow.Modules.SpendAnalysis.Domain.Repositories;

namespace TradeFlow.Modules.SpendAnalysis.Infrastructure.Database;

public sealed class SpendAnalysisDbContext(
    DbContextOptions<SpendAnalysisDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider), IUnitOfWork
{
    public DbSet<CategoryTaxonomy> Categories => Set<CategoryTaxonomy>();
    public DbSet<PoLineCategoryMapping> PoLineCategoryMappings => Set<PoLineCategoryMapping>();
    public DbSet<SpendCubeEntry> SpendCubeEntries => Set<SpendCubeEntry>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schemas.SpendAnalysis);

        modelBuilder.Entity<CategoryTaxonomy>(e =>
        {
            e.ToTable("categories");
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
            e.ToTable("po_line_category_mappings");
            e.HasKey(m => m.Id);
            e.HasIndex(m => m.PoLineId).IsUnique();
            e.HasOne<CategoryTaxonomy>()
                .WithMany()
                .HasForeignKey(m => m.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SpendCubeEntry>(e =>
        {
            e.ToTable("spend_cube_entries");
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.TenantId, s.Period });
            e.HasIndex(s => new { s.TenantId, s.VendorId, s.Period });
            e.HasIndex(s => new { s.TenantId, s.CategoryId, s.Period });
        });
    }
}
