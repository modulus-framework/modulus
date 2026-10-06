namespace Modulus.Authorization.Scopes;

/// <summary>The kinds of data a grant can cover ("same permission, different data").</summary>
public enum ScopeKind
{
    /// <summary>Every record of the company (the default, and what a grant without a scope means).</summary>
    Tenant = 0,

    /// <summary>Records of an org unit and its descendants: the caller's own placement, or a named unit.</summary>
    OrgUnit = 1,

    /// <summary>Records the caller owns.</summary>
    Own = 2,

    /// <summary>Records tied to objects the caller is explicitly assigned to (customers, warehouses, projects, ...).</summary>
    Assigned = 3,
}

/// <summary>
/// What a grant covers. <see cref="ScopeKind.OrgUnit"/> with no value means "the caller's own org scope"; with a value it is
/// that unit and its descendants. <see cref="ScopeKind.Assigned"/> needs the assignment type (e.g. <c>customer</c>).
/// </summary>
public sealed record PermissionScope
{
    /// <summary>Creates a scope; use the factory members for the common shapes.</summary>
    public PermissionScope(ScopeKind kind, string? value = null)
    {
        switch (kind)
        {
            case ScopeKind.Tenant or ScopeKind.Own when value is not null:
                throw new ArgumentException($"A {kind} scope takes no value.", nameof(value));
            case ScopeKind.OrgUnit when value is not null && !Guid.TryParse(value, out _):
                throw new ArgumentException("An org-unit scope value must be an org unit id.", nameof(value));
            case ScopeKind.Assigned when string.IsNullOrWhiteSpace(value):
                throw new ArgumentException("An assigned scope needs the assignment type.", nameof(value));
            case < ScopeKind.Tenant or > ScopeKind.Assigned:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
        Value = kind is ScopeKind.OrgUnit && value is not null ? Guid.Parse(value).ToString("D") : value?.Trim();
    }

    /// <summary>The kind of data covered.</summary>
    public ScopeKind Kind { get; }

    /// <summary>The org unit id or assignment type, when the kind needs one.</summary>
    public string? Value { get; }

    /// <summary>Every record of the company.</summary>
    public static PermissionScope Tenant { get; } = new(ScopeKind.Tenant);

    /// <summary>Records the caller owns.</summary>
    public static PermissionScope Own { get; } = new(ScopeKind.Own);

    /// <summary>The caller's own org scope.</summary>
    public static PermissionScope MyOrgUnits { get; } = new(ScopeKind.OrgUnit);

    /// <summary>One org unit and its descendants.</summary>
    public static PermissionScope OrgUnit(Guid unit) => new(ScopeKind.OrgUnit, unit.ToString("D"));

    /// <summary>Records tied to objects of <paramref name="assignmentType"/> the caller is assigned to.</summary>
    public static PermissionScope Assigned(string assignmentType) => new(ScopeKind.Assigned, assignmentType);

    /// <summary>The org unit named by <see cref="Value"/>, when this is a named-unit scope.</summary>
    public Guid? OrgUnitId => Kind is ScopeKind.OrgUnit && Guid.TryParse(Value, out var id) ? id : null;

    /// <summary>The storage / wire form: <c>tenant</c>, <c>own</c>, <c>org</c>, <c>org:{id}</c>, <c>assigned:{type}</c>.</summary>
    public string Format() => Kind switch
    {
        ScopeKind.Tenant => "tenant",
        ScopeKind.Own => "own",
        ScopeKind.OrgUnit => Value is null ? "org" : "org:" + Value,
        _ => "assigned:" + Value,
    };

    /// <inheritdoc />
    public override string ToString() => Format();

    /// <summary>Parses <see cref="Format"/>; false for anything else.</summary>
    public static bool TryParse(string? text, out PermissionScope scope)
    {
        scope = Tenant;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        var head = (colon < 0 ? trimmed : trimmed[..colon]).ToLowerInvariant();
        var tail = colon < 0 ? null : trimmed[(colon + 1)..];
        try
        {
            switch (head)
            {
                case "tenant" when tail is null:
                    scope = Tenant;
                    return true;
                case "own" when tail is null:
                    scope = Own;
                    return true;
                case "org":
                    scope = new PermissionScope(ScopeKind.OrgUnit, tail);
                    return true;
                case "assigned" when tail is not null:
                    scope = new PermissionScope(ScopeKind.Assigned, tail);
                    return true;
                default:
                    return false;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
