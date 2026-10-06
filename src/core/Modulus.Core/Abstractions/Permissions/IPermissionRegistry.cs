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

/// <summary>A declared permission and its (optional) prerequisites.</summary>
public sealed record PermissionDefinition(
    string Permission,
    string Description,
    string[] Requires)
{
    /// <summary>The permission's sensitivity level.</summary>
    public PermissionSensitivity Sensitivity { get; init; }
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

    IReadOnlyList<PermissionDefinition> GetAll();
    IReadOnlyList<PermissionDefinition> GetByModule(string moduleName);
    bool Exists(string permission);
    void Freeze();
}
