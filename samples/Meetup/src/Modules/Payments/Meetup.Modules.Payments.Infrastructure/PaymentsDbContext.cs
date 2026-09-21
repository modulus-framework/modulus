using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using Meetup.Modules.Payments.Application;
using Meetup.Modules.Payments.Domain;
using Meetup.Modules.Payments.Domain.Entities;

namespace Meetup.Modules.Payments.Infrastructure;

/// <summary>The Payments module's own DbContext.</summary>
public sealed class PaymentsDbContext(
    DbContextOptions<PaymentsDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider sp)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp), IUnitOfWork
{
    // Naming is owned by dbsh SQL (schema + snake_case); the prefix is disabled.
    protected override string TablePrefix => string.Empty;

    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<MeetingFeePayment> MeetingFeePayments => Set<MeetingFeePayment>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);
        mb.HasDefaultSchema("payments");
    }
}
