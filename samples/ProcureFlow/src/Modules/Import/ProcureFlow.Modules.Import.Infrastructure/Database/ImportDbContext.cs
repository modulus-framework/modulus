using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using ProcureFlow.Modules.Import.Application;
using ProcureFlow.Modules.Import.Domain.Constants;
using ProcureFlow.Modules.Import.Domain.Entities;

namespace ProcureFlow.Modules.Import.Infrastructure.Database;

public sealed class ImportDbContext(
    DbContextOptions<ImportDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider), IUnitOfWork
{
    public DbSet<ImportFile> ImportFiles => Set<ImportFile>();
    public DbSet<ProformaInvoice> ProformaInvoices => Set<ProformaInvoice>();
    public DbSet<CommercialInvoice> CommercialInvoices => Set<CommercialInvoice>();
    public DbSet<PackingList> PackingLists => Set<PackingList>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<TransportDocument> TransportDocuments => Set<TransportDocument>();
    public DbSet<FreightCost> FreightCosts => Set<FreightCost>();
    public DbSet<InsurancePolicy> InsurancePolicies => Set<InsurancePolicy>();
    public DbSet<ImportPermit> Permits => Set<ImportPermit>();
    public DbSet<BillOfEntry> BillsOfEntry => Set<BillOfEntry>();
    public DbSet<AssessmentVariance> AssessmentVariances => Set<AssessmentVariance>();
    public DbSet<PortCharge> PortCharges => Set<PortCharge>();
    public DbSet<CnfAgent> CnfAgents => Set<CnfAgent>();
    public DbSet<ImportPlan> ImportPlans => Set<ImportPlan>();
    public DbSet<CertificateOfOrigin> CertificatesOfOrigin => Set<CertificateOfOrigin>();
    public DbSet<CooIssuerRegistry> CooIssuerRegistries => Set<CooIssuerRegistry>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schemas.Import);

        modelBuilder.Entity<ImportFile>(builder =>
        {
            builder.ToTable("import_files");
            builder.Property(f => f.FileNumber).HasMaxLength(50).IsRequired();
            builder.Property(f => f.Incoterm).HasMaxLength(10).IsRequired();
            builder.Property(f => f.Currency).HasMaxLength(3).IsRequired();
            builder.Property(f => f.PortOfLoading).HasMaxLength(100).IsRequired();
            builder.Property(f => f.PortOfDischarge).HasMaxLength(100).IsRequired();
            builder.Property(f => f.CreatedBy).HasMaxLength(100).IsRequired();
            builder.Property(f => f.HoldReason).HasMaxLength(500);
            builder.Property(f => f.DisputeReason).HasMaxLength(500);
            builder.Property(f => f.CancellationReason).HasMaxLength(500);
            builder.Property(f => f.EstimatedGoodsValue).HasPrecision(18, 4);
            builder.Property(f => f.ClearingBalance).HasPrecision(18, 4);
            builder.HasIndex(f => new { f.TenantId, f.FileNumber }).IsUnique();
            builder.HasIndex(f => new { f.TenantId, f.CompanyId, f.FiscalYear, f.Sequence }).IsUnique();

            builder.OwnsMany(f => f.Milestones, milestone =>
            {
                milestone.ToTable("import_milestones");
                milestone.WithOwner().HasForeignKey("FileId");
                milestone.HasKey("FileId", "Id");
                milestone.Property(m => m.Name).HasMaxLength(50).IsRequired();
                milestone.Property(m => m.Note).HasMaxLength(500);
            });

            builder.OwnsMany(f => f.Containers, container =>
            {
                container.ToTable("import_containers");
                container.WithOwner().HasForeignKey("FileId");
                container.HasKey("FileId", "Id");
                container.Property(c => c.ContainerNo).HasMaxLength(11).IsRequired();
                container.Property(c => c.SizeType).HasMaxLength(10).IsRequired();
                container.Property(c => c.IsoCode).HasMaxLength(20);
                container.Property(c => c.SealNo).HasMaxLength(50);

                container.OwnsMany(c => c.Events, containerEvent =>
                {
                    containerEvent.ToTable("import_container_events");
                    containerEvent.WithOwner().HasForeignKey("FileId", "ContainerId");
                    containerEvent.Property<Guid>("Id");
                    containerEvent.HasKey("FileId", "ContainerId", "Id");
                    containerEvent.Property(e => e.Type).HasMaxLength(20).IsRequired();
                });
            });

            builder.OwnsMany(f => f.CostEntries, entry =>
            {
                entry.ToTable("import_cost_entries");
                entry.WithOwner().HasForeignKey("FileId");
                entry.HasKey("FileId", "Id");
                entry.Property(e => e.Element).HasMaxLength(50).IsRequired();
                entry.Property(e => e.Currency).HasMaxLength(3).IsRequired();
                entry.Property(e => e.SourceDocType).HasMaxLength(20).IsRequired();
                entry.Property(e => e.SourceDocNumber).HasMaxLength(50).IsRequired();
                entry.Property(e => e.AmountFcy).HasPrecision(18, 4);
                entry.Property(e => e.AmountBdt).HasPrecision(18, 4);
            });

            builder.OwnsMany(f => f.Documents, document =>
            {
                document.ToTable("import_file_documents");
                document.WithOwner().HasForeignKey("FileId");
                document.HasKey("FileId", "Id");
                document.Property(d => d.Type).HasMaxLength(20).IsRequired();
                document.Property(d => d.Name).HasMaxLength(200).IsRequired();
            });
        });

        modelBuilder.Entity<ProformaInvoice>(builder =>
        {
            builder.ToTable("proforma_invoices");
            builder.Property(p => p.PiNumber).HasMaxLength(50).IsRequired();
            builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            builder.Property(p => p.BeneficiaryName).HasMaxLength(200).IsRequired();
            builder.Property(p => p.BeneficiaryBank).HasMaxLength(200).IsRequired();
            builder.Property(p => p.BeneficiaryAccount).HasMaxLength(100).IsRequired();
            builder.Property(p => p.ReceivedBy).HasMaxLength(100).IsRequired();
            builder.HasIndex(p => new { p.TenantId, p.PiNumber }).IsUnique();

            builder.OwnsMany(p => p.Lines, line =>
            {
                line.ToTable("pi_lines");
                line.WithOwner().HasForeignKey("PiId");
                line.HasKey("PiId", "Id");
                line.Property(l => l.Description).HasMaxLength(500).IsRequired();
                line.Property(l => l.Uom).HasMaxLength(10).IsRequired();
                line.Property(l => l.VarianceNote).HasMaxLength(1000);
                line.Property(l => l.UnitPrice).HasPrecision(18, 4);
            });
        });

        modelBuilder.Entity<CommercialInvoice>(builder =>
        {
            builder.ToTable("commercial_invoices");
            builder.Property(c => c.CiNumber).HasMaxLength(50).IsRequired();
            builder.Property(c => c.Currency).HasMaxLength(3).IsRequired();
            builder.Property(c => c.ReceivedBy).HasMaxLength(100).IsRequired();
            builder.Property(c => c.TotalFcy).HasPrecision(18, 4);
            builder.HasIndex(c => new { c.TenantId, c.CiNumber }).IsUnique();

            builder.OwnsMany(c => c.Lines, line =>
            {
                line.ToTable("ci_lines");
                line.WithOwner().HasForeignKey("CiId");
                line.HasKey("CiId", "Id");
                line.Property(l => l.Description).HasMaxLength(500).IsRequired();
                line.Property(l => l.Uom).HasMaxLength(10).IsRequired();
                line.Property(l => l.UnitPrice).HasPrecision(18, 4);
                line.Property(l => l.BoeValue).HasPrecision(18, 4);
            });
        });

        modelBuilder.Entity<PackingList>(builder =>
        {
            builder.ToTable("packing_lists");
            builder.Property(p => p.PlNumber).HasMaxLength(50).IsRequired();
            builder.Property(p => p.NetWeightKg).HasPrecision(18, 4);
            builder.Property(p => p.GrossWeightKg).HasPrecision(18, 4);
            builder.Property(p => p.VolumeCbm).HasPrecision(18, 4);

            builder.OwnsMany(p => p.Lines, line =>
            {
                line.ToTable("pl_lines");
                line.WithOwner().HasForeignKey("PlId");
                line.HasKey("PlId", "Id");
                line.Property(l => l.Uom).HasMaxLength(10).IsRequired();
                line.Property(l => l.NetWeightKg).HasPrecision(18, 4);
                line.Property(l => l.GrossWeightKg).HasPrecision(18, 4);
                line.Property(l => l.VolumeCbm).HasPrecision(18, 4);
            });
        });

        modelBuilder.Entity<Shipment>(builder =>
        {
            builder.ToTable("shipments");
            builder.Property(s => s.ShipmentNo).HasMaxLength(50).IsRequired();
            builder.Property(s => s.VesselVoyage).HasMaxLength(100).IsRequired();
            builder.Property(s => s.CreatedBy).HasMaxLength(100).IsRequired();
            builder.HasIndex(s => new { s.TenantId, s.ShipmentNo }).IsUnique();

            builder.OwnsMany(s => s.Milestones, milestone =>
            {
                milestone.ToTable("shipment_milestones");
                milestone.WithOwner().HasForeignKey("ShipmentId");
                milestone.HasKey("ShipmentId", "Id");
                milestone.Property(m => m.Name).HasMaxLength(50).IsRequired();
                milestone.Property(m => m.Note).HasMaxLength(500);
            });
        });

        modelBuilder.Entity<InsurancePolicy>(builder =>
        {
            builder.ToTable("insurance_policies");
            builder.Property(i => i.PolicyNo).HasMaxLength(50).IsRequired();
            builder.Property(i => i.Insurer).HasMaxLength(200).IsRequired();
            builder.Property(i => i.CoverNoteRef).HasMaxLength(100).IsRequired();
            builder.Property(i => i.Currency).HasMaxLength(3).IsRequired();
            builder.Property(i => i.InsuredValueFcy).HasPrecision(18, 4);
            builder.Property(i => i.PremiumFcy).HasPrecision(18, 4);
            builder.HasIndex(i => new { i.TenantId, i.PolicyNo }).IsUnique();
        });

        modelBuilder.Entity<ImportPermit>(builder =>
        {
            builder.ToTable("import_permits");
            builder.Property(p => p.PermitNo).HasMaxLength(50).IsRequired();
            builder.Property(p => p.Category).HasMaxLength(100).IsRequired();
            builder.Property(p => p.IssuedBy).HasMaxLength(100).IsRequired();
            builder.Property(p => p.CeilingQty).HasPrecision(18, 4);
            builder.Property(p => p.CeilingValue).HasPrecision(18, 4);
            builder.HasIndex(p => new { p.TenantId, p.PermitNo }).IsUnique();

            builder.OwnsMany(p => p.Utilizations, utilization =>
            {
                utilization.ToTable("permit_utilizations");
                utilization.WithOwner().HasForeignKey("PermitId");
                utilization.HasKey("PermitId", "Id");
                utilization.Property(u => u.Quantity).HasPrecision(18, 4);
                utilization.Property(u => u.Value).HasPrecision(18, 4);
            });
        });

        modelBuilder.Entity<CnfAgent>(builder =>
        {
            builder.ToTable("cnf_agents");
            builder.Property(a => a.Name).HasMaxLength(200).IsRequired();
            builder.Property(a => a.AinNumber).HasMaxLength(50).IsRequired();
            builder.Property(a => a.Contacts).HasMaxLength(500);
            builder.Property(a => a.RateCardPerBoe).HasPrecision(18, 4);
            builder.Property(a => a.RateCardPerContainer).HasPrecision(18, 4);
            builder.Property(a => a.RateCardPctOfValue).HasPrecision(9, 6);
            builder.Property(a => a.RateCardDocumentationCharges).HasPrecision(18, 4);
            builder.HasIndex(a => new { a.TenantId, a.AinNumber }).IsUnique();

            builder.OwnsMany(a => a.ChargeBills, bill =>
            {
                bill.ToTable("cnf_charge_bills");
                bill.WithOwner().HasForeignKey("AgentId");
                bill.HasKey("AgentId", "Id");
                bill.Property(b => b.BillNo).HasMaxLength(50).IsRequired();
                bill.Property(b => b.AmountBdt).HasPrecision(18, 4);
            });
        });

        modelBuilder.Entity<TransportDocument>(builder =>
        {
            builder.ToTable("transport_documents");
            builder.Property(t => t.DocumentNumber).HasMaxLength(50).IsRequired();
            builder.Property(t => t.FreightTerms).HasMaxLength(20).IsRequired();
            builder.Property(t => t.Consignee).HasMaxLength(500).IsRequired();
            builder.Property(t => t.NotifyParty).HasMaxLength(500).IsRequired();
            builder.HasIndex(t => new { t.TenantId, t.DocumentNumber }).IsUnique();
            builder.HasIndex(t => t.ShipmentId);
            builder.HasIndex(t => t.FileId);
        });

        modelBuilder.Entity<FreightCost>(builder =>
        {
            builder.ToTable("freight_costs");
            builder.Property(f => f.Description).HasMaxLength(500).IsRequired();
            builder.Property(f => f.Amount).HasPrecision(18, 4);
            builder.Property(f => f.Currency).HasMaxLength(3).IsRequired();
            builder.Property(f => f.SurchargeType).HasMaxLength(50);
            builder.Property(f => f.InvoiceNo).HasMaxLength(50);
            builder.HasIndex(f => f.ShipmentId);
            builder.HasIndex(f => f.FileId);
        });

        modelBuilder.Entity<BillOfEntry>(builder =>
        {
            builder.ToTable("bills_of_entry");
            builder.Property(b => b.BoeNumber).HasMaxLength(50).IsRequired();
            builder.Property(b => b.CustomsOffice).HasMaxLength(100).IsRequired();
            builder.Property(b => b.DeclarantAin).HasMaxLength(50).IsRequired();
            builder.Property(b => b.DisputeReason).HasMaxLength(500);
            builder.Property(b => b.TotalAssessableValue).HasPrecision(18, 4);
            builder.Property(b => b.TotalDuty).HasPrecision(18, 4);
            builder.HasIndex(b => new { b.TenantId, b.BoeNumber }).IsUnique();
            builder.HasIndex(b => b.FileId);

            builder.OwnsMany(b => b.Lines, line =>
            {
                line.ToTable("boe_lines");
                line.WithOwner().HasForeignKey("BoeId");
                line.HasKey("BoeId", "Id");
                line.Property(l => l.HsCode).HasMaxLength(20).IsRequired();
                line.Property(l => l.AssessableValue).HasPrecision(18, 4);
                line.Property(l => l.Uom).HasMaxLength(10).IsRequired();
            });

            builder.OwnsMany(b => b.DutyLines, duty =>
            {
                duty.ToTable("boe_duty_lines");
                duty.WithOwner().HasForeignKey("BoeId");
                duty.HasKey("BoeId", "Id");
                duty.Property(d => d.Component).HasMaxLength(20).IsRequired();
                duty.Property(d => d.Rate).HasPrecision(9, 6);
                duty.Property(d => d.Amount).HasPrecision(18, 4);
                duty.Property(d => d.SroRef).HasMaxLength(50);
            });

            builder.OwnsMany(b => b.Milestones, milestone =>
            {
                milestone.ToTable("boe_milestones");
                milestone.WithOwner().HasForeignKey("BoeId");
                milestone.HasKey("BoeId", "Id");
                milestone.Property(m => m.Name).HasMaxLength(100).IsRequired();
            });
        });

        modelBuilder.Entity<AssessmentVariance>(builder =>
        {
            builder.ToTable("assessment_variances");
            builder.Property(v => v.Component).HasMaxLength(50).IsRequired();
            builder.Property(v => v.SystemAmount).HasPrecision(18, 4);
            builder.Property(v => v.AssessedAmount).HasPrecision(18, 4);
            builder.Property(v => v.VarianceAmount).HasPrecision(18, 4);
            builder.Property(v => v.Reason).HasMaxLength(500).IsRequired();
            builder.Property(v => v.Resolution).HasMaxLength(500);
            builder.HasIndex(v => v.BoeId);
        });

        modelBuilder.Entity<PortCharge>(builder =>
        {
            builder.ToTable("port_charges");
            builder.Property(c => c.Amount).HasPrecision(18, 4);
            builder.Property(c => c.Currency).HasMaxLength(3).IsRequired();
            builder.Property(c => c.ReceiptRef).HasMaxLength(50);
            builder.Property(c => c.Description).HasMaxLength(500);
            builder.HasIndex(c => c.FileId);
        });

        modelBuilder.Entity<ImportPlan>(builder =>
        {
            builder.ToTable("import_plans");
            builder.Property(p => p.PlanNumber).HasMaxLength(50).IsRequired();
            builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            builder.Property(p => p.TotalEstFob).HasPrecision(18, 4);
            builder.Property(p => p.TotalEstLanded).HasPrecision(18, 4);
            builder.HasIndex(p => new { p.TenantId, p.PlanNumber }).IsUnique();
            builder.HasIndex(p => new { p.TenantId, p.FiscalYear });

            builder.OwnsMany(p => p.Lines, line =>
            {
                line.ToTable("import_plan_lines");
                line.WithOwner().HasForeignKey("PlanId");
                line.HasKey("PlanId", "Id");
                line.Property(l => l.Description).HasMaxLength(500).IsRequired();
                line.Property(l => l.SourceCountry).HasMaxLength(100);
                line.Property(l => l.EstQty).HasPrecision(18, 4);
                line.Property(l => l.EstFob).HasPrecision(18, 4);
                line.Property(l => l.EstLanded).HasPrecision(18, 4);
                line.Property(l => l.ActualQty).HasPrecision(18, 4);
                line.Property(l => l.ActualFob).HasPrecision(18, 4);
                line.Property(l => l.ActualLanded).HasPrecision(18, 4);
            });
        });

        modelBuilder.Entity<CertificateOfOrigin>(builder =>
        {
            builder.ToTable("certificates_of_origin");
            builder.Property(c => c.OriginCountry).HasMaxLength(100).IsRequired();
            builder.Property(c => c.DocumentNo).HasMaxLength(50).IsRequired();
            builder.Property(c => c.IssuerName).HasMaxLength(200);
            builder.Property(c => c.MismatchReason).HasMaxLength(500);
            builder.HasIndex(c => new { c.TenantId, c.FileId }).IsUnique();
            builder.HasIndex(c => new { c.TenantId, c.DocumentNo }).IsUnique();
        });

        modelBuilder.Entity<CooIssuerRegistry>(builder =>
        {
            builder.ToTable("coo_issuer_registries");
            builder.Property(r => r.Country).HasMaxLength(100).IsRequired();
            builder.Property(r => r.IssuerName).HasMaxLength(200).IsRequired();
            builder.Property(r => r.LicenseNo).HasMaxLength(100);
            builder.HasIndex(r => new { r.TenantId, r.Country, r.IssuerName }).IsUnique();
        });
    }
}