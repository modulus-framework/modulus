namespace Modulus.Authorization.Resources;

/// <summary>
/// The outcome of a resource/workflow authorization check: whether the action is
/// permitted on the instance, and — when denied — a human-readable reason for
/// diagnostics and audit logs. Deny-by-default: the evaluator returns a
/// <see cref="Deny"/> unless a policy rule explicitly grants the action.
/// </summary>
/// <param name="IsAllowed">True when the action is permitted on the resource.</param>
/// <param name="Reason">Why the action was denied; <see langword="null"/> when allowed.</param>
public sealed record AccessDecision(bool IsAllowed, string? Reason)
{
    /// <summary>The shared allowed decision.</summary>
    public static readonly AccessDecision Allowed = new(true, null);

    /// <summary>The action is permitted.</summary>
    public static AccessDecision Allow() => Allowed;

    /// <summary>The action is refused, with a diagnostic <paramref name="reason"/>.</summary>
    public static AccessDecision Deny(string reason) => new(false, reason);
}
