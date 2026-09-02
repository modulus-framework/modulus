using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using TradeFlow.Modules.Procurement.Application;
using TradeFlow.Modules.Procurement.Domain.Constants;
using TradeFlow.Modules.Procurement.Domain.Entities;

namespace TradeFlow.Modules.Procurement.Infrastructure.Database;

public sealed class ProcurementDbContext(
    DbContextOptions<ProcurementDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider), IUnitOfWork
{
    public DbSet<PurchaseRequisition> PurchaseRequisitions => Set<PurchaseRequisition>();
    public DbSet<Rfq> Rfqs => Set<Rfq>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<Contract> Contracts => Set<Contract>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schemas.Procurement);

        modelBuilder.Entity<PurchaseRequisition>(builder =>
        {
            builder.ToTable("purchase_requisitions");
            builder.Property(p => p.PrNumber).HasMaxLength(50).IsRequired();
            builder.Property(p => p.RequesterName).HasMaxLength(100).IsRequired();
            builder.Property(p => p.RejectionReason).HasMaxLength(500);
            builder.Property(p => p.CancellationReason).HasMaxLength(500);
            builder.HasIndex(p => new { p.TenantId, p.PrNumber }).IsUnique();

            builder.OwnsMany(p => p.Lines, line =>
            {
                line.ToTable("pr_lines");
                line.WithOwner().HasForeignKey("PrId");
                line.HasKey("PrId", "Id");
                line.Property(l => l.FreeText).HasMaxLength(500);
                line.Property(l => l.Category).HasMaxLength(100);
                line.Property(l => l.Uom).HasMaxLength(10).IsRequired();
                line.Property(l => l.Currency).HasMaxLength(3).IsRequired();
                line.Property(l => l.Notes).HasMaxLength(1000);
                line.Property(l => l.EstimatedUnitPrice).HasPrecision(18, 4);
            });
        });

        modelBuilder.Entity<Rfq>(builder =>
        {
            builder.ToTable("rfqs");
            builder.Property(r => r.RfqNumber).HasMaxLength(50).IsRequired();
            builder.Property(r => r.Title).HasMaxLength(200).IsRequired();
            builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
            builder.Property(r => r.CreatedBy).HasMaxLength(100).IsRequired();
            builder.Property(r => r.CancellationReason).HasMaxLength(500);
            builder.HasIndex(r => new { r.TenantId, r.RfqNumber }).IsUnique();

            builder.OwnsMany(r => r.Lines, line =>
            {
                line.ToTable("rfq_lines");
                line.WithOwner().HasForeignKey("RfqId");
                line.HasKey("RfqId", "Id");
                line.Property(l => l.FreeText).HasMaxLength(500);
                line.Property(l => l.HsCode).HasMaxLength(12);
                line.Property(l => l.Uom).HasMaxLength(10).IsRequired();
                line.Property(l => l.PortOfLoading).HasMaxLength(100);
                line.Property(l => l.PortOfDischarge).HasMaxLength(100);
            });

            builder.OwnsMany(r => r.Invitations, invitation =>
            {
                invitation.ToTable("rfq_invitations");
                invitation.WithOwner().HasForeignKey("RfqId");
                invitation.HasKey("RfqId", "VendorId");
            });

            builder.OwnsMany(r => r.Bids, bid =>
            {
                bid.ToTable("rfq_bids");
                bid.WithOwner().HasForeignKey("RfqId");
                bid.HasKey("RfqId", "Id");
                bid.Property(b => b.BidNo).HasMaxLength(50).IsRequired();
                bid.Property(b => b.Currency).HasMaxLength(3).IsRequired();
                bid.Property(b => b.TotalAmountFcy).HasPrecision(18, 4);
                bid.Property(b => b.Notes).HasMaxLength(1000);
            });

            builder.OwnsMany(r => r.Comparison, row =>
            {
                row.ToTable("rfq_comparison");
                row.WithOwner().HasForeignKey("RfqId");
                row.HasKey("RfqId", "BidId");
                row.Property(c => c.Currency).HasMaxLength(3).IsRequired();
                row.Property(c => c.BidAmountFcy).HasPrecision(18, 4);
                row.Property(c => c.FreightBdt).HasPrecision(18, 4);
                row.Property(c => c.DutyBdt).HasPrecision(18, 4);
                row.Property(c => c.HandlingBdt).HasPrecision(18, 4);
                row.Property(c => c.LandedTotalBdt).HasPrecision(18, 4);
            });

            builder.OwnsOne(r => r.Award, award =>
            {
                award.ToTable("rfq_awards");
                award.Property(a => a.Currency).HasMaxLength(3).IsRequired();
                award.Property(a => a.Justification).HasMaxLength(1000);
                award.Property(a => a.AwardedBy).HasMaxLength(100).IsRequired();
                award.Property(a => a.AmountFcy).HasPrecision(18, 4);
                award.Property(a => a.SplitPercent).HasPrecision(9, 6);
                award.Property(a => a.CfoApprovedBy).HasMaxLength(100);
            });
        });

        modelBuilder.Entity<PurchaseOrder>(builder =>
        {
            builder.ToTable("purchase_orders");
            builder.Property(p => p.PoNumber).HasMaxLength(50).IsRequired();
            builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            builder.Property(p => p.Incoterm).HasMaxLength(10).IsRequired();
            builder.Property(p => p.CreatedBy).HasMaxLength(100).IsRequired();
            builder.Property(p => p.PortOfLoading).HasMaxLength(100);
            builder.Property(p => p.PortOfDischarge).HasMaxLength(100);
            builder.Property(p => p.CfoOverrideReason).HasMaxLength(500);
            builder.Property(p => p.CfoOverrideBy).HasMaxLength(100);
            builder.Property(p => p.CloseReason).HasMaxLength(500);
            builder.Property(p => p.ShipmentTolerancePct).HasPrecision(9, 6);
            builder.Property(p => p.ReceivedTolerancePct).HasPrecision(9, 6);
            builder.HasIndex(p => new { p.TenantId, p.PoNumber }).IsUnique();

            builder.OwnsMany(p => p.Lines, line =>
            {
                line.ToTable("po_lines");
                line.WithOwner().HasForeignKey("PoId");
                line.HasKey("PoId", "Id");
                line.Property(l => l.FreeText).HasMaxLength(500);
                line.Property(l => l.HsCode).HasMaxLength(12);
                line.Property(l => l.Uom).HasMaxLength(10).IsRequired();
                line.Property(l => l.Notes).HasMaxLength(1000);
                line.Property(l => l.UnitPrice).HasPrecision(18, 4);
                line.Property(l => l.ReceivedQuantity).HasPrecision(18, 4);
            });

            builder.OwnsMany(p => p.Revisions, revision =>
            {
                revision.ToTable("po_revisions");
                revision.WithOwner().HasForeignKey("PoId");
                revision.HasKey("PoId", "Version");
                revision.Property(r => r.Reason).HasMaxLength(500).IsRequired();
                revision.Property(r => r.By).HasMaxLength(100).IsRequired();
                revision.Property(r => r.TotalDelta).HasPrecision(18, 4);
            });

            builder.OwnsOne(p => p.Feasibility, snapshot =>
            {
                snapshot.ToTable("po_feasibility");
                snapshot.Property(s => s.Verdict).HasMaxLength(20).IsRequired();
                snapshot.Property(s => s.Score).HasPrecision(9, 6);
                snapshot.Property(s => s.Reasons).HasMaxLength(2000);

                snapshot.Property(s => s.NormalizedWeights)
                    .HasConversion(
                        w => JsonSerializer.Serialize(w, (JsonSerializerOptions?)null),
                        json => (IReadOnlyDictionary<string, decimal>)(JsonSerializer.Deserialize<Dictionary<string, decimal>>(json, (JsonSerializerOptions?)null) ?? new Dictionary<string, decimal>()))
                    .HasMaxLength(2000)
                    .IsRequired();

                snapshot.OwnsMany(s => s.Factors, f =>
                {
                    f.ToTable("po_feasibility_factors");
                    f.Property<int>("Id");
                    f.WithOwner().HasForeignKey("FeasibilityPoId");
                    f.HasKey("FeasibilityPoId", "Id");
                    f.Property(x => x.Name).HasMaxLength(100).IsRequired();
                    f.Property(x => x.RawValue).HasPrecision(18, 6);
                    f.Property(x => x.NormalizedScore).HasPrecision(9, 6);
                    f.Property(x => x.WeightedContribution).HasPrecision(9, 6);
                    f.Property(x => x.Description).HasMaxLength(500).IsRequired();
                });

                snapshot.OwnsMany(s => s.RiskFlags, rf =>
                {
                    rf.ToTable("po_feasibility_risk_flags");
                    rf.Property<int>("Id");
                    rf.WithOwner().HasForeignKey("FeasibilityPoId");
                    rf.HasKey("FeasibilityPoId", "Id");
                    rf.Property(x => x.Category).HasMaxLength(50).IsRequired();
                    rf.Property(x => x.Message).HasMaxLength(500).IsRequired();
                    rf.Property(x => x.Severity).HasMaxLength(20).IsRequired();
                });

                snapshot.OwnsMany(s => s.Counterfactuals, cf =>
                {
                    cf.ToTable("po_feasibility_counterfactuals");
                    cf.Property<int>("Id");
                    cf.WithOwner().HasForeignKey("FeasibilityPoId");
                    cf.HasKey("FeasibilityPoId", "Id");
                    cf.Property(c => c.Description).HasMaxLength(500).IsRequired();
                    cf.Property(c => c.EstimatedScoreDelta).HasPrecision(9, 6);
                    cf.Property(c => c.EstimatedCostDelta).HasPrecision(18, 4);
                });
            });
        });

        modelBuilder.Entity<Contract>(builder =>
        {
            builder.ToTable("contracts");
            builder.Property(c => c.ContractNumber).HasMaxLength(50).IsRequired();
            builder.Property(c => c.Currency).HasMaxLength(3).IsRequired();
            builder.Property(c => c.CreatedBy).HasMaxLength(100).IsRequired();
            builder.Property(c => c.UpdatedBy).HasMaxLength(100);
            builder.Property(c => c.Notes).HasMaxLength(2000);
            builder.Property(c => c.TerminationReason).HasMaxLength(1000);
            builder.Property(c => c.CancellationReason).HasMaxLength(1000);
            builder.Property(c => c.CapValue).HasPrecision(18, 4);
            builder.Property(c => c.ConsumedValue).HasPrecision(18, 4);
            builder.HasIndex(c => new { c.TenantId, c.ContractNumber }).IsUnique();

            builder.OwnsMany(c => c.Lines, line =>
            {
                line.ToTable("contract_lines");
                line.WithOwner().HasForeignKey("ContractId");
                line.HasKey("ContractId", "Id");
                line.Property(l => l.FreeText).HasMaxLength(500);
                line.Property(l => l.UnitPrice).HasPrecision(18, 4);
                line.Property(l => l.MinQuantity).HasPrecision(18, 4);
                line.Property(l => l.EscalationJson).HasMaxLength(2000);
                line.Property(l => l.Notes).HasMaxLength(1000);
            });

            builder.OwnsMany(c => c.Documents, doc =>
            {
                doc.ToTable("contract_documents");
                doc.WithOwner().HasForeignKey("ContractId");
                doc.HasKey("ContractId", "Id");
                doc.Property(d => d.DocumentType).HasMaxLength(50).IsRequired();
                doc.Property(d => d.S3Key).HasMaxLength(500).IsRequired();
                doc.Property(d => d.UploadedBy).HasMaxLength(100).IsRequired();
            });

            builder.OwnsMany(c => c.Milestones, milestone =>
            {
                milestone.ToTable("contract_milestones");
                milestone.WithOwner().HasForeignKey("ContractId");
                milestone.HasKey("ContractId", "Id");
                milestone.Property(m => m.Title).HasMaxLength(200).IsRequired();
                milestone.Property(m => m.Deliverables).HasMaxLength(2000);
                milestone.Property(m => m.SlaJson).HasMaxLength(2000);
            });

            builder.OwnsMany(c => c.Revisions, revision =>
            {
                revision.ToTable("contract_revisions");
                revision.WithOwner().HasForeignKey("ContractId");
                revision.HasKey("ContractId", "Version");
                revision.Property(r => r.Reason).HasMaxLength(500).IsRequired();
                revision.Property(r => r.By).HasMaxLength(100).IsRequired();
                revision.Property(r => r.PreviousCapValue).HasPrecision(18, 4);
                revision.Property(r => r.NewCapValue).HasPrecision(18, 4);
            });
        });
    }
}