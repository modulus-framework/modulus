namespace Modulus.Authorization.Scopes;

/// <summary>The authorization-relevant keys of one record: who owns it, which org unit it belongs to, and the objects it hangs off.</summary>
/// <param name="OwnerId">The owning user, if the record has an owner.</param>
/// <param name="OrgUnitId">The owning org unit, if the record is org-scoped.</param>
/// <param name="AccessKeys">Assignable objects the record belongs to, by assignment type (a customer id, a warehouse id, ...).</param>
public sealed record ScopeFacts(
    Guid? OwnerId,
    Guid? OrgUnitId,
    IReadOnlyDictionary<string, Guid?>? AccessKeys = null);

/// <summary>What a caller brings to a scope check: who they are and which units and objects they are tied to.</summary>
public interface IScopeSubject
{
    /// <summary>The caller's user id; null for an anonymous caller (nothing then matches).</summary>
    Guid? UserId { get; }

    /// <summary>Whether the caller's own org scope is lifted (the bypass grant).</summary>
    bool IsOrgUnrestricted { get; }

    /// <summary>The org units of the caller's own scope.</summary>
    IReadOnlySet<Guid> OrgUnits { get; }

    /// <summary><paramref name="unit"/> and its descendants.</summary>
    IReadOnlySet<Guid> OrgSubtree(Guid unit);

    /// <summary>The objects of <paramref name="assignmentType"/> the caller is assigned to right now.</summary>
    IReadOnlySet<Guid> AssignedTargets(string assignmentType);
}

/// <summary>Checks one record against a <see cref="ResolvedScope"/>; fail-closed throughout.</summary>
public static class ScopeEvaluator
{
    /// <summary>Whether <paramref name="facts"/> satisfy at least one alternative of <paramref name="scope"/>.</summary>
    public static bool Allows(ResolvedScope scope, ScopeFacts facts, IScopeSubject subject)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(subject);

        return scope.AnyOf.Any(clause => clause.Constraints.All(c => Satisfies(c, facts, subject)));
    }

    private static bool Satisfies(PermissionScope constraint, ScopeFacts facts, IScopeSubject subject)
    {
        switch (constraint.Kind)
        {
            case ScopeKind.Tenant:
                return true;

            case ScopeKind.Own:
                return subject.UserId is { } me && facts.OwnerId == me;

            case ScopeKind.OrgUnit:
                if (constraint.OrgUnitId is { } named)
                    return facts.OrgUnitId is { } unit && subject.OrgSubtree(named).Contains(unit);
                return subject.IsOrgUnrestricted || (facts.OrgUnitId is { } own && subject.OrgUnits.Contains(own));

            case ScopeKind.Assigned:
                return facts.AccessKeys is { } keys
                       && keys.TryGetValue(constraint.Value!, out var key)
                       && key is { } target
                       && subject.AssignedTargets(constraint.Value!).Contains(target);

            default:
                return false;
        }
    }
}
