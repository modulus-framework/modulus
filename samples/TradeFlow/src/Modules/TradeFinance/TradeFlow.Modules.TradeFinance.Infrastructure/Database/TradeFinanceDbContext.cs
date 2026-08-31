using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using TradeFlow.Modules.TradeFinance.Application;
using TradeFlow.Modules.TradeFinance.Domain.Constants;
using TradeFlow.Modules.TradeFinance.Domain.Entities;

namespace TradeFlow.Modules.TradeFinance.Infrastructure.Database;

public sealed class TradeFinanceDbContext(
    DbContextOptions<TradeFinanceDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider), IUnitOfWork
{
    public DbSet<LetterOfCredit> LettersOfCredit => Set<LetterOfCredit>();
    public DbSet<TtPayment> TtPayments => Set<TtPayment>();
    public DbSet<SwiftMessage> SwiftMessages => Set<SwiftMessage>();
    public DbSet<BankFacility> BankFacilities => Set<BankFacility>();
    public DbSet<PaymentObligation> PaymentObligations => Set<PaymentObligation>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schemas.TradeFinance);

        modelBuilder.Entity<LetterOfCredit>(builder =>
        {
            builder.ToTable("letters_of_credit");
            builder.Property(l => l.LcNumber).HasMaxLength(50).IsRequired();
            builder.Property(l => l.Currency).HasMaxLength(3).IsRequired();
            builder.Property(l => l.BeneficiaryName).HasMaxLength(200).IsRequired();
            builder.Property(l => l.Incoterm).HasMaxLength(10).IsRequired();
            builder.Property(l => l.PortOfLoading).HasMaxLength(100).IsRequired();
            builder.Property(l => l.PortOfDischarge).HasMaxLength(100).IsRequired();
            builder.Property(l => l.CreatedBy).HasMaxLength(100).IsRequired();
            builder.Property(l => l.CancellationReason).HasMaxLength(500);
            builder.Property(l => l.TolerancePct).HasPrecision(9, 6);
            builder.Property(l => l.MarginPct).HasPrecision(9, 6);
            builder.Property(l => l.Amount).HasPrecision(18, 4);
            builder.Property(l => l.BookingFxRate).HasPrecision(18, 6);
            builder.Property(l => l.RealizedFxRate).HasPrecision(18, 6);
            builder.HasIndex(l => new { l.TenantId, l.LcNumber }).IsUnique();

            builder.OwnsMany(l => l.Charges, charge =>
            {
                charge.ToTable("lc_charges");
                charge.WithOwner().HasForeignKey("LcId");
                charge.HasKey("LcId", "Id");
                charge.Property(c => c.Currency).HasMaxLength(3).IsRequired();
                charge.Property(c => c.RefDoc).HasMaxLength(100);
                charge.Property(c => c.Amount).HasPrecision(18, 4);
            });

            builder.OwnsMany(l => l.Amendments, amendment =>
            {
                amendment.ToTable("lc_amendments");
                amendment.WithOwner().HasForeignKey("LcId");
                amendment.HasKey("LcId", "Id");
                amendment.Property(a => a.ReasonCode).HasMaxLength(50).IsRequired();
                amendment.Property(a => a.Reason).HasMaxLength(500).IsRequired();
                amendment.Property(a => a.RequestedBy).HasMaxLength(100).IsRequired();
                amendment.Property(a => a.ApprovedBy).HasMaxLength(100);
                amendment.Property(a => a.ValueDelta).HasPrecision(18, 4);
            });

            builder.OwnsMany(l => l.Presentations, presentation =>
            {
                presentation.ToTable("lc_presentations");
                presentation.WithOwner().HasForeignKey("LcId");
                presentation.HasKey("LcId", "Id");
                presentation.Property(p => p.PresentationNo).HasMaxLength(50).IsRequired();
                presentation.Property(p => p.DocumentRefs).HasMaxLength(2000);

                presentation.OwnsMany(p => p.Discrepancies, discrepancy =>
                {
                    discrepancy.ToTable("lc_presentation_discrepancies");
                    discrepancy.WithOwner().HasForeignKey("LcId", "PresentationId");
                    discrepancy.Property<Guid>("Id");
                    discrepancy.HasKey("LcId", "PresentationId", "Id");
                    discrepancy.Property(d => d.Code).HasMaxLength(20).IsRequired();
                    discrepancy.Property(d => d.Description).HasMaxLength(500).IsRequired();
                });
            });

            builder.OwnsMany(l => l.MarginLedger, entry =>
            {
                entry.ToTable("lc_margin_ledger");
                entry.WithOwner().HasForeignKey("LcId");
                entry.HasKey("LcId", "Id");
                entry.Property(e => e.Currency).HasMaxLength(3).IsRequired();
                entry.Property(e => e.Reason).HasMaxLength(500).IsRequired();
                entry.Property(e => e.Amount).HasPrecision(18, 4);
            });

            builder.OwnsMany(l => l.Maturities, maturity =>
            {
                maturity.ToTable("lc_maturities");
                maturity.WithOwner().HasForeignKey("LcId");
                maturity.HasKey("LcId", "Id");
                maturity.Property(m => m.Currency).HasMaxLength(3).IsRequired();
                maturity.Property(m => m.Amount).HasPrecision(18, 4);
            });
        });

        modelBuilder.Entity<TtPayment>(builder =>
        {
            builder.ToTable("tt_payments");
            builder.Property(t => t.TtNumber).HasMaxLength(50).IsRequired();
            builder.Property(t => t.BeneficiaryName).HasMaxLength(200).IsRequired();
            builder.Property(t => t.Currency).HasMaxLength(3).IsRequired();
            builder.Property(t => t.BankRef).HasMaxLength(100).IsRequired();
            builder.Property(t => t.CreatedBy).HasMaxLength(100).IsRequired();
            builder.Property(t => t.Amount).HasPrecision(18, 4);
            builder.Property(t => t.FxRate).HasPrecision(18, 6);
            builder.Property(t => t.Charges).HasPrecision(18, 4);
            builder.HasIndex(t => new { t.TenantId, t.TtNumber }).IsUnique();
        });

        modelBuilder.Entity<SwiftMessage>(builder =>
        {
            builder.ToTable("swift_messages");
            builder.Property(s => s.MtType).HasMaxLength(10).IsRequired();
            builder.Property(s => s.Reference).HasMaxLength(100).IsRequired();
            builder.Property(s => s.Direction).HasMaxLength(10).IsRequired();
            builder.Property(s => s.ContentRef).HasMaxLength(200);
            builder.HasIndex(s => new { s.TenantId, s.Reference }).IsUnique();
        });

        modelBuilder.Entity<BankFacility>(builder =>
        {
            builder.ToTable("bank_facilities");
            builder.Property(f => f.Currency).HasMaxLength(3).IsRequired();
            builder.Property(f => f.LimitAmount).HasPrecision(18, 4);
            builder.HasIndex(f => new { f.TenantId, f.BankId }).IsUnique();

            builder.OwnsMany(f => f.Entries, entry =>
            {
                entry.ToTable("facility_exposure_entries");
                entry.WithOwner().HasForeignKey("FacilityId");
                entry.HasKey("FacilityId", "Id");
                entry.Property(e => e.ReferenceNumber).HasMaxLength(100).IsRequired();
                entry.Property(e => e.Reason).HasMaxLength(500).IsRequired();
                entry.Property(e => e.Amount).HasPrecision(18, 4);
            });
        });

        modelBuilder.Entity<PaymentObligation>(builder =>
        {
            builder.ToTable("payment_obligations");
            builder.Property(o => o.Type).HasMaxLength(20).IsRequired();
            builder.Property(o => o.SourceNumber).HasMaxLength(50).IsRequired();
            builder.Property(o => o.Currency).HasMaxLength(3).IsRequired();
            builder.Property(o => o.Amount).HasPrecision(18, 4);
            builder.HasIndex(o => new { o.TenantId, o.DueDate, o.Status });
        });
    }
}