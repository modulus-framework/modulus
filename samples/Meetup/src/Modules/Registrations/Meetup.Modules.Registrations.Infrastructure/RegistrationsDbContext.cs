using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using Meetup.Modules.Registrations.Application;
using Meetup.Modules.Registrations.Domain;

namespace Meetup.Modules.Registrations.Infrastructure;

/// <summary>The Registrations module's own DbContext (own tables/connection).</summary>
public sealed class RegistrationsDbContext(
    DbContextOptions<RegistrationsDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider sp)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp), IUnitOfWork
{
    // Naming is owned by dbsh SQL (schema + snake_case); the prefix is disabled.
    protected override string TablePrefix => string.Empty;

    public DbSet<UserRegistration> Registrations => Set<UserRegistration>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);
        mb.HasDefaultSchema("registrations");
    }
}
