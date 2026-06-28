namespace Modulus.Identity.Abstractions;

/// <summary>
/// Abstraction for role/permission seeding during module initialization.
/// </summary>
public interface IIdentitySeeder
{
    /// <summary>Create default roles and assign permissions.</summary>
    Task SeedAsync(CancellationToken ct = default);

    /// <summary>Create a role if it does not exist.</summary>
    Task EnsureRoleAsync(
        string roleName,
        string? displayName = null,
        bool isDefault = false,
        CancellationToken ct = default);

    /// <summary>Assign a permission claim to a role.</summary>
    Task GrantPermissionToRoleAsync(
        string roleName,
        string permission,
        CancellationToken ct = default);
}

/// <summary>
/// Abstraction for user store operations beyond CRUD.
/// Used by the identity module for profile and role management.
/// </summary>
public interface IModulusUserStore
{
    Task<ModulusUser?> FindByEmailAsync(string email, CancellationToken ct);
    Task<ModulusUser?> FindByExternalLoginAsync(
        string provider, string subject, CancellationToken ct);
    Task<IReadOnlyList<string>> GetRoleNamesAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<string>> GetPermissionClaimsAsync(Guid userId, CancellationToken ct);
}
