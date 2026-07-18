namespace Modulus.Authorization.Resources;

/// <summary>
/// A declarative, instance-level authorization policy for one resource type — the
/// resource/workflow layer of the pipeline (blueprint §5.7, §5.8). It is an ordered
/// set of <see cref="ResourceRule"/>s evaluated <b>deny-by-default</b> with
/// <b>deny-override</b>: for a given action, if any satisfied <see cref="PolicyEffect.Deny"/>
/// rule matches the action is refused; otherwise it is permitted only if some
/// satisfied <see cref="PolicyEffect.Allow"/> rule matches; with no matching allow the
/// action is refused. This mirrors the grant resolver's allow/deny semantics, so the
/// whole authorization stack fails closed consistently.
/// </summary>
public sealed class ResourcePolicy
{
    private readonly IReadOnlyList<ResourceRule> _rules;

    internal ResourcePolicy(IReadOnlyList<ResourceRule> rules) => _rules = rules;

    /// <summary>Builds a policy from a fluent rule declaration.</summary>
    public static ResourcePolicy Define(Action<ResourcePolicyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new ResourcePolicyBuilder();
        configure(builder);
        return builder.Build();
    }

    /// <summary>The rules, in declaration order — for administrative review of the policy matrix.</summary>
    public IReadOnlyList<ResourceRule> Rules => _rules;

    /// <summary>
    /// Evaluates the policy for the requested action on the requested resource.
    /// Deny-override then allow-if-any, else fail-closed.
    /// </summary>
    public AccessDecision Evaluate(ResourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var matchedAllow = false;
        foreach (var rule in _rules)
        {
            if (!rule.AppliesTo(request.Action) || !rule.Requirement(request))
                continue;

            if (rule.Effect == PolicyEffect.Deny)
                return AccessDecision.Deny(
                    $"action '{request.Action}' is denied by policy on this resource");

            matchedAllow = true;
        }

        return matchedAllow
            ? AccessDecision.Allow()
            : AccessDecision.Deny($"no policy rule grants action '{request.Action}' on this resource");
    }
}
