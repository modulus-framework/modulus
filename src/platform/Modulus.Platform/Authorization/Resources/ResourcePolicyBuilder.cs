namespace Modulus.Authorization.Resources;

/// <summary>
/// Fluent builder for a <see cref="ResourcePolicy"/>. Rules read as the control
/// intent they encode, for example:
/// <code>
/// ResourcePolicy.Define(p => p
///     .Allow("edit",    r => r.OwnedByCaller() &amp;&amp; r.InState("Draft", "Rejected"))
///     .Allow("edit",    r => r.CallerHasPermission("doc:edit:any"))
///     .Allow("approve", r => r.CallerHasPermission("doc:approve") &amp;&amp; r.InState("Submitted"))
///     .Transition("submit", from: ["Draft", "Rejected"], to: "Submitted",
///                 r => r.OwnedByCaller() || r.CallerHasPermission("doc:submit"))
///     .Deny("*", r => r.InState("Archived")));   // archived documents are immutable
/// </code>
/// </summary>
public sealed class ResourcePolicyBuilder
{
    private readonly List<ResourceRule> _rules = [];

    /// <summary>Permits <paramref name="action"/> when <paramref name="requirement"/> holds.</summary>
    public ResourcePolicyBuilder Allow(string action, Func<ResourceRequest, bool> requirement)
    {
        _rules.Add(new ResourceRule(PolicyEffect.Allow, Require(action), requirement));
        return this;
    }

    /// <summary>Refuses <paramref name="action"/> when <paramref name="requirement"/> holds (deny wins).</summary>
    public ResourcePolicyBuilder Deny(string action, Func<ResourceRequest, bool> requirement)
    {
        _rules.Add(new ResourceRule(PolicyEffect.Deny, Require(action), requirement));
        return this;
    }

    /// <summary>
    /// Permits a workflow <paramref name="transition"/> action only from one of the
    /// <paramref name="from"/> states (the transition guard) and when
    /// <paramref name="requirement"/> holds. <paramref name="to"/> is recorded as
    /// metadata for the policy matrix; applying the state change is the domain's job.
    /// </summary>
    public ResourcePolicyBuilder Transition(
        string transition,
        string[] from,
        string to,
        Func<ResourceRequest, bool> requirement)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(requirement);
        _rules.Add(new ResourceRule(
            PolicyEffect.Allow,
            Require(transition),
            r => r.InState(from) && requirement(r),
            to));
        return this;
    }

    /// <summary>
    /// Permits a workflow <paramref name="transition"/> guarded only by the source
    /// state — any caller who passes the upstream capability layer may perform it.
    /// </summary>
    public ResourcePolicyBuilder Transition(string transition, string[] from, string to)
        => Transition(transition, from, to, static _ => true);

    internal ResourcePolicy Build() => new(_rules);

    private static string Require(string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return action;
    }
}
