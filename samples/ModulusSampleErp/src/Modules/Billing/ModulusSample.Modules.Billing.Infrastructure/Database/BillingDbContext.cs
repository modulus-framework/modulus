using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.Abstractions;
using Modulus.Events;
using ModulusSample.Modules.Billing.Domain.Entities;

namespace ModulusSample.Modules.Billing.Infrastructure.Database;

public sealed class BillingDbContext(
    DbContextOptions<BillingDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher domainEventDispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, domainEventDispatcher, serviceProvider)
{
    public DbSet<Invoice> Invoices { get; set; } = null!;
    public DbSet<Payment> Payments { get; set; } = null!;
    public DbSet<CreditNote> CreditNotes { get; set; } = null!;

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("billing");
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.InvoiceNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Currency).IsRequired().HasMaxLength(3);
            entity.OwnsMany(e => e.Lines, nav =>
            {
                nav.ToTable("invoice_lines");
                nav.WithOwner().HasForeignKey("invoice_id");
                nav.HasKey(nameof(InvoiceLine.Id));
                nav.Property(l => l.Description).IsRequired().HasMaxLength(500);
            });
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PaymentNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.PaymentMethod).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.ReferenceNumber).HasMaxLength(100);
        });

        modelBuilder.Entity<CreditNote>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CreditNoteNumber).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Reason).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
        });
    }
}
