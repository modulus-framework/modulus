namespace Modulus.Core.Abstractions;

/// <summary>A declared permission and its (optional) prerequisites.</summary>
public sealed record PermissionDefinition(
    string Permission,
    string Description,
    string[] Requires);

/// <summary>
/// Registry of permissions declared by modules during ConfigureServices.
/// Frozen after configuration so runtime callers see an immutable set.
/// </summary>
public interface IPermissionRegistry
{
    void Add(string permission, string description,
        string[]? requires = null);
    IReadOnlyList<PermissionDefinition> GetAll();
    IReadOnlyList<PermissionDefinition> GetByModule(string moduleName);
    bool Exists(string permission);
    void Freeze();
}
