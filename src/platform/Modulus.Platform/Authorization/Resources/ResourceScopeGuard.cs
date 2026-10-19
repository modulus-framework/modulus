namespace Modulus.Authorization.Resources;

using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.DataQuality;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Abstractions.Exceptions;

/// <summary>
/// Decides whether a record, <em>as it will be after the write</em>, lies inside the caller's scope. Used for creates and for
/// updates that change a record's owner, org unit or scope-defining values. The permission that let the caller reach the endpoint
/// says nothing about the record the request produces: a caller who may edit invoices in one unit must not create one in another.
/// </summary>
public interface IResourceScopeGuard
{
    /// <summary>
    /// Checks <paramref name="resource"/> against the caller's tenant, org-unit scope and, when <paramref name="permission"/> is given,
    /// that permission's own scope. Returns <see cref="AccessReasonCodes.OutOfScope"/> (or <see cref="AccessReasonCodes.MissingAttribute"/>
    /// for an unset org unit) when refused. The decision names the attribute, never the record's values.
    /// </summary>
    Task<AccessDecision> CheckResultingScopeAsync(object resource, string action, string? permission = null, CancellationToken ct = default);
}

/// <summary>Default <see cref="IResourceScopeGuard"/>. Scoped, so it reads the caller's current tenant and data scope.</summary>
public sealed class ResourceScopeGuard(
    ICurrentTenant tenant,
    ICurrentDataScope dataScope,
    Scopes.IScopeEnforcer? scopes = null,
    IDataQualityFindingSink? findings = null,
    Scopes.IScopeMapRegistry? maps = null) : IResourceScopeGuard
{
    /// <inheritdoc />
    public Task<AccessDecision> CheckResultingScopeAsync(
        object resource, string action, string? permission = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        var type = resource.GetType().Name;

        // Tenant: a tenant-owned record must belong to the company the caller works in. Only the host context sees across companies,
        // and a caller with no company may not write a tenant-owned record at all.
        if (resource is IHasTenantId owned && !tenant.IsHost && (tenant.TenantId is not { } current || owned.TenantId != current))
            return Task.FromResult(AccessDecision.Deny(AccessReasonCodes.OutOfScope, $"the {type} belongs to another company"));

        // Org unit: the same rule the list filter and the single-record check apply. An unrestricted caller passes any unit.
        if (!dataScope.IsUnrestricted && resource is IHasOrgUnit orgScoped)
        {
            if (Known(orgScoped.OrgUnitId) is not { } unit)
            {
                // The unit is unset, so the record cannot be placed in scope: a missing attribute, reported as a finding.
                findings?.Record(new DataQualityFinding(tenant.TenantId, type, RecordIdOf(resource), action, ["OrgUnitId"]));
                return Task.FromResult(AccessDecision.Deny(AccessReasonCodes.MissingAttribute, $"the {type} has no OrgUnitId, so it cannot be placed in scope"));
            }

            if (!dataScope.OrgUnitIds.Contains(unit))
                return Task.FromResult(AccessDecision.Deny(AccessReasonCodes.OutOfScope, $"the {type} is outside the org units you may act on"));
        }

        // The permission's own scope (own, assigned, org unit or tenant), when the caller's grant for it is narrower than the company.
        if (permission is not null && scopes is not null && !scopes.IsInScope(resource, permission))
        {
            // A dimension value the permission's assigned scope needs is not set on the record: it cannot be placed in scope, a missing
            // attribute, reported as a finding (the same as a missing org unit). Otherwise the record is simply outside the scope.
            var missing = MissingAssignedValues(resource, scopes.Describe(permission));
            if (missing.Count > 0)
            {
                findings?.Record(new DataQualityFinding(tenant.TenantId, type, RecordIdOf(resource), action, missing));
                return Task.FromResult(AccessDecision.Deny(
                    AccessReasonCodes.MissingAttribute, $"the {type} has no {string.Join(", ", missing)}, so it cannot be placed in scope"));
            }

            return Task.FromResult(AccessDecision.Deny(AccessReasonCodes.OutOfScope, $"the {type} is outside the scope of '{permission}'"));
        }

        return Task.FromResult(AccessDecision.Allow());
    }

    private static Guid? Known(Guid id) => id == Guid.Empty ? null : id;

    // The assignment types a permission's assigned scope names, whose value the record leaves unset. Only the declared access keys
    // count: a type the map does not declare is not a missing value.
    private List<string> MissingAssignedValues(object resource, Scopes.ResolvedScope scope)
    {
        var missing = new List<string>();
        if (maps?.Find(resource.GetType()) is not { } map)
            return missing;

        var keys = map.FactsOf(resource).AccessKeys;
        if (keys is null)
            return missing;

        foreach (var constraint in scope.AnyOf.SelectMany(c => c.Constraints))
        {
            if (constraint.Kind != Scopes.ScopeKind.Assigned || constraint.Value is null)
                continue;
            if (keys.TryGetValue(constraint.Value, out var value) && value is null && !missing.Contains(constraint.Value, StringComparer.OrdinalIgnoreCase))
                missing.Add(constraint.Value);
        }

        return missing;
    }

    private static string? RecordIdOf(object resource)
    {
        var id = resource.GetType().GetProperty("Id")?.GetValue(resource)?.ToString();
        return id is null or "00000000-0000-0000-0000-000000000000" ? null : id;
    }
}

/// <summary>Throwing form of <see cref="IResourceScopeGuard"/>, for handlers.</summary>
public static class ResourceScopeGuardExtensions
{
    /// <summary>
    /// Refuses the write with <see cref="ForbiddenException"/> (403, the reason code in the message) when the record as it will be
    /// lies outside the caller's scope. Call it after the record is built or changed and before it is saved, in the same transaction.
    /// </summary>
    public static async Task EnsureResultingScopeAsync(
        this IResourceScopeGuard guard, object resource, string action, string? permission = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(guard);
        var decision = await guard.CheckResultingScopeAsync(resource, action, permission, ct).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            var type = resource.GetType().Name;
            throw new ForbiddenException(
                $"{type}:{action}",
                $"Not allowed to {action} this {type}: {decision.Code} ({decision.Reason}).");
        }
    }
}
