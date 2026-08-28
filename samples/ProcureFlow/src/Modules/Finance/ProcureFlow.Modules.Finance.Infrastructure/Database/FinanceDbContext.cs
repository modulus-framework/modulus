using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using ProcureFlow.Modules.Finance.Application;
using ProcureFlow.Modules.Finance.Domain.Entities;
using ProcureFlow.Modules.Finance.Domain.Repositories;
using ProcureFlow.Modules.Finance.Infrastructure.Repositories;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Finance.Infrastructure.Database;

public class FinanceDbContext : ModuleDbContext, IFinanceUnitOfWork
{
    protected override string TablePrefix => "Finance";

    public FinanceDbContext(
        DbContextOptions<FinanceDbContext> options,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        DomainEventDispatcher domainEventDispatcher,
        IServiceProvider serviceProvider)
        : base(options, currentTenant, currentUser, domainEventDispatcher, serviceProvider)
    {
    }

    public Task<int> SaveChangesAsync(Guid userId, CancellationToken ct = default)
        => SaveChangesAsync(ct);

    public DbSet<ApInvoice> ApInvoices => Set<ApInvoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<PaymentProposal> PaymentProposals => Set<PaymentProposal>();
    public DbSet<ApPayment> ApPayments => Set<ApPayment>();
    public DbSet<JournalBatch> JournalBatches => Set<JournalBatch>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<FxRate> FxRates => Set<FxRate>();
    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
    public DbSet<MatchException> MatchExceptions => Set<MatchException>();
    public DbSet<GrIrAccrual> GrIrAccruals => Set<GrIrAccrual>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("finance");

        ConfigureApInvoice(modelBuilder);
        ConfigureInvoiceLine(modelBuilder);
        ConfigurePaymentProposal(modelBuilder);
        ConfigureApPayment(modelBuilder);
        ConfigureJournalBatch(modelBuilder);
        ConfigureJournalLine(modelBuilder);
        ConfigureFxRate(modelBuilder);
        ConfigureCostCenter(modelBuilder);
        ConfigureMatchException(modelBuilder);
        ConfigureGrIrAccrual(modelBuilder);
    }

    private static void ConfigureApInvoice(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApInvoice>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.InvoiceNumber).HasMaxLength(50).IsRequired();
            b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            b.Property(x => x.TotalAmount).HasPrecision(18, 4).IsRequired();
            b.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();

            b.HasIndex(x => x.InvoiceNumber).IsUnique();
            b.HasIndex(x => x.VendorId);
            b.HasIndex(x => x.Status);
            b.HasIndex(x => x.DueDate);
        });
    }

    private static void ConfigureInvoiceLine(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InvoiceLine>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.Uom).HasMaxLength(20);
            b.Property(x => x.UnitPrice).HasPrecision(18, 4);
            b.Property(x => x.LineTotal).HasPrecision(18, 4);

            b.HasOne<ApInvoice>()
                .WithMany(x => x.Lines)
                .HasForeignKey("ApInvoiceId")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigurePaymentProposal(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentProposal>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.ProposalNumber).HasMaxLength(50).IsRequired();
            b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            b.Property(x => x.TotalAmount).HasPrecision(18, 4).IsRequired();

            b.HasIndex(x => x.ProposalNumber).IsUnique();
            b.HasIndex(x => x.Status);

            b.Property<List<Guid>>("_invoiceIds")
                .HasColumnName("InvoiceIds")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<Guid>>(v, (JsonSerializerOptions?)null) ?? new List<Guid>());
        });
    }

    private static void ConfigureApPayment(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApPayment>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Amount).HasPrecision(18, 4).IsRequired();
            b.Property(x => x.ReferenceNumber).HasMaxLength(100).IsRequired();

            b.HasIndex(x => x.InvoiceId);
            b.HasIndex(x => x.Status);
        });
    }

    private static void ConfigureJournalBatch(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JournalBatch>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.JournalNumber).HasMaxLength(50).IsRequired();
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.Currency).HasMaxLength(3).IsRequired();

            b.HasIndex(x => x.JournalNumber).IsUnique();
            b.HasIndex(x => x.PostingDate);
            b.HasIndex(x => x.Status);
        });
    }

    private static void ConfigureJournalLine(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JournalLine>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.AccountCode).HasMaxLength(30).IsRequired();
            b.Property(x => x.AccountName).HasMaxLength(200);
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.Debit).HasPrecision(18, 4);
            b.Property(x => x.Credit).HasPrecision(18, 4);

            b.HasOne<JournalBatch>()
                .WithMany(x => x.Lines)
                .HasForeignKey("JournalBatchId")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureFxRate(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FxRate>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.FromCurrency).HasMaxLength(3).IsRequired();
            b.Property(x => x.ToCurrency).HasMaxLength(3).IsRequired();
            b.Property(x => x.Rate).HasPrecision(18, 6).IsRequired();

            b.HasIndex(x => new { x.EffectiveDate, x.FromCurrency, x.ToCurrency }).IsUnique();
            b.HasIndex(x => x.FromCurrency);
            b.HasIndex(x => x.ToCurrency);
        });
    }

    private static void ConfigureCostCenter(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CostCenter>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(30).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();

            b.HasIndex(x => x.Code).IsUnique();
            b.HasIndex(x => x.ParentId);
        });
    }

    private static void ConfigureMatchException(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MatchException>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.InvoiceQty).HasPrecision(18, 4);
            b.Property(x => x.MatchedQty).HasPrecision(18, 4);
            b.Property(x => x.InvoicePrice).HasPrecision(18, 4);
            b.Property(x => x.MatchedPrice).HasPrecision(18, 4);
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.Resolution).HasMaxLength(1000);
            b.Property(x => x.ResolvedBy).HasMaxLength(100);

            b.HasIndex(x => x.InvoiceId);
            b.HasIndex(x => x.Status);
        });
    }

    private static void ConfigureGrIrAccrual(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GrIrAccrual>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.GrnNumber).HasMaxLength(50);
            b.Property(x => x.Amount).HasPrecision(18, 4);
            b.Property(x => x.Currency).HasMaxLength(3);
            b.Property(x => x.CreatedBy).HasMaxLength(100);
            b.Property(x => x.ClearedBy).HasMaxLength(100);

            b.HasIndex(x => x.GrnId).IsUnique();
            b.HasIndex(x => x.VendorId);
            b.HasIndex(x => x.Status);
        });
    }
}
