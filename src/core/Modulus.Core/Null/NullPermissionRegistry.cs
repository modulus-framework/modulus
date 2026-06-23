namespace Modulus.Core.Null;

using Modulus.Core.Abstractions;

/// <summary>
/// Auto-registered by <c>AddModulus</c> when no Identity module is present.
/// All AddPermissions() calls are safe no-ops; Exists() returns true so that
/// the dynamic authorization policy provider never denies by default.
/// </summary>
public sealed class NullPermissionRegistry : IPermissionRegistry
{
    public void Add(string permission, string description,
        string[]? requires = null) { }

    public IReadOnlyList<PermissionDefinition> GetAll()        => [];
    public IReadOnlyList<PermissionDefinition> GetByModule(string _) => [];
    public bool Exists(string permission) => true;
    public void Freeze() { }
}
