using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using ModulusSample.Modules.Purchasing.Domain.Entities;

namespace ModulusSample.Modules.Purchasing.Infrastructure.Database;

public sealed class PurchasingDbContext(
    DbContextOptions<PurchasingDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider)
{
    public const string SchemaName = "purchasing";

    public DbSet<PurchaseRequisition> Requisitions => Set<PurchaseRequisition>();
    public DbSet<PurchaseOrder> Orders => Set<PurchaseOrder>();
    public DbSet<GoodsReceipt> Receipts => Set<GoodsReceipt>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(SchemaName);

        // Configure PurchaseRequisition
        modelBuilder.Entity<PurchaseRequisition>()
            .OwnsMany(r => r.Lines, lb =>
            {
                lb.WithOwner().HasForeignKey("RequisitionId");
                lb.HasKey(nameof(RequisitionLine.Id));
                lb.ToTable("RequisitionLines", SchemaName);
            });

        // Configure PurchaseOrder
        modelBuilder.Entity<PurchaseOrder>()
            .OwnsMany(o => o.Lines, lb =>
            {
                lb.WithOwner().HasForeignKey("OrderId");
                lb.HasKey(nameof(PurchaseOrderLine.Id));
                lb.ToTable("OrderLines", SchemaName);
            });

        // Configure GoodsReceipt
        modelBuilder.Entity<GoodsReceipt>()
            .OwnsMany(r => r.Lines, lb =>
            {
                lb.WithOwner().HasForeignKey("ReceiptId");
                lb.HasKey(nameof(ReceiptLine.Id));
                lb.ToTable("ReceiptLines", SchemaName);
            });
    }
}
