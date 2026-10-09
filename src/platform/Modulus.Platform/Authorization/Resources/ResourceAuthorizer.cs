namespace Modulus.Authorization.Resources;

using Modulus.Core.Abstractions;

/// <summary>
/// The enforcement point for instance-level (resource/workflow) authorization: call it
/// in a command handler once the record is loaded to decide whether the current
/// principal may perform an action on <em>that specific record right now</em> —
/// "may this user approve <i>this</i> invoice, given they hold <c>doc:approve</c> and
/// it is Submitted?" (blueprint §5.7, §5.8).
/// </summary>
public interface IResourceAuthorizer
{
    /// <summary>
    /// Decides whether the current principal may perform <paramref name="action"/> on
    /// <paramref name="resource"/>. Fail-closed: a resource type with no registered
    /// policy, or a policy with no granting rule, denies. Async so a decorator can
    /// durably record the decision (<c>AddScopedDecisionAuditing</c>, blueprint
    /// §5.14/§16) — the built-in implementation itself is pure in-memory evaluation.
    /// </summary>
    Task<AccessDecision> AuthorizeAsync(
        object resource, string action, CancellationToken ct = default);
}

/// <summary>Per-record questions built on <see cref="IResourceAuthorizer"/>: what may the caller do here, and why not.</summary>
public static class ResourceAuthorizerExtensions
{
    /// <summary>
    /// The actions of the record's policy the current principal may perform right now, in policy order — what a client
    /// shows as buttons. Each action goes through the same <see cref="IResourceAuthorizer"/> (and so the same audit
    /// decorator) as a real attempt. A resource type with no policy has none.
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetAvailableActionsAsync(
        this IResourceAuthorizer authorizer, IResourcePolicyRegistry registry, object resource, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(authorizer);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(resource);

        var policy = registry.Find(resource.GetType());
        if (policy is null)
            return [];

        var available = new List<string>();
        foreach (var action in policy.Actions)
        {
            if ((await authorizer.AuthorizeAsync(resource, action, ct).ConfigureAwait(false)).IsAllowed)
                available.Add(action);
        }

        return available;
    }
}

/// <summary>
/// Bridges <see cref="IResourceAuthorizer"/> to the current request: builds a
/// <see cref="ResourceRequest"/> from the principal's <em>identity</em>
/// (<see cref="ICurrentUser"/>) and data scope (<see cref="ICurrentDataScope"/>) plus
/// the resource's <see cref="ResourceAttributes"/>, then evaluates the registered
/// <see cref="ResourcePolicy"/>. Scoped. The in-scope probe reuses
/// <see cref="ICurrentDataScope"/>, so the single-item rule and the bulk list filter
/// (increment 3) never diverge.
/// </summary>
public sealed class ResourceAuthorizer(
    ICurrentUser currentUser,
    ICurrentDataScope dataScope,
    IResourcePolicyRegistry registry,
    Scopes.IScopeEnforcer? scopes = null,
    Approval.IApprovalAuthorityEvaluator? approvals = null) : IResourceAuthorizer
{
    /// <summary>
    /// Explains the decision for <paramref name="action"/> on <paramref name="resource"/>: the decision plus which
    /// rules matched. For diagnostics and support tooling; it does not audit, so expose it only to administrators.
    /// </summary>
    public PolicyExplanation Explain(object resource, string action)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        var policy = registry.Find(resource.GetType());
        if (policy is null)
        {
            return new PolicyExplanation(
                AccessDecision.Deny(AccessReasonCodes.MetadataMissing,
                    $"no resource policy is registered for '{resource.GetType().Name}'"),
                []);
        }

        return policy.Explain(BuildRequest(resource, action));
    }

    public Task<AccessDecision> AuthorizeAsync(
        object resource, string action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        var policy = registry.Find(resource.GetType());
        if (policy is null)
            return Task.FromResult(AccessDecision.Deny(
                AccessReasonCodes.MetadataMissing,
                $"no resource policy is registered for '{resource.GetType().Name}'"));

        return Task.FromResult(policy.Evaluate(BuildRequest(resource, action)));
    }

    private ResourceRequest BuildRequest(object resource, string action)
        => new(
            currentUser.UserId,
            currentUser.HasPermission,
            unit => dataScope.IsUnrestricted
                    || (unit is { } u && dataScope.OrgUnitIds.Contains(u)),
            ResourceAttributes.From(resource),
            action,
            scopes is null
                ? null
                : permission => scopes.IsInScope(resource, permission),
            approvals is null
                ? null
                : permission => approvals.Check(permission, resource).IsWithinAuthority,
            (resource as Core.Abstractions.Entities.IHasApprovalTrail)?.ActedByUserIds);
}
