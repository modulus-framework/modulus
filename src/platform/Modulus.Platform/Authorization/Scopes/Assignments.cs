namespace Modulus.Authorization.Scopes;

using System.Collections.Concurrent;

/// <summary>
/// Links users to business objects (customers, warehouses, projects, ...): the "Assigned" scope reads these. The framework does
/// not know what is assigned; modules and tenants name the assignment types.
/// </summary>
public interface IAssignmentStore
{
    /// <summary>The objects of <paramref name="assignmentType"/> assigned to <paramref name="userId"/> at <paramref name="now"/> (effective-dated).</summary>
    IReadOnlySet<Guid> TargetsFor(Guid userId, string assignmentType, DateTimeOffset now);
}

/// <summary>A user-to-object assignment with an optional effective window.</summary>
/// <param name="UserId">The assigned user.</param>
/// <param name="AssignmentType">What kind of object (<c>customer</c>, <c>warehouse</c>, ...), case-insensitive.</param>
/// <param name="TargetId">The object.</param>
/// <param name="ValidFrom">Effective from this instant; null from the start.</param>
/// <param name="ValidUntil">Effective until this instant (exclusive); null for good.</param>
public sealed record Assignment(
    Guid UserId,
    string AssignmentType,
    Guid TargetId,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidUntil = null)
{
    /// <summary>Whether the assignment counts at <paramref name="now"/> (BR-016: only assignments active at decision time).</summary>
    public bool IsActiveAt(DateTimeOffset now)
        => (ValidFrom is null || now >= ValidFrom) && (ValidUntil is null || now < ValidUntil);
}

/// <summary>The default <see cref="IAssignmentStore"/>: in memory, empty until assignments are added (so "Assigned" matches nothing).</summary>
public sealed class InMemoryAssignmentStore : IAssignmentStore
{
    private readonly ConcurrentDictionary<(Guid User, string Type, Guid Target), Assignment> _assignments = new();

    /// <summary>Adds or replaces an assignment.</summary>
    public InMemoryAssignmentStore Assign(Assignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentException.ThrowIfNullOrWhiteSpace(assignment.AssignmentType);
        _assignments[Key(assignment.UserId, assignment.AssignmentType, assignment.TargetId)] = assignment;
        return this;
    }

    /// <summary>Adds an assignment that is effective now and does not expire.</summary>
    public InMemoryAssignmentStore Assign(Guid userId, string assignmentType, Guid targetId)
        => Assign(new Assignment(userId, assignmentType, targetId));

    /// <summary>Removes an assignment (no-op when it does not exist).</summary>
    public InMemoryAssignmentStore Unassign(Guid userId, string assignmentType, Guid targetId)
    {
        _assignments.TryRemove(Key(userId, assignmentType, targetId), out _);
        return this;
    }

    /// <inheritdoc />
    public IReadOnlySet<Guid> TargetsFor(Guid userId, string assignmentType, DateTimeOffset now)
        => _assignments.Values
            .Where(a => a.UserId == userId
                        && string.Equals(a.AssignmentType, assignmentType, StringComparison.OrdinalIgnoreCase)
                        && a.IsActiveAt(now))
            .Select(a => a.TargetId)
            .ToHashSet();

    private static (Guid, string, Guid) Key(Guid user, string type, Guid target) => (user, type.ToLowerInvariant(), target);
}
