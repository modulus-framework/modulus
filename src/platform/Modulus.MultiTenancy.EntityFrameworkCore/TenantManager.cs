using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;

namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// Provisioning surface for the EF-backed tenant store: create tenants and toggle
/// their active state. Registered as a scoped service by
/// <c>AddEfCoreTenantStore</c>. Reads go through <see cref="ITenantStore"/> /
/// <see cref="IEfTenantStore"/>; this is the write side, used by admin endpoints or
/// seed code. Membership and activation changes are recorded in the tenant's security audit chain and reported
/// to every <see cref="IAccessChangeObserver"/> (systems that cache access, such as the AI connector).
/// </summary>
public class TenantManager<TTenant, TMembership, TDataStore>(
    TenantStoreDbContext<TTenant, TMembership, TDataStore> db,
    ISecurityAuditLog? audit = null,
    ICurrentUser? actor = null,
    IEnumerable<IAccessChangeObserver>? observers = null) : ITenantMemberProvisioner
    where TTenant : TenantEntity, new()
    where TMembership : TenantMembershipEntity, new()
    where TDataStore : TenantDataStoreEntity
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

        var entity = new TTenant
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
    /// immediately (see <see cref="IEfTenantStore"/>). Returns
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
    /// Sets the parent company of <paramref name="id"/> (null makes it top-level). A company cannot be its own parent, and a link that
    /// would close a cycle is refused with <see cref="ArgumentException"/>; a missing parent is refused the same way. Returns
    /// <see langword="false"/> if no tenant has the id.
    /// </summary>
    public async Task<bool> SetParentAsync(Guid id, Guid? parentId, CancellationToken ct = default)
    {
        var entity = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (entity is null) return false;
        if (parentId is { } parent)
        {
            if (parent == id)
                throw new ArgumentException("A company cannot be its own parent.", nameof(parentId));
            if (!await db.Tenants.AnyAsync(t => t.Id == parent, ct))
                throw new ArgumentException("The parent company does not exist.", nameof(parentId));

            // Walk up from the proposed parent: reaching the company itself would make a cycle.
            var cursor = parent;
            for (var depth = 0; depth < MaxHierarchyDepth; depth++)
            {
                var next = await db.Tenants.AsNoTracking().Where(t => t.Id == cursor).Select(t => t.ParentId).FirstOrDefaultAsync(ct);
                if (next is null) break;
                if (next == id)
                    throw new ArgumentException("That parent would make the company its own ancestor.", nameof(parentId));
                cursor = next.Value;
            }
        }

        entity.ParentId = parentId;
        await db.SaveChangesAsync(ct);
        Audit("tenant.parent-changed", id, $"tenant:{id} parent={parentId?.ToString("D") ?? "none"}");
        return true;
    }

    /// <summary>Grants <paramref name="userId"/> membership in <paramref name="tenantId"/> (one login across
    /// companies), or re-activates a revoked one. Returns <see langword="false"/> when the tenant
    /// does not exist.
    /// </summary>
    public async Task<bool> AddMemberAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        // The seat count and the insert must not interleave with another add for the same company: on a server database two adds could
        // both see a free seat. The company row is locked for the transaction (a no-op update), so a concurrent add waits at the lock
        // until this one commits, then counts the seat it took.
        var strategy = db.Database.CreateExecutionStrategy();
        var added = await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var locked = await db.Tenants.Where(t => t.Id == tenantId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.MaxUsers, t => t.MaxUsers), ct);
            if (locked == 0)
                return false;

            var limit = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.MaxUsers).FirstOrDefaultAsync(ct);
            var existing = await db.TenantMemberships
                .FirstOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId, ct);
            if (existing is not { IsActive: true } && limit is { } max
                && await db.TenantMemberships.CountAsync(m => m.TenantId == tenantId && m.IsActive, ct) >= max)
            {
                Audit("membership.limit-reached", tenantId, $"user:{userId}");
                throw new Modulus.Core.Abstractions.Exceptions.ConflictException(
                    $"The company has reached its limit of {max} users.");
            }

            if (existing is null)
            {
                db.TenantMemberships.Add(new TMembership
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
            await tx.CommitAsync(ct);
            return true;
        });
        if (!added)
            return false;

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

    /// <summary>
    /// One page of companies, newest last by slug, optionally filtered by a part of the slug or the display name. Reads only; the
    /// caller is responsible for the host context.
    /// </summary>
    public async Task<(IReadOnlyList<TenantAdminView> Items, int Total)> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var query = db.Tenants.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(t => t.Slug.Contains(term) || (t.DisplayName != null && t.DisplayName.Contains(term)));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(t => t.Slug)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ViewOf)
            .ToListAsync(ct);
        return (items, total);
    }

    /// <summary>One company, or null when no company has the id.</summary>
    public Task<TenantAdminView?> GetAsync(Guid id, CancellationToken ct = default)
        => db.Tenants.AsNoTracking().Where(t => t.Id == id).Select(ViewOf).FirstOrDefaultAsync(ct);

    /// <summary>Every membership of a company, active or revoked, oldest first.</summary>
    public async Task<IReadOnlyList<TenantMemberView>> ListMembersAsync(Guid tenantId, CancellationToken ct = default)
    {
        // Sorted here: SQLite cannot order by DateTimeOffset in the database.
        var members = await db.TenantMemberships.AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .Select(m => new TenantMemberView(m.UserId, m.IsActive, m.CreatedAtUtc))
            .ToListAsync(ct);
        return members.OrderBy(m => m.AddedAtUtc).ToList();
    }

    // A bound on the ancestor walk: a deeper chain than this is refused rather than walked forever on corrupt data.
    private const int MaxHierarchyDepth = 64;

    private System.Linq.Expressions.Expression<Func<TTenant, TenantAdminView>> ViewOf => t => new TenantAdminView(
        t.Id,
        t.Slug,
        t.DisplayName,
        t.GroupId,
        t.Status,
        t.TrialEndsAt,
        t.MaxUsers,
        t.ParentId,
        db.TenantMemberships.Count(m => m.TenantId == t.Id && m.IsActive),
        t.CreatedAtUtc);

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
