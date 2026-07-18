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
    /// policy, or a policy with no granting rule, denies.
    /// </summary>
    AccessDecision Authorize(object resource, string action);
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
    IResourcePolicyRegistry registry) : IResourceAuthorizer
{
    public AccessDecision Authorize(object resource, string action)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        var policy = registry.Find(resource.GetType());
        if (policy is null)
            return AccessDecision.Deny(
                $"no resource policy is registered for '{resource.GetType().Name}'");

        var request = new ResourceRequest(
            currentUser.UserId,
            currentUser.HasPermission,
            unit => dataScope.IsUnrestricted
                    || (unit is { } u && dataScope.OrgUnitIds.Contains(u)),
            ResourceAttributes.From(resource),
            action);

        return policy.Evaluate(request);
    }
}
