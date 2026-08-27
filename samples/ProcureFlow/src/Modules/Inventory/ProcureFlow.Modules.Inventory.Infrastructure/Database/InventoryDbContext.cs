using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using ProcureFlow.Modules.Inventory.Application;
using ProcureFlow.Modules.Inventory.Domain.Constants;
using ProcureFlow.Modules.Inventory.Domain.Entities;

namespace ProcureFlow.Modules.Inventory.Infrastructure.Database;

public sealed class InventoryDbContext(
    DbContextOptions<InventoryDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider), IUnitOfWork
{
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<Grn> Grns => Set<Grn>();
    public DbSet<QcInspection> QcInspections => Set<QcInspection>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<InventoryValueLedgerEntry> LedgerEntries => Set<InventoryValueLedgerEntry>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schemas.Inventory);

        modelBuilder.Entity<StockItem>(builder =>
        {
            builder.ToTable("stock_items");
            builder.Property(s => s.Sku).HasMaxLength(50).IsRequired();
            builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
            builder.Property(s => s.Uom).HasMaxLength(10).IsRequired();
            builder.Property(s => s.QuantityOnHand).HasPrecision(18, 4);
            builder.Property(s => s.WeightedAverageCost).HasPrecision(18, 4);
            builder.HasIndex(s => new { s.TenantId, s.SiteId, s.ItemId }).IsUnique();
        });

        modelBuilder.Entity<Grn>(builder =>
        {
            builder.ToTable("grns");
            builder.Property(g => g.GrnNumber).HasMaxLength(50).IsRequired();
            builder.Property(g => g.CreatedBy).HasMaxLength(100).IsRequired();
            builder.HasIndex(g => new { g.TenantId, g.GrnNumber }).IsUnique();

            builder.OwnsMany(g => g.Lines, line =>
            {
                line.ToTable("grn_lines");
                line.WithOwner().HasForeignKey("GrnId");
                line.HasKey("GrnId", "Id");
                line.Property(l => l.SourceDocNumber).HasMaxLength(50).IsRequired();
                line.Property(l => l.OrderedQty).HasPrecision(18, 4);
                line.Property(l => l.ReceivedQty).HasPrecision(18, 4);
                line.Property(l => l.ProvisionalUnitCost).HasPrecision(18, 4);
            });
        });

        modelBuilder.Entity<QcInspection>(builder =>
        {
            builder.ToTable("qc_inspections");
            builder.Property(q => q.InspectedBy).HasMaxLength(100).IsRequired();
            builder.HasIndex(q => new { q.TenantId, q.GrnId }).IsUnique();

            builder.OwnsMany(q => q.Lines, line =>
            {
                line.ToTable("qc_inspection_lines");
                line.WithOwner().HasForeignKey("InspectionId");
                line.HasKey("InspectionId", "Id");
                line.Property(l => l.Note).HasMaxLength(500);
                line.Property(l => l.InspectedQty).HasPrecision(18, 4);
                line.Property(l => l.AcceptedQty).HasPrecision(18, 4);
            });
        });

        modelBuilder.Entity<Batch>(builder =>
        {
            builder.ToTable("batches");
            builder.Property(b => b.BatchNo).HasMaxLength(50).IsRequired();
            builder.Property(b => b.SourceDoc).HasMaxLength(100);
            builder.Property(b => b.Quantity).HasPrecision(18, 4);
            builder.Property(b => b.UnitCost).HasPrecision(18, 4);
            builder.HasIndex(b => new { b.TenantId, b.SiteId, b.ItemId, b.BatchNo }).IsUnique();
        });

        modelBuilder.Entity<InventoryValueLedgerEntry>(builder =>
        {
            builder.ToTable("inventory_value_ledger");
            builder.Property(e => e.SourceDoc).HasMaxLength(100).IsRequired();
            builder.Property(e => e.Reference).HasMaxLength(100).IsRequired();
            builder.Property(e => e.Quantity).HasPrecision(18, 4);
            builder.Property(e => e.UnitCost).HasPrecision(18, 4);
            builder.Property(e => e.ValueDelta).HasPrecision(18, 4);
            builder.HasIndex(e => new { e.TenantId, e.SiteId, e.ItemId, e.OccurredAtUtc });
        });
    }
}