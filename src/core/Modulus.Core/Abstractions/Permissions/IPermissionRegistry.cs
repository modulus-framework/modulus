namespace Modulus.Core.Abstractions;

/// <summary>
/// How dangerous a permission is. <see cref="Critical"/> permissions are never reached
/// through a wildcard (<c>module:*</c>) and are never delegable: they must be named.
/// </summary>
public enum PermissionSensitivity
{
    /// <summary>Ordinary permission.</summary>
    Normal = 0,

    /// <summary>Sensitive; shown prominently in review UIs and audits.</summary>
    Sensitive = 1,

    /// <summary>Administrative authority; excluded from wildcards, never delegable.</summary>
    Critical = 2,
}

/// <summary>
/// Where a permission is in its life. <see cref="Active"/> is the normal state. <see cref="Deprecated"/> keeps the
/// permission working for the grants that already hold it, but refuses new grants (move to <see cref="PermissionDefinition.ReplacedBy"/>).
/// <see cref="Retired"/> denies every holder, whatever the grant says.
/// </summary>
public enum PermissionLifecycle
{
    /// <summary>Assignable and effective.</summary>
    Active = 0,

    /// <summary>Effective for existing grants; no new grant may name it.</summary>
    Deprecated = 1,

    /// <summary>Denied to everyone. The declaration stays, so the denial is explicit rather than "unknown".</summary>
    Retired = 2,
}

/// <summary>A declared permission and its (optional) prerequisites.</summary>
public sealed record PermissionDefinition(
    string Permission,
    string Description,
    string[] Requires)
{
    /// <summary>The permission's sensitivity level.</summary>
    public PermissionSensitivity Sensitivity { get; init; }

    /// <summary>The lifecycle state (<see cref="PermissionLifecycle.Active"/> unless declared otherwise).</summary>
    public PermissionLifecycle Lifecycle { get; init; }

    /// <summary>
    /// The permission that replaces this one. It is never granted automatically: an administrator grants the replacement explicitly.
    /// </summary>
    public string? ReplacedBy { get; init; }
}

/// <summary>
/// Registry of permissions declared by modules during ConfigureServices.
/// Frozen after configuration so runtime callers see an immutable set.
/// </summary>
public interface IPermissionRegistry
{
    void Add(string permission, string description,
        string[]? requires = null);

    /// <summary>
    /// Declares a permission with a <paramref name="sensitivity"/>. Registries that do not
    /// track sensitivity fall back to a plain (<see cref="PermissionSensitivity.Normal"/>) declaration.
    /// </summary>
    void Add(string permission, string description, string[]? requires, PermissionSensitivity sensitivity)
        => Add(permission, description, requires);

    /// <summary>
    /// Declares a permission with a sensitivity and a lifecycle state. Declaring the same permission again with a different
    /// sensitivity, lifecycle or replacement is an error: the catalog must not change meaning silently. A registry that does
    /// not support lifecycle refuses the call rather than ignore a retirement.
    /// </summary>
    void Add(string permission, string description, string[]? requires, PermissionSensitivity sensitivity,
        PermissionLifecycle lifecycle, string? replacedBy)
        => throw new NotSupportedException("This permission registry does not support permission lifecycle.");

    IReadOnlyList<PermissionDefinition> GetAll();
    IReadOnlyList<PermissionDefinition> GetByModule(string moduleName);
    bool Exists(string permission);
    void Freeze();
}
