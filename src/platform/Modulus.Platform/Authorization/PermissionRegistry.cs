namespace Modulus.Authorization;

using System.Collections.Concurrent;
using Modulus.Core.Abstractions;

public sealed class PermissionRegistry : IPermissionRegistry
{
    private readonly ConcurrentDictionary<string, PermissionDefinition>
        _permissions = new(StringComparer.OrdinalIgnoreCase);
    private bool _frozen;

    public void Add(
        string permission,
        string description,
        string[]? requires = null)
    {
        if (_frozen)
            throw new InvalidOperationException(
                "PermissionRegistry is frozen. Declarations must happen in ConfigureServices.");

        _permissions[permission] = new PermissionDefinition(
            permission, description, requires ?? []);
    }

    public IReadOnlyList<PermissionDefinition> GetAll()
        => [.. _permissions.Values];

    public IReadOnlyList<PermissionDefinition> GetByModule(string module)
        => [.. _permissions.Values
            .Where(p => p.Permission.StartsWith(module + ":",
                StringComparison.OrdinalIgnoreCase))];

    public bool Exists(string permission)
        => _permissions.ContainsKey(permission);

    public void Freeze() => _frozen = true;
}