using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>Why access was asked for.</summary>
public enum AccessRequestKind
{
    /// <summary>An ordinary request: an approver decides before any access is given.</summary>
    Request = 0,

    /// <summary>Emergency access (break-glass): given at once from a configured profile, reviewed afterwards.</summary>
    BreakGlass = 1,
}

/// <summary>Where a request stands.</summary>
public enum AccessRequestStatus
{
    /// <summary>Waiting for an approver.</summary>
    Pending = 0,

    /// <summary>Approved: temporary grants were made.</summary>
    Approved = 1,

    /// <summary>Refused by an approver.</summary>
    Denied = 2,

    /// <summary>Withdrawn by the requester.</summary>
    Cancelled = 3,

    /// <summary>Emergency access in force (or past); it still needs a review.</summary>
    Activated = 4,
}

/// <summary>A request for temporary access, or a break-glass activation.</summary>
/// <param name="Id">The request id.</param>
/// <param name="Kind">Request or break-glass.</param>
/// <param name="RequesterId">Who asked.</param>
/// <param name="Permissions">The permissions asked for.</param>
/// <param name="Reason">The justification (required).</param>
/// <param name="Hours">How long the access lasts once given.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="CreatedAt">When it was made.</param>
/// <param name="DecidedBy">The approver or refuser.</param>
/// <param name="DecidedAt">When it was decided.</param>
/// <param name="Note">The approver's note.</param>
/// <param name="AccessEndsAt">When the access it gave stops.</param>
/// <param name="ReviewedBy">Who reviewed a break-glass use afterwards.</param>
/// <param name="ReviewedAt">When it was reviewed.</param>
public sealed record AccessRequest(
    Guid Id, AccessRequestKind Kind, Guid RequesterId, IReadOnlyList<string> Permissions, string Reason, int Hours,
    AccessRequestStatus Status, DateTimeOffset CreatedAt, Guid? DecidedBy = null, DateTimeOffset? DecidedAt = null,
    string? Note = null, DateTimeOffset? AccessEndsAt = null, Guid? ReviewedBy = null, DateTimeOffset? ReviewedAt = null);

/// <summary>Access requests and break-glass activations of the current company (tenant-filtered table <c>ModulusAccessRequests</c>).</summary>
public sealed class EfAccessRequestStore(IDbContextFactory<AuthorizationStoreDbContext> factory)
{
    /// <summary>Records a new request or activation.</summary>
    public async Task<AccessRequest> CreateAsync(AccessRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var row = new AccessRequestRow
        {
            Id = request.Id == Guid.Empty ? Guid.CreateVersion7() : request.Id,
            Kind = request.Kind,
            RequesterId = request.RequesterId,
            Permissions = JsonSerializer.Serialize(request.Permissions),
            Reason = request.Reason,
            Hours = request.Hours,
            Status = request.Status,
            CreatedAt = request.CreatedAt,
            DecidedBy = request.DecidedBy,
            DecidedAt = request.DecidedAt,
            Note = request.Note,
            AccessEndsAt = request.AccessEndsAt,
        };
        await using var db = await factory.CreateDbContextAsync(ct);
        db.AccessRequests.Add(row);
        await db.SaveChangesAsync(ct);
        return ToRecord(row);
    }

    /// <summary>One request by id; null when it does not exist in this company.</summary>
    public async Task<AccessRequest?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.AccessRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : ToRecord(row);
    }

    /// <summary>How many requests of the user are waiting.</summary>
    public async Task<int> CountPendingAsync(Guid requesterId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AccessRequests.CountAsync(r => r.RequesterId == requesterId && r.Status == AccessRequestStatus.Pending, ct);
    }

    /// <summary>The user's own requests, newest first.</summary>
    public async Task<IReadOnlyList<AccessRequest>> ListForRequesterAsync(Guid requesterId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.AccessRequests.AsNoTracking().Where(r => r.RequesterId == requesterId).ToListAsync(ct);
        return [.. rows.OrderByDescending(r => r.CreatedAt).Select(ToRecord)];
    }

    /// <summary>Requests in a status (and kind), oldest first: an approver's queue, a reviewer's list.</summary>
    public async Task<IReadOnlyList<AccessRequest>> ListAsync(
        AccessRequestStatus status, AccessRequestKind? kind = null, bool unreviewedOnly = false, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = db.AccessRequests.AsNoTracking().Where(r => r.Status == status);
        if (kind is { } k)
            rows = rows.Where(r => r.Kind == k);
        if (unreviewedOnly)
            rows = rows.Where(r => r.ReviewedAt == null);
        return [.. (await rows.ToListAsync(ct)).OrderBy(r => r.CreatedAt).Select(ToRecord)];
    }

    /// <summary>
    /// Moves a request from <paramref name="expected"/> to <paramref name="next"/> in one conditional update, so two approvers cannot
    /// both decide the same request. False when it is no longer in <paramref name="expected"/>.
    /// </summary>
    public async Task<bool> DecideAsync(
        Guid id, AccessRequestStatus expected, AccessRequestStatus next, Guid? decidedBy, DateTimeOffset now, string? note,
        DateTimeOffset? accessEndsAt, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var updated = await db.AccessRequests.Where(r => r.Id == id && r.Status == expected)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, next)
                .SetProperty(r => r.DecidedBy, decidedBy)
                .SetProperty(r => r.DecidedAt, now)
                .SetProperty(r => r.Note, note)
                .SetProperty(r => r.AccessEndsAt, accessEndsAt), ct);
        return updated > 0;
    }

    /// <summary>Marks a break-glass use reviewed; false when it is not an unreviewed break-glass record.</summary>
    public async Task<bool> ReviewAsync(Guid id, Guid? reviewer, DateTimeOffset now, string? note, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AccessRequests.Where(r => r.Id == id && r.Kind == AccessRequestKind.BreakGlass && r.ReviewedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.ReviewedBy, reviewer)
                .SetProperty(r => r.ReviewedAt, now)
                .SetProperty(r => r.Note, note), ct) > 0;
    }

    private static AccessRequest ToRecord(AccessRequestRow r) => new(
        r.Id, r.Kind, r.RequesterId, JsonSerializer.Deserialize<string[]>(r.Permissions) ?? [], r.Reason, r.Hours, r.Status, r.CreatedAt,
        r.DecidedBy, r.DecidedAt, r.Note, r.AccessEndsAt, r.ReviewedBy, r.ReviewedAt);
}
