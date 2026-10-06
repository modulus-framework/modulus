using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.Scopes;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// EF Core-backed <see cref="IAssignmentStore"/>: user-to-object assignments as durable, tenant-filtered, effective-dated rows,
/// so a change takes effect on the next request without a role change or a restart.
/// </summary>
public sealed class EfAssignmentStore(IDbContextFactory<AuthorizationStoreDbContext> factory) : IAssignmentStore
{
    /// <inheritdoc />
    public IReadOnlySet<Guid> TargetsFor(Guid userId, string assignmentType, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assignmentType);
        var type = assignmentType.ToLowerInvariant();

        using var db = factory.CreateDbContext();
        return db.Assignments.AsNoTracking()
            .Where(a => a.UserId == userId && a.AssignmentType == type)
            .AsEnumerable()
            .Where(a => (a.ValidFrom is null || now >= a.ValidFrom) && (a.ValidUntil is null || now < a.ValidUntil))
            .Select(a => a.TargetId)
            .ToHashSet();
    }

    /// <summary>Adds an assignment, or replaces the window of an existing one.</summary>
    public async Task AssignAsync(Assignment assignment, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentException.ThrowIfNullOrWhiteSpace(assignment.AssignmentType);
        if (assignment is { ValidFrom: { } from, ValidUntil: { } until } && until <= from)
            throw new ArgumentException("An assignment must end after it begins.", nameof(assignment));

        var type = assignment.AssignmentType.Trim().ToLowerInvariant();
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Assignments.SingleOrDefaultAsync(
            a => a.UserId == assignment.UserId && a.AssignmentType == type && a.TargetId == assignment.TargetId, ct);
        if (row is null)
        {
            row = new AssignmentRow { UserId = assignment.UserId, AssignmentType = type, TargetId = assignment.TargetId };
            db.Assignments.Add(row);
        }

        row.ValidFrom = assignment.ValidFrom;
        row.ValidUntil = assignment.ValidUntil;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Removes an assignment; false when it did not exist.</summary>
    public async Task<bool> UnassignAsync(Guid userId, string assignmentType, Guid targetId, CancellationToken ct = default)
    {
        var type = assignmentType.Trim().ToLowerInvariant();
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Assignments
            .Where(a => a.UserId == userId && a.AssignmentType == type && a.TargetId == targetId)
            .ExecuteDeleteAsync(ct) > 0;
    }

    /// <summary>Every assignment of a user, ended ones included, for review.</summary>
    public async Task<IReadOnlyCollection<Assignment>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Assignments.AsNoTracking().Where(a => a.UserId == userId)
            .OrderBy(a => a.AssignmentType).ToListAsync(ct);
        return [.. rows.Select(a => new Assignment(a.UserId, a.AssignmentType, a.TargetId, a.ValidFrom, a.ValidUntil))];
    }
}
