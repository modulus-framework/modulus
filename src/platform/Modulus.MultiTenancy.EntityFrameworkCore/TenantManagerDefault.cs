namespace Modulus.MultiTenancy.EntityFrameworkCore;

using Modulus.Core.Abstractions;

/// <summary>Tenant administration over the default <see cref="TenantStoreDbContext"/> entities.</summary>
public sealed class TenantManager(
    TenantStoreDbContext db,
    ISecurityAuditLog? audit = null,
    ICurrentUser? actor = null,
    IEnumerable<IAccessChangeObserver>? observers = null)
    : TenantManager<TenantEntity, TenantMembershipEntity, TenantDataStoreEntity>(db, audit, actor, observers);
