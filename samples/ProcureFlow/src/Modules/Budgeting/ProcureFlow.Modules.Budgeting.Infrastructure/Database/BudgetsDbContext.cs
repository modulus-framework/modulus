using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using ProcureFlow.Modules.Budgeting.Application;
using ProcureFlow.Modules.Budgeting.Domain.Constants;
using ProcureFlow.Modules.Budgeting.Domain.Entities;

namespace ProcureFlow.Modules.Budgeting.Infrastructure.Database;

public sealed class BudgetsDbContext(
    DbContextOptions<BudgetsDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider), IUnitOfWork
{
    public DbSet<Budget> Budgets => Set<Budget>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schemas.Budgeting);

        modelBuilder.Entity<Budget>(builder =>
        {
            builder.ToTable("budgets");
            builder.Property(b => b.Category).HasMaxLength(100).IsRequired();
            builder.Property(b => b.Currency).HasMaxLength(3).IsRequired();
            builder.Property(b => b.Amount).HasPrecision(18, 4);
            builder.HasIndex(b => new { b.TenantId, b.FiscalYear, b.CostCenterId, b.Category, b.ProjectId }).IsUnique();

            builder.OwnsMany(b => b.Revisions, revision =>
            {
                revision.ToTable("budget_revisions");
                revision.WithOwner().HasForeignKey("BudgetId");
                revision.HasKey("BudgetId", "Id");
                revision.Property(r => r.Reason).HasMaxLength(500).IsRequired();
                revision.Property(r => r.NewAmount).HasPrecision(18, 4);
            });

            builder.OwnsMany(b => b.Ledger, entry =>
            {
                entry.ToTable("budget_ledger");
                entry.WithOwner().HasForeignKey("BudgetId");
                entry.HasKey("BudgetId", "Id");
                entry.Property(e => e.Currency).HasMaxLength(3).IsRequired();
                entry.Property(e => e.SourceDocumentType).HasMaxLength(50).IsRequired();
                entry.Property(e => e.SourceDocumentNumber).HasMaxLength(100).IsRequired();
                entry.Property(e => e.Amount).HasPrecision(18, 4);
                entry.Property(e => e.BalanceAfter).HasPrecision(18, 4);
                entry.HasIndex(e => e.ReferenceId);
            });
        });
    }
}