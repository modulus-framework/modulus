namespace Modulus.Authorization.Resources;

/// <summary>
/// The outcome of a resource/workflow authorization check: whether the action is
/// permitted on the instance, and — when denied — a human-readable reason for
/// diagnostics and audit logs plus a stable machine-readable <see cref="Code"/>
/// (see <see cref="AccessReasonCodes"/>). Deny-by-default: the evaluator returns a
/// <see cref="Deny(string)"/> unless a policy rule explicitly grants the action.
/// </summary>
/// <param name="IsAllowed">True when the action is permitted on the resource.</param>
/// <param name="Reason">Why the action was denied; <see langword="null"/> when allowed.</param>
/// <param name="Code">Stable reason code (<see cref="AccessReasonCodes"/>); never shown as prose.</param>
public sealed record AccessDecision(bool IsAllowed, string? Reason, string? Code = null)
{
    /// <summary>The shared allowed decision.</summary>
    public static readonly AccessDecision Allowed = new(true, null, AccessReasonCodes.Allowed);

    /// <summary>The action is permitted.</summary>
    public static AccessDecision Allow() => Allowed;

    /// <summary>The action is refused, with a diagnostic <paramref name="reason"/>.</summary>
    public static AccessDecision Deny(string reason) => new(false, reason, AccessReasonCodes.PolicyViolation);

    /// <summary>The action is refused with a specific reason <paramref name="code"/>.</summary>
    public static AccessDecision Deny(string code, string reason) => new(false, reason, code);
}

/// <summary>Stable reason codes for authorization decisions (BRS Appendix B).</summary>
public static class AccessReasonCodes
{
    /// <summary>The action is permitted.</summary>
    public const string Allowed = "ALLOWED";

    /// <summary>The record belongs to another tenant.</summary>
    public const string TenantMismatch = "TENANT_MISMATCH";

    /// <summary>The caller does not hold the permission.</summary>
    public const string PermissionNotGranted = "PERMISSION_NOT_GRANTED";

    /// <summary>An explicit deny applies.</summary>
    public const string ExplicitDeny = "EXPLICIT_DENY";

    /// <summary>A restriction removes the scope that would have allowed it.</summary>
    public const string ScopeRestricted = "SCOPE_RESTRICTED";

    /// <summary>The record is outside the granted scope.</summary>
    public const string OutOfScope = "OUT_OF_SCOPE";

    /// <summary>The caller does not own the record.</summary>
    public const string NotOwner = "NOT_OWNER";

    /// <summary>The record is not assigned to the caller.</summary>
    public const string NotAssigned = "NOT_ASSIGNED";

    /// <summary>The grant is expired or not yet valid.</summary>
    public const string GrantExpired = "GRANT_EXPIRED";

    /// <summary>A delegation is invalid, expired or exceeds the delegator's authority.</summary>
    public const string DelegationInvalid = "DELEGATION_INVALID";

    /// <summary>The record's workflow state forbids the action.</summary>
    public const string InvalidState = "INVALID_STATE";

    /// <summary>No policy rule grants the action, or a policy deny applies.</summary>
    public const string PolicyViolation = "POLICY_VIOLATION";

    /// <summary>A segregation-of-duties constraint is violated.</summary>
    public const string SoDConflict = "SOD_CONFLICT";

    /// <summary>The field is protected from the caller.</summary>
    public const string FieldProtected = "FIELD_PROTECTED";

    /// <summary>No policy/metadata is registered for the resource (fail closed).</summary>
    public const string MetadataMissing = "METADATA_MISSING";

    /// <summary>Evaluation failed; denied fail-closed.</summary>
    public const string EvaluationError = "EVALUATION_ERROR";
}
