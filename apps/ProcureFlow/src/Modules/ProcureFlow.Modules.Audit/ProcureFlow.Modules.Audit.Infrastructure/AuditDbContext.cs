using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;
using Modulus.Events;
using Modulus.EntityFrameworkCore;
using ProcureFlow.Modules.Audit.Application;

namespace ProcureFlow.Modules.Audit.Infrastructure;

/// <summary>
/// The Audit module's own DbContext. Each module owns its context
/// (with its own tables/connection) so modules are independently deployable.
/// Implements the module's <see cref="IUnitOfWork"/> so handlers can save
/// without depending on EF Core.
/// </summary>
public sealed class AuditDbContext(
    DbContextOptions<AuditDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider sp)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp), IUnitOfWork
{
    protected override string TablePrefix => "audit_";
}
