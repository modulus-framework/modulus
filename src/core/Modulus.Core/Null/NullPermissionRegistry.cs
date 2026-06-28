namespace Modulus.Core.Null;

using Modulus.Core.Abstractions;

/// <summary>
/// Auto-registered by <c>AddModulus</c> when no Identity module is present.
/// All AddPermissions() calls are safe no-ops. Exists() returns false so the
/// dynamic authorization policy provider treats every requested permission as
/// undeclared (fail-closed). Do not widen this to <c>true</c> — an absent
/// registry must never grant access.
/// </summary>
public sealed class NullPermissionRegistry : IPermissionRegistry
{
    public void Add(string permission, string description,
        string[]? requires = null)
    { }

    public IReadOnlyList<PermissionDefinition> GetAll() => [];
    public IReadOnlyList<PermissionDefinition> GetByModule(string _) => [];
    public bool Exists(string permission) => false;
    public void Freeze() { }
}
