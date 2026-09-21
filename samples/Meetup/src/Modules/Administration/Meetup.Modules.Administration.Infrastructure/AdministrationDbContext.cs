using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using Meetup.Modules.Administration.Application;
using Meetup.Modules.Administration.Domain;

namespace Meetup.Modules.Administration.Infrastructure;

/// <summary>The Administration module's own DbContext.</summary>
public sealed class AdministrationDbContext(
    DbContextOptions<AdministrationDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider sp)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp), IUnitOfWork
{
    // Naming is owned by dbsh SQL (schema + snake_case); the prefix is disabled.
    protected override string TablePrefix => string.Empty;

    public DbSet<MeetingGroupProposal> Proposals => Set<MeetingGroupProposal>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);
        mb.HasDefaultSchema("administration");
    }
}
