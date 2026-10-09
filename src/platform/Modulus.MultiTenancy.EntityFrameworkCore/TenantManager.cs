using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;

namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// Provisioning surface for the EF-backed tenant store: create tenants and toggle
/// their active state. Registered as a scoped service by
/// <c>AddEfCoreTenantStore</c>. Reads go through <see cref="ITenantStore"/> /
/// <see cref="EfTenantStore"/>; this is the write side, used by admin endpoints or
/// seed code. Membership and activation changes are recorded in the tenant's security audit chain and reported
/// to every <see cref="IAccessChangeObserver"/> (systems that cache access, such as the AI connector).
/// </summary>
public sealed class TenantManager(
    TenantStoreDbContext db,
    ISecurityAuditLog? audit = null,
    ICurrentUser? actor = null,
    IEnumerable<IAccessChangeObserver>? observers = null)
{
    /// <summary>
    /// Creates a new active tenant. Throws
    /// <see cref="InvalidOperationException"/> if <paramref name="slug"/> is already
    /// taken (also enforced by a unique index at the database level).
    /// </summary>
    public Task<TenantInfo> CreateAsync(
        string slug,
        string? displayName = null,
        Guid? id = null,
        CancellationToken ct = default)
        => CreateAsync(slug, displayName, id, groupId: null, ct);

    /// <summary>
    /// Creates a new active tenant in the group of companies <paramref name="groupId"/>.
    /// </summary>
    public async Task<TenantInfo> CreateAsync(
        string slug,
        string? displayName,
        Guid? id,
        Guid? groupId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        if (await db.Tenants.AnyAsync(t => t.Slug == slug, ct))
            throw new InvalidOperationException(
                $"A tenant with slug '{slug}' already exists.");

        var entity = new TenantEntity
        {
            Id = id ?? Guid.NewGuid(),
            Slug = slug,
            DisplayName = displayName,
            GroupId = groupId,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        db.Tenants.Add(entity);
        await db.SaveChangesAsync(ct);
        return new TenantInfo(entity.Id, entity.Slug, entity.DisplayName, GroupId: entity.GroupId);
    }

    /// <summary>
    /// Sets a tenant's active flag. A deactivated tenant stops resolving
    /// immediately (see <see cref="EfTenantStore"/>). Returns
    /// <see langword="false"/> if no tenant has the given id.
    /// </summary>
    public async Task<bool> SetActiveAsync(
        Guid id,
        bool isActive,
        CancellationToken ct = default)
    {
        var entity = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (entity is null) return false;

        entity.IsActive = isActive;
        await db.SaveChangesAsync(ct);
        Audit(isActive ? "tenant.activated" : "tenant.deactivated", id, $"tenant:{id}");
        await NotifyAsync(AccessChangeKinds.Tenant, isActive ? "tenant.activated" : "tenant.deactivated", id, null, ct);
        return true;
    }

    /// <summary>
    /// Moves a company through its life: <see cref="TenantStatus.Trial"/> (until <paramref name="trialEndsAt"/>),
    /// <see cref="TenantStatus.Active"/>, <see cref="TenantStatus.Suspended"/> (read-only) or <see cref="TenantStatus.Closed"/>
    /// (no longer resolves). Takes effect on the next request. Returns <see langword="false"/> if no tenant has the id.
    /// </summary>
    public async Task<bool> SetStatusAsync(Guid id, TenantStatus status, DateTimeOffset? trialEndsAt = null, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (status is TenantStatus.Trial && trialEndsAt is null)
            throw new ArgumentException("A trial needs an end date.", nameof(trialEndsAt));

        var entity = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (entity is null) return false;

        entity.Status = status;
        entity.IsActive = status is not TenantStatus.Closed;
        entity.TrialEndsAt = status is TenantStatus.Trial ? trialEndsAt : null;
        await db.SaveChangesAsync(ct);
        Audit("tenant.status-changed", id, $"tenant:{id} -> {status}");
        await NotifyAsync(AccessChangeKinds.Tenant, $"tenant.{status.ToString().ToLowerInvariant()}", id, null, ct);
        return true;
    }

    /// <summary>
    /// Limits how many active members the company may have (null removes the limit). Lowering it below the current
    /// count removes nobody; it only refuses new members. Returns <see langword="false"/> if no tenant has the id.
    /// </summary>
    public async Task<bool> SetMaxUsersAsync(Guid id, int? maxUsers, CancellationToken ct = default)
    {
        if (maxUsers is < 0)
            throw new ArgumentOutOfRangeException(nameof(maxUsers));

        var entity = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (entity is null) return false;

        entity.MaxUsers = maxUsers;
        await db.SaveChangesAsync(ct);
        Audit("tenant.limit-changed", id, $"tenant:{id} maxUsers={maxUsers?.ToString() ?? "none"}");
        return true;
    }

    /// <summary>
    /// Grants <paramref name="userId"/> membership in <paramref name="tenantId"/> (one login across
    /// companies), or re-activates a revoked one. Returns <see langword="false"/> when the tenant
    /// does not exist.
    /// </summary>
    public async Task<bool> AddMemberAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        var maxUsers = await db.Tenants.Where(t => t.Id == tenantId).Select(t => new { t.MaxUsers }).FirstOrDefaultAsync(ct);
        if (maxUsers is null)
            return false;

        var existing = await db.TenantMemberships
            .FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId, ct);
        if (existing is not { IsActive: true } && maxUsers.MaxUsers is { } limit
            && await db.TenantMemberships.CountAsync(m => m.TenantId == tenantId && m.IsActive, ct) >= limit)
        {
            Audit("membership.limit-reached", tenantId, $"user:{userId}");
            throw new Modulus.Core.Abstractions.Exceptions.ConflictException(
                $"The company has reached its limit of {limit} users.");
        }

        if (existing is null)
        {
            db.TenantMemberships.Add(new TenantMembershipEntity
            {
                UserId = userId,
                TenantId = tenantId,
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.IsActive = true;
        }

        await db.SaveChangesAsync(ct);
        Audit("membership.added", tenantId, $"user:{userId}");
        await NotifyAsync(AccessChangeKinds.Membership, "membership.added", tenantId, userId, ct);
        return true;
    }

    /// <summary>
    /// Revokes a membership (the row is kept, inactive). Takes effect on the user's next request.
    /// Returns <see langword="false"/> when no such membership exists.
    /// </summary>
    public async Task<bool> RemoveMemberAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        var existing = await db.TenantMemberships
            .FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId, ct);
        if (existing is null)
            return false;

        existing.IsActive = false;
        await db.SaveChangesAsync(ct);
        Audit("membership.removed", tenantId, $"user:{userId}");
        await NotifyAsync(AccessChangeKinds.Membership, "membership.removed", tenantId, userId, ct);
        return true;
    }

    private ValueTask NotifyAsync(string kind, string reason, Guid tenantId, Guid? userId, CancellationToken ct)
        => observers is null
            ? ValueTask.CompletedTask
            : observers.NotifyAccessChangedAsync(
                new AccessChange { Kind = kind, Reason = reason, TenantId = tenantId, UserId = userId }, ct: ct);

    private void Audit(string action, Guid tenantId, string target)
        => audit?.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Tenancy,
            Action = action,
            TenantId = tenantId,
            Actor = actor?.UserId?.ToString(),
            Target = target,
        });
}
