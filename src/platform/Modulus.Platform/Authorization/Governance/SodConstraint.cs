namespace Modulus.Authorization.Governance;

/// <summary>
/// A segregation-of-duties constraint: a set of permissions that are <b>mutually
/// exclusive</b> for a single principal (blueprint §5.6, §13). Holding two or more of them
/// is a toxic combination — the classic "maker cannot be checker" / "cannot approve a
/// payment you set up" control. Modelled as data so the framework can <i>analyze</i> it as
/// a standing control (who currently violates it) and <i>prevent</i> it (would this grant
/// create a violation?), rather than encoding it in ad-hoc role design.
/// </summary>
/// <param name="Name">A stable name for the control, for reports and attestation.</param>
/// <param name="MutuallyExclusive">The permissions at most one of which a principal may hold.</param>
/// <param name="Rationale">Why the separation exists (the control objective), for auditors.</param>
public sealed record SodConstraint(
    string Name,
    IReadOnlyCollection<string> MutuallyExclusive,
    string? Rationale = null);

/// <summary>
/// A detected breach of a <see cref="SodConstraint"/>: the principal holds the listed
/// <see cref="HeldPermissions"/>, which the constraint forbids together.
/// </summary>
/// <param name="Constraint">The violated constraint.</param>
/// <param name="HeldPermissions">The specific mutually-exclusive permissions the principal holds.</param>
public sealed record SodViolation(
    SodConstraint Constraint,
    IReadOnlyCollection<string> HeldPermissions);
