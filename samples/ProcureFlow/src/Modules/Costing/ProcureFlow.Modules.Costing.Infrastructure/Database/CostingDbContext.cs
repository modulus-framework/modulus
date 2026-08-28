using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using ProcureFlow.Modules.Costing.Application;
using ProcureFlow.Modules.Costing.Domain.Constants;
using ProcureFlow.Modules.Costing.Domain.Entities;

namespace ProcureFlow.Modules.Costing.Infrastructure.Database;

public sealed class CostingDbContext(
    DbContextOptions<CostingDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider), IUnitOfWork
{
    public DbSet<LandedCostSheet> LandedCostSheets => Set<LandedCostSheet>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schemas.Costing);

        modelBuilder.Entity<LandedCostSheet>(builder =>
        {
            builder.ToTable("landed_cost_sheets");
            builder.Property(s => s.SheetNumber).HasMaxLength(50).IsRequired();
            builder.Property(s => s.Currency).HasMaxLength(3).IsRequired();
            builder.HasIndex(s => new { s.TenantId, s.SheetNumber }).IsUnique();
            builder.HasIndex(s => new { s.TenantId, s.FileId });

            builder.OwnsMany(s => s.Lines, line =>
            {
                line.ToTable("landed_cost_lines");
                line.WithOwner().HasForeignKey("SheetId");
                line.HasKey("SheetId", "Id");
                line.Property(l => l.GoodsValueFcy).HasPrecision(18, 4);
                line.Property(l => l.GoodsValueBdt).HasPrecision(18, 4);
                line.Property(l => l.ReceivedQty).HasPrecision(18, 4);
                line.Property(l => l.NetWeightKg).HasPrecision(18, 4);
                line.Property(l => l.GrossWeightKg).HasPrecision(18, 4);
                line.Property(l => l.VolumeCbm).HasPrecision(18, 4);
                line.Property(l => l.ContainerShare).HasPrecision(9, 6);
                line.Property(l => l.TotalLandedCostBdt).HasPrecision(18, 4);
                line.Property(l => l.UnitLandedCost).HasPrecision(18, 4);

                line.OwnsMany(l => l.Allocations, allocation =>
                {
                    allocation.ToTable("landed_cost_allocations");
                    allocation.WithOwner().HasForeignKey("SheetId", "LineId");
                    allocation.Property<Guid>("Id");
                    allocation.HasKey("SheetId", "LineId", "Id");
                    allocation.Property(a => a.ElementName).HasMaxLength(100).IsRequired();
                    allocation.Property(a => a.AmountBdt).HasPrecision(18, 4);
                });
            });

            builder.OwnsMany(s => s.Elements, element =>
            {
                element.ToTable("cost_elements");
                element.WithOwner().HasForeignKey("SheetId");
                element.HasKey("SheetId", "Id");
                element.Property(e => e.Name).HasMaxLength(100).IsRequired();
                element.Property(e => e.SourceDocType).HasMaxLength(20).IsRequired();
                element.Property(e => e.SourceDocNumber).HasMaxLength(50).IsRequired();
                element.Property(e => e.AmountFcy).HasPrecision(18, 4);
                element.Property(e => e.FxRate).HasPrecision(18, 6);
                element.Property(e => e.AmountBdt).HasPrecision(18, 4);
                element.Property(e => e.SelectedLineIds)
                    .HasColumnType("uuid[]");
            });
        });
    }
}