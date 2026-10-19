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
        => Add(permission, description, requires, PermissionSensitivity.Normal);

    public void Add(
        string permission,
        string description,
        string[]? requires,
        PermissionSensitivity sensitivity)
        => Add(permission, description, requires, sensitivity, PermissionLifecycle.Active, replacedBy: null);

    public void Add(
        string permission,
        string description,
        string[]? requires,
        PermissionSensitivity sensitivity,
        PermissionLifecycle lifecycle,
        string? replacedBy)
    {
        if (_frozen)
            throw new InvalidOperationException(
                "PermissionRegistry is frozen. Declarations must happen in ConfigureServices.");
        if (lifecycle is PermissionLifecycle.Active && replacedBy is not null)
            throw new ArgumentException("Only a deprecated or retired permission can name its replacement.", nameof(replacedBy));

        var definition = new PermissionDefinition(permission, description, requires ?? [])
        {
            Sensitivity = sensitivity,
            Lifecycle = lifecycle,
            ReplacedBy = replacedBy,
        };

        // Redeclaring a permission is allowed only when it means the same thing. A changed sensitivity, lifecycle, replacement or
        // prerequisite would otherwise be decided by whichever module registers last.
        if (_permissions.TryGetValue(permission, out var existing) && !SameMeaning(existing, definition))
            throw new InvalidOperationException(
                $"Permission '{permission}' is declared twice with different sensitivity, lifecycle, replacement or prerequisites.");

        _permissions[permission] = definition;
    }

    private static bool SameMeaning(PermissionDefinition a, PermissionDefinition b)
        => a.Sensitivity == b.Sensitivity
           && a.Lifecycle == b.Lifecycle
           && string.Equals(a.ReplacedBy, b.ReplacedBy, StringComparison.OrdinalIgnoreCase)
           && new HashSet<string>(a.Requires, StringComparer.OrdinalIgnoreCase).SetEquals(b.Requires);

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
