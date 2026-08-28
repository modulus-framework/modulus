using ProcureFlow.Modules.Vendors.Application.Abstractions;
using ProcureFlow.Modules.Vendors.Domain.Constants;
using ProcureFlow.Modules.Vendors.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;

namespace ProcureFlow.Modules.Vendors.Infrastructure.Database;

public sealed class VendorsDbContext(
    DbContextOptions<VendorsDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider), IUnitOfWork
{
    public DbSet<Vendor> Vendors => Set<Vendor>();

    protected override string TablePrefix => string.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schemas.Vendors);

        modelBuilder.Entity<Vendor>(builder =>
        {
            builder.ToTable("vendors");
            builder.HasKey(v => v.Id);

            builder.OwnsMany(v => v.Qualifications, q =>
            {
                q.WithOwner().HasForeignKey("VendorId");
                q.HasKey("VendorId", nameof(VendorQualification.Id));
                q.ToTable("vendor_qualifications");
            });

            builder.OwnsMany(v => v.BankAccounts, b =>
            {
                b.WithOwner().HasForeignKey("VendorId");
                b.HasKey("VendorId", nameof(VendorBankAccount.Id));
                b.ToTable("vendor_bank_accounts");
            });

            builder.OwnsMany(v => v.Scorecards, s =>
            {
                s.WithOwner().HasForeignKey("VendorId");
                s.HasKey("VendorId", nameof(VendorScorecard.Id));
                s.ToTable("vendor_scorecards");
            });
        });
    }
}