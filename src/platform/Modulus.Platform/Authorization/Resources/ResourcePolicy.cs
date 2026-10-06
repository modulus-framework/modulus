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

    /// <summary>The distinct actions the policy names (the wildcard <c>*</c> rule is not an action), in declaration order.</summary>
    public IReadOnlyList<string> Actions
        => [.. _rules.Select(r => r.Action).Where(a => a != ResourceRule.AnyAction).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Evaluates the policy and reports, for every rule that applies to the action, whether it matched — the
    /// answer to "why can or can't this caller do this to this record". It never throws on a rule that does:
    /// a rule whose requirement throws counts as not matched and is flagged <see cref="RuleOutcome.Faulted"/>
    /// (the decision itself stays fail-closed through <see cref="Evaluate"/>).
    /// </summary>
    public PolicyExplanation Explain(ResourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var outcomes = new List<RuleOutcome>();
        for (var i = 0; i < _rules.Count; i++)
        {
            var rule = _rules[i];
            if (!rule.AppliesTo(request.Action))
                continue;

            var matched = false;
            var faulted = false;
            try
            {
                matched = rule.Requirement(request);
            }
            catch (Exception)
            {
                faulted = true;
            }

            outcomes.Add(new RuleOutcome(i, rule.Effect, rule.Action, matched, faulted));
        }

        return new PolicyExplanation(Evaluate(request), outcomes);
    }

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
            if (!rule.AppliesTo(request.Action))
                continue;

            bool satisfied;
            try
            {
                satisfied = rule.Requirement(request);
            }
            catch (Exception)
            {
                // A rule that throws must never open access, and an allow that cannot be evaluated is not an allow.
                return AccessDecision.Deny(AccessReasonCodes.EvaluationError,
                    $"a policy rule for action '{request.Action}' failed to evaluate");
            }

            if (!satisfied)
                continue;

            if (rule.Effect == PolicyEffect.Deny)
                return AccessDecision.Deny(
                    AccessReasonCodes.PolicyViolation,
                    $"action '{request.Action}' is denied by policy on this resource");

            matchedAllow = true;
        }

        return matchedAllow
            ? AccessDecision.Allow()
            : AccessDecision.Deny(AccessReasonCodes.PolicyViolation, $"no policy rule grants action '{request.Action}' on this resource");
    }
}

/// <summary>How one rule fared while explaining a decision.</summary>
/// <param name="Index">The rule's position in the policy (declaration order).</param>
/// <param name="Effect">Allow or deny.</param>
/// <param name="Action">The action the rule names (<c>*</c> for any).</param>
/// <param name="Matched">Whether its requirement held for this caller and record.</param>
/// <param name="Faulted">Whether the requirement threw (treated as not matched).</param>
public sealed record RuleOutcome(int Index, PolicyEffect Effect, string Action, bool Matched, bool Faulted);

/// <summary>A decision with the per-rule evidence behind it.</summary>
/// <param name="Decision">The authoritative decision (same as <see cref="ResourcePolicy.Evaluate"/>).</param>
/// <param name="Rules">Every rule that applies to the action and whether it matched.</param>
public sealed record PolicyExplanation(AccessDecision Decision, IReadOnlyList<RuleOutcome> Rules);
