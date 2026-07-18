namespace Modulus.Authorization.Grants;

using System.Collections.Concurrent;

/// <summary>
/// The default <see cref="IPermissionGrantStore"/>: holds role and user grants in
/// memory. Seed it at startup via
/// <see cref="Extensions.AuthorizationExtensions.AddPermissionGrants"/>; grants
/// may also be added or revoked at runtime (they are dynamic, unlike the frozen
/// permission catalog). Empty by default, so an application that never seeds a
/// grant is fail-closed — every principal resolves to no permissions.
/// </summary>
public sealed class InMemoryPermissionGrantStore : IPermissionGrantStore
{
    // holder key ("role:name" / "user:guid") → permission (OrdinalIgnoreCase) → grant.
    // A ConcurrentDictionary per holder gives lock-free reads during runtime mutation.
    private readonly ConcurrentDictionary<string,
        ConcurrentDictionary<string, PermissionGrant>> _grants =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Grants one or more permissions to a role.</summary>
    public InMemoryPermissionGrantStore GrantToRole(string role, params string[] permissions)
        => Set(GrantHolderType.Role, role, PermissionGrantType.Allow, permissions);

    /// <summary>Explicitly denies one or more permissions to a role (overrides any allow).</summary>
    public InMemoryPermissionGrantStore DenyToRole(string role, params string[] permissions)
        => Set(GrantHolderType.Role, role, PermissionGrantType.Deny, permissions);

    /// <summary>Grants one or more permissions directly to a user.</summary>
    public InMemoryPermissionGrantStore GrantToUser(Guid userId, params string[] permissions)
        => Set(GrantHolderType.User, userId.ToString(), PermissionGrantType.Allow, permissions);

    /// <summary>Explicitly denies one or more permissions directly to a user.</summary>
    public InMemoryPermissionGrantStore DenyToUser(Guid userId, params string[] permissions)
        => Set(GrantHolderType.User, userId.ToString(), PermissionGrantType.Deny, permissions);

    /// <summary>Removes a role grant/denial (no-op if it was never set).</summary>
    public InMemoryPermissionGrantStore RevokeFromRole(string role, string permission)
        => Remove(GrantHolderType.Role, role, permission);

    /// <summary>Removes a direct user grant/denial (no-op if it was never set).</summary>
    public InMemoryPermissionGrantStore RevokeFromUser(Guid userId, string permission)
        => Remove(GrantHolderType.User, userId.ToString(), permission);

    public IReadOnlyCollection<PermissionGrant> GetGrants(PrincipalGrantQuery principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var result = new List<PermissionGrant>();

        foreach (var role in principal.Roles)
            Collect(Key(GrantHolderType.Role, role), result);

        if (principal.UserId is { } userId)
            Collect(Key(GrantHolderType.User, userId.ToString()), result);

        return result;
    }

    private void Collect(string holderKey, List<PermissionGrant> into)
    {
        if (_grants.TryGetValue(holderKey, out var byPermission))
            into.AddRange(byPermission.Values);
    }

    private InMemoryPermissionGrantStore Set(
        GrantHolderType holderType, string holder,
        PermissionGrantType type, string[] permissions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(holder);
        ArgumentNullException.ThrowIfNull(permissions);

        var bucket = _grants.GetOrAdd(
            Key(holderType, holder),
            _ => new ConcurrentDictionary<string, PermissionGrant>(
                StringComparer.OrdinalIgnoreCase));

        foreach (var permission in permissions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(permission);
            bucket[permission] = new PermissionGrant(holderType, holder, permission, type);
        }

        return this;
    }

    private InMemoryPermissionGrantStore Remove(
        GrantHolderType holderType, string holder, string permission)
    {
        if (_grants.TryGetValue(Key(holderType, holder), out var bucket))
            bucket.TryRemove(permission, out _);
        return this;
    }

    private static string Key(GrantHolderType holderType, string holder)
        => holderType is GrantHolderType.Role ? "role:" + holder : "user:" + holder;
}
