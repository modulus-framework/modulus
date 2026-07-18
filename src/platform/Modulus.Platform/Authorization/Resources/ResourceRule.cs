namespace Modulus.Authorization.Resources;

/// <summary>Whether a matched rule grants or refuses the action.</summary>
public enum PolicyEffect
{
    /// <summary>The action is permitted when the requirement is satisfied.</summary>
    Allow = 0,

    /// <summary>The action is refused when the requirement is satisfied (deny wins).</summary>
    Deny = 1,
}

/// <summary>
/// One declarative rule in a <see cref="ResourcePolicy"/>: it binds an
/// <see cref="Action"/> to a <see cref="Requirement"/> over the
/// <see cref="ResourceRequest"/> and an <see cref="Effect"/>. Rules are
/// data — surfaced via <see cref="ResourcePolicy.Rules"/> so the (state × action ×
/// who) matrix can be reviewed by administrators (blueprint §5.8 best practice),
/// rather than buried in <c>switch</c> statements in handlers.
/// </summary>
/// <param name="Effect">Whether satisfying <paramref name="Requirement"/> allows or denies.</param>
/// <param name="Action">The action this rule governs, or <c>*</c> for every action.</param>
/// <param name="Requirement">The condition, over the request, under which the effect applies.</param>
/// <param name="ToState">For a transition rule, the state the action moves the record into (metadata only).</param>
public sealed record ResourceRule(
    PolicyEffect Effect,
    string Action,
    Func<ResourceRequest, bool> Requirement,
    string? ToState = null)
{
    /// <summary>The wildcard action token that matches every action.</summary>
    public const string AnyAction = "*";

    /// <summary>Whether this rule governs <paramref name="action"/> (its action or the wildcard).</summary>
    public bool AppliesTo(string action)
        => Action == AnyAction || string.Equals(Action, action, StringComparison.OrdinalIgnoreCase);
}
