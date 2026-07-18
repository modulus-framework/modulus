namespace Modulus.Authorization.Fields;

/// <summary>What a principal must satisfy to read or write one field.</summary>
public enum FieldRequirementKind
{
    /// <summary>No clearance needed — any caller past the upstream layers may access the field.</summary>
    Open,

    /// <summary>The caller must hold a specific permission.</summary>
    Permission,

    /// <summary>No one may access the field through this profile (fail-closed default for sensitive, unconfigured fields).</summary>
    Closed,
}

/// <summary>
/// The resolved clearance a field security profile demands for one direction (read or
/// write) of one field: open to all, gated on a named permission, or closed. This is the
/// unit a <see cref="FieldSecurityProfile"/> produces per field and the field authorizer
/// tests against the current principal — deny-by-default, so an unconfigured sensitive
/// field is <see cref="Closed"/> until a profile explicitly opens it.
/// </summary>
/// <param name="Kind">Whether access is open, permission-gated, or closed.</param>
/// <param name="Permission">The required permission when <see cref="Kind"/> is <see cref="FieldRequirementKind.Permission"/>; otherwise <see langword="null"/>.</param>
public readonly record struct FieldRequirement(FieldRequirementKind Kind, string? Permission)
{
    /// <summary>Access is granted to any caller.</summary>
    public static readonly FieldRequirement Open = new(FieldRequirementKind.Open, null);

    /// <summary>Access is denied to every caller through this profile.</summary>
    public static readonly FieldRequirement Closed = new(FieldRequirementKind.Closed, null);

    /// <summary>Access requires holding <paramref name="permission"/>.</summary>
    public static FieldRequirement Require(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        return new FieldRequirement(FieldRequirementKind.Permission, permission);
    }

    /// <summary>
    /// True when <paramref name="hasPermission"/> shows the caller clears this
    /// requirement. Fail-closed: a <see cref="FieldRequirementKind.Closed"/> requirement
    /// is never satisfied.
    /// </summary>
    public bool IsSatisfiedBy(Func<string, bool> hasPermission)
    {
        ArgumentNullException.ThrowIfNull(hasPermission);
        return Kind switch
        {
            FieldRequirementKind.Open => true,
            FieldRequirementKind.Permission => hasPermission(Permission!),
            _ => false,
        };
    }
}
