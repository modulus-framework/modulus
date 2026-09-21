using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using Meetup.Modules.UserAccess.Application;
using Meetup.Modules.UserAccess.Domain;

namespace Meetup.Modules.UserAccess.Infrastructure;

/// <summary>The UserAccess module's own DbContext.</summary>
public sealed class UserAccessDbContext(
    DbContextOptions<UserAccessDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider sp)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp), IUnitOfWork
{
    // Naming is owned by dbsh SQL (schema + snake_case); the prefix is disabled.
    protected override string TablePrefix => string.Empty;

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);
        mb.HasDefaultSchema("useraccess");
    }
}
