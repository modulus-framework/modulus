namespace Modulus.Authorization.Governance;

/// <summary>
/// The set of <see cref="SodConstraint"/>s in force, evaluated against a principal's
/// effective permissions to detect toxic combinations (blueprint §5.6, §13). It is the
/// analyzable standing control the framework promises: run <see cref="Evaluate"/> over any
/// user's effective set for a recertification/attestation report, or over a <i>proposed</i>
/// effective set (current + a permission about to be granted) to block creating a
/// violation.
/// </summary>
public interface ISodPolicy
{
    /// <summary>The constraints being enforced — surfaced for governance review.</summary>
    IReadOnlyCollection<SodConstraint> Constraints { get; }

    /// <summary>
    /// Every constraint the principal breaches given <paramref name="effectivePermissions"/>
    /// — i.e. those where they hold two or more mutually-exclusive permissions. Empty when
    /// compliant.
    /// </summary>
    IReadOnlyCollection<SodViolation> Evaluate(IReadOnlySet<string> effectivePermissions);
}

/// <summary>
/// Default <see cref="ISodPolicy"/> over a fixed list of constraints. Stateless and
/// thread-safe; matching is case-insensitive.
/// </summary>
public sealed class SodPolicy : ISodPolicy
{
    private readonly IReadOnlyList<SodConstraint> _constraints;

    /// <summary>The empty policy — no constraints, so nothing ever violates.</summary>
    public static readonly SodPolicy Empty = new([]);

    public SodPolicy(IEnumerable<SodConstraint> constraints)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        _constraints = [.. constraints];
    }

    public IReadOnlyCollection<SodConstraint> Constraints => _constraints;

    public IReadOnlyCollection<SodViolation> Evaluate(IReadOnlySet<string> effectivePermissions)
    {
        ArgumentNullException.ThrowIfNull(effectivePermissions);
        if (_constraints.Count == 0)
            return [];

        var violations = new List<SodViolation>();
        foreach (var constraint in _constraints)
        {
            var held = constraint.MutuallyExclusive
                .Where(effectivePermissions.Contains)
                .ToArray();

            // A constraint is breached only when two or more exclusive permissions co-occur.
            if (held.Length >= 2)
                violations.Add(new SodViolation(constraint, held));
        }

        return violations;
    }
}
