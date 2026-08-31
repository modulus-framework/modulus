using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using TradeFlow.Modules.Customs.Application;
using TradeFlow.Modules.Customs.Domain.Constants;
using TradeFlow.Modules.Customs.Domain.Entities;

namespace TradeFlow.Modules.Customs.Infrastructure.Database;

public sealed class CustomsDbContext(
    DbContextOptions<CustomsDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider), IUnitOfWork
{
    public DbSet<HsCode> HsCodes => Set<HsCode>();
    public DbSet<DutyRate> DutyRates => Set<DutyRate>();
    public DbSet<SroBenefit> SroBenefits => Set<SroBenefit>();
    public DbSet<BillOfEntry> BillsOfEntry => Set<BillOfEntry>();
    public DbSet<AitAtLedgerEntry> AitAtLedgerEntries => Set<AitAtLedgerEntry>();
    public DbSet<DemurrageAccrual> DemurrageAccruals => Set<DemurrageAccrual>();
    public DbSet<ItemHsMapping> ItemHsMappings => Set<ItemHsMapping>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schemas.Customs);

        modelBuilder.Entity<HsCode>(builder =>
        {
            builder.ToTable("hs_codes");
            builder.Property(h => h.Code).HasMaxLength(12).IsRequired();
            builder.Property(h => h.Description).HasMaxLength(500).IsRequired();
            builder.HasIndex(h => new { h.Code, h.EffectiveFrom }).IsUnique();
        });

        modelBuilder.Entity<DutyRate>(builder =>
        {
            builder.ToTable("duty_rates");
            builder.Property(d => d.HsCode).HasMaxLength(12).IsRequired();
            builder.Property(d => d.Rate).HasPrecision(9, 6);
            builder.Property(d => d.SpecificRate).HasPrecision(18, 4);
            builder.Property(d => d.Uom).HasMaxLength(10);
            builder.Property(d => d.RefDoc).HasMaxLength(100);
            builder.Property(d => d.Maker).HasMaxLength(100).IsRequired();
            builder.Property(d => d.Checker).HasMaxLength(100);
            builder.HasIndex(d => new { d.HsCode, d.Component, d.EffectiveFrom });
        });

        modelBuilder.Entity<SroBenefit>(builder =>
        {
            builder.ToTable("sro_benefits");
            builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
            builder.Property(s => s.HsCodePrefix).HasMaxLength(12).IsRequired();
            builder.Property(s => s.OverrideRate).HasPrecision(9, 6);
            builder.Property(s => s.CapPercent).HasPrecision(9, 6);
            builder.Property(s => s.Conditions).HasMaxLength(1000);
        });

        modelBuilder.Entity<BillOfEntry>(builder =>
        {
            builder.ToTable("bills_of_entry");
            builder.Property(b => b.BoeNo).HasMaxLength(50).IsRequired();
            builder.Property(b => b.OfficeCode).HasMaxLength(50).IsRequired();
            builder.Property(b => b.DeclarantAin).HasMaxLength(50).IsRequired();
            builder.Property(b => b.TolerancePct).HasPrecision(9, 6);
            builder.HasIndex(b => new { b.TenantId, b.BoeNo }).IsUnique();

            builder.OwnsMany(b => b.Lines, line =>
            {
                line.ToTable("boe_lines");
                line.WithOwner().HasForeignKey("BoeId");
                line.HasKey("BoeId", "Id");
                line.Property(l => l.HsCode).HasMaxLength(12).IsRequired();
                line.Property(l => l.Description).HasMaxLength(500).IsRequired();
                line.Property(l => l.Uom).HasMaxLength(10).IsRequired();
                line.Property(l => l.DeclaredAvFcy).HasPrecision(18, 4);
                line.Property(l => l.CustomsExchangeRate).HasPrecision(18, 6);
                line.Property(l => l.LandingChargePct).HasPrecision(9, 6);
                line.Property(l => l.TariffValueBdt).HasPrecision(18, 4);
                line.Property(l => l.ComputedTtiBdt).HasPrecision(18, 4);
                line.Property(l => l.AssessedTtiBdt).HasPrecision(18, 4);
                line.HasIndex(l => l.HsCode);

                line.OwnsMany(l => l.AssessedDutyLines, duty =>
                {
                    duty.ToTable("boe_line_assessed_duties");
                    duty.WithOwner().HasForeignKey("BoeId", "BoeLineId");
                    duty.Property<Guid>("Id");
                    duty.HasKey("BoeId", "BoeLineId", "Id");
                    duty.Property(d => d.Component).HasMaxLength(10).IsRequired();
                    duty.Property(d => d.Amount).HasPrecision(18, 4);
                });

                line.OwnsMany(l => l.RateLineage, lineage =>
                {
                    lineage.ToTable("boe_line_rate_lineage");
                    lineage.WithOwner().HasForeignKey("BoeId", "BoeLineId");
                    lineage.Property<Guid>("Id");
                    lineage.HasKey("BoeId", "BoeLineId", "Id");
                    lineage.Property(r => r.Component).HasMaxLength(10).IsRequired();
                });
            });

            builder.OwnsMany(b => b.Challans, challan =>
            {
                challan.ToTable("boe_challans");
                challan.WithOwner().HasForeignKey("BoeId");
                challan.HasKey("BoeId", "Id");
                challan.Property(c => c.ChallanNo).HasMaxLength(50).IsRequired();
                challan.Property(c => c.Amount).HasPrecision(18, 4);
                challan.Property(c => c.EvidenceRef).HasMaxLength(200);
            });

            builder.OwnsMany(b => b.Milestones, milestone =>
            {
                milestone.ToTable("boe_milestones");
                milestone.WithOwner().HasForeignKey("BoeId");
                milestone.HasKey("BoeId", "Id");
                milestone.Property(m => m.Stage).HasMaxLength(50).IsRequired();
            });

            builder.OwnsMany(b => b.Disputes, dispute =>
            {
                dispute.ToTable("boe_disputes");
                dispute.WithOwner().HasForeignKey("BoeId");
                dispute.HasKey("BoeId", "Id");
                dispute.Property(d => d.GuaranteeRef).HasMaxLength(100);
                dispute.Property(d => d.VarianceAmount).HasPrecision(18, 4);
                dispute.Property(d => d.TolerancePct).HasPrecision(9, 6);
            });
        });

        modelBuilder.Entity<AitAtLedgerEntry>(builder =>
        {
            builder.ToTable("ait_at_ledger");
            builder.Property(a => a.Amount).HasPrecision(18, 4);
            builder.HasIndex(a => new { a.CompanyId, a.FiscalYear, a.Component });
        });

        modelBuilder.Entity<DemurrageAccrual>(builder =>
        {
            builder.ToTable("demurrage_accruals");
            builder.Property(d => d.ContainerRef).HasMaxLength(30).IsRequired();
            builder.Property(d => d.PortCode).HasMaxLength(20).IsRequired();
            builder.Property(d => d.DailyRateBdt).HasPrecision(18, 4);
            builder.Property(d => d.AccruedAmountBdt).HasPrecision(18, 4);
            builder.HasIndex(d => new { d.TenantId, d.FileId });
        });

        modelBuilder.Entity<ItemHsMapping>(builder =>
        {
            builder.ToTable("item_hs_mappings");
            builder.Property(m => m.HsCode).HasMaxLength(12).IsRequired();
            builder.Property(m => m.Confidence).HasPrecision(5, 4);
            builder.Property(m => m.Notes).HasMaxLength(1000);
            builder.Property(m => m.RejectionReason).HasMaxLength(500);
            builder.HasIndex(m => new { m.TenantId, m.ItemId }).IsUnique();
            builder.HasIndex(m => new { m.TenantId, m.HsCode });
        });
    }
}