namespace Modulus.Identity;

using Microsoft.AspNetCore.Identity;
using Modulus.Identity.Abstractions;

/// <summary>
/// Default identity seeder that creates roles and permission claims.
/// Called by IdentityModule.InitializeAsync.
/// </summary>
internal sealed class DefaultIdentitySeeder<TUser, TRole>(
    RoleManager<TRole> roleManager,
    IEnumerable<ModulusRoleSeed> roleSeeds)
    : IIdentitySeeder
    where TUser : ModulusUser
    where TRole : ModulusRole, new()
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        foreach (var seed in roleSeeds)
        {
            await EnsureRoleAsync(
                seed.Name, seed.DisplayName, seed.IsDefault, ct);

            foreach (var perm in seed.Permissions)
                await GrantPermissionToRoleAsync(seed.Name, perm, ct);
        }
    }

    public async Task EnsureRoleAsync(
        string roleName,
        string? displayName = null,
        bool isDefault = false,
        CancellationToken ct = default)
    {
        if (await roleManager.RoleExistsAsync(roleName))
            return;

        var role = new TRole
        {
            Name = roleName,
            DisplayName = displayName ?? roleName,
            IsDefault = isDefault,
            NormalizedName = roleName.ToUpperInvariant(),
        };

        await roleManager.CreateAsync(role);
    }

    public async Task GrantPermissionToRoleAsync(
        string roleName,
        string permission,
        CancellationToken ct = default)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role is null) return;

        var existing = await roleManager.GetClaimsAsync(role);
        if (existing.Any(c => c.Type == "permission" && c.Value == permission))
            return;

        await roleManager.AddClaimAsync(role,
            new System.Security.Claims.Claim("permission", permission));
    }
}

/// <summary>
/// Declarative role seed used during module initialization.
/// </summary>
public sealed record ModulusRoleSeed(
    string Name,
    string? DisplayName,
    bool IsDefault,
    string[] Permissions);
