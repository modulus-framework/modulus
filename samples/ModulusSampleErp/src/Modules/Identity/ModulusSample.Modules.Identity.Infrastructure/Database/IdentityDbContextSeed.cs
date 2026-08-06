using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Infrastructure.Authorization;
using ModulusSample.Modules.Identity.Domain.Authorization;
using ModulusSample.Modules.Identity.Domain.Entities;
using ModulusSample.Modules.Identity.Domain.Enums;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ModulusSample.Modules.Identity.Infrastructure.Database;

public static class IdentityDbContextSeed
{
    private const string DevelopmentEnvironment = "Development";

    public static async Task SeedAsync(
        IdentityDbContext context,
        ILogger logger,
        string environment = DevelopmentEnvironment)
    {
        try
        {
            await SeedPermissionsAsync(context, logger);
            await SeedRolesAsync(context, logger);
            await SeedUsersAsync(context, logger, environment);
            await SeedRolePermissionsAsync(context, logger);

            logger.LogInformation("Users module seeding completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding Users module");
            throw;
        }
    }

    private static async Task SeedPermissionsAsync(IdentityDbContext context, ILogger logger)
    {
        IReadOnlySet<string> validCodes = AppPermissions.AllSet;

        List<Permission> existingPermissions = await context.Permissions.ToListAsync();
        var existingCodes = existingPermissions.Select(p => p.Code).ToHashSet();

        var stalePermissions = existingPermissions
            .Where(p => !validCodes.Contains(p.Code))
            .ToList();

        if (stalePermissions.Count > 0)
        {
            logger.LogInformation("Removing {Count} stale permissions", stalePermissions.Count);

            var staleCodes = stalePermissions.Select(p => p.Code).ToList();

            await context.Database.BeginTransactionAsync();

            foreach (string staleCode in staleCodes)
            {
                await context.Database.ExecuteSqlRawAsync(
                    "DELETE FROM \"identity\".\"role_permissions\" WHERE \"permission_id\" = {0}", staleCode);
            }

            context.Permissions.RemoveRange(stalePermissions);
            await context.SaveChangesAsync();

            await context.Database.CommitTransactionAsync();
        }

        var missingCodes = validCodes.Except(existingCodes).ToList();

        if (missingCodes.Count == 0)
        {
            logger.LogInformation("All permissions already exist, skipping permission seeding");
            return;
        }

        logger.LogInformation("Seeding {Count} missing permissions...", missingCodes.Count);

        var permissions = new List<Permission>();

        foreach (string permissionCode in missingCodes)
        {
            string category = GetCategoryFromPermissionCode(permissionCode);
            string name = GetNameFromPermissionCode(permissionCode);
            string description = GetDescriptionFromPermissionCode(permissionCode);

            Result<Permission> permissionResult = Permission.Create(
                permissionCode,
                name,
                description,
                category,
                isActive: true);

            if (permissionResult.IsSuccess)
            {
                permissions.Add(permissionResult.Value);
            }
        }

        await context.Permissions.AddRangeAsync(permissions);
        await context.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} permissions", permissions.Count);
    }

    private static async Task SeedRolesAsync(IdentityDbContext context, ILogger logger)
    {
        List<string> existingRoleNames = await context.Roles.Select(r => r.Name).ToListAsync();

        if (existingRoleNames.Count == Roles.All.Count)
        {
            logger.LogInformation("All roles already exist, skipping role seeding");
            return;
        }

        logger.LogInformation("Seeding roles...");

        var roles = new List<Role>();

        foreach (string roleName in Roles.All)
        {
            if (existingRoleNames.Contains(roleName))
            {
                continue;
            }

            Result<Role> roleResult = roleName switch
            {
                Roles.Admin => Role.Create(RoleId.Create(), Roles.Admin,
                    "Platform administrator with full system access", isSystem: true),
                Roles.User => Role.Create(RoleId.Create(), Roles.User,
                    "Standard platform user", isSystem: true),
                _ => Result.Failure<Role>(Error.NotFound("Role.Unknown", $"Unknown role: {roleName}"))
            };

            if (roleResult.IsSuccess)
            {
                roles.Add(roleResult.Value);
            }
        }

        if (roles.Count > 0)
        {
            await context.Roles.AddRangeAsync(roles);
            await context.SaveChangesAsync();
        }

        logger.LogInformation("Seeded {Count} roles", roles.Count);
    }

    private static async Task SeedRolePermissionsAsync(IdentityDbContext context, ILogger logger)
    {
        List<Role> roles = await context.Roles
            .Include(r => r.Permissions)
            .ToListAsync();

        List<Permission> permissions = await context.Permissions.ToListAsync();

        var permissionMap = permissions.ToDictionary(p => p.Code, p => p.Id);

        User? firstUser = await context.Users.FirstOrDefaultAsync();

        if (firstUser == null)
        {
            logger.LogWarning("No users found in database. Creating a system user for seeding role permissions.");

            var systemUserId = UserId.Create();
            var systemUser = User.Create(
                systemUserId,
                Email.Create("system@modulussample.local").Value,
                UserName.Create("system"),
                "System",
                "User",
                UserType.Admin,
                emailConfirmed: true);

            Role systemAdminRole = roles.First(r => r.Name == Roles.Admin);
            systemUser.AddRole(systemAdminRole.Id);

            await context.Users.AddAsync(systemUser);
            await context.SaveChangesAsync();

            firstUser = systemUser;
            logger.LogInformation("Created system user {UserId} for seeding purposes", systemUserId.Value);
        }

        UserId grantedByUserId = firstUser.Id;
        logger.LogInformation("Using user {UserId} as the grantor for role permissions", grantedByUserId.Value);

        int addedCount = 0;
        foreach (Role role in roles)
        {
            foreach (string permissionCode in RolePermissions.GetPermissionsForRole(role.Name))
            {
                if (!permissionMap.TryGetValue(permissionCode, out PermissionId permissionId))
                {
                    logger.LogWarning("Permission code '{PermissionCode}' not found in seeded permissions.",
                        permissionCode);
                    continue;
                }

                int before = role.Permissions.Count;
                role.AddPermission(permissionId, grantedByUserId);
                if (role.Permissions.Count > before)
                {
                    addedCount++;
                }
            }
        }

        await context.SaveChangesAsync();

        logger.LogInformation("Seeded {Count} new role permissions", addedCount);
    }

    private static async Task SeedUsersAsync(
        IdentityDbContext context,
        ILogger logger,
        string environment)
    {
        if (await context.Users.AnyAsync())
        {
            logger.LogInformation("Users already exist, skipping user seeding");
            return;
        }

        logger.LogInformation("Seeding users in {Environment} environment...", environment);

        Role adminRole = await context.Roles.FirstAsync(r => r.Name == Roles.Admin);
        Role userRole = await context.Roles.FirstAsync(r => r.Name == Roles.User);

        var users = new List<User>();
        var userCredentials = new List<(string Username, string Password, string Email)>();

        var userData = new[]
        {
            new
            {
                Email = "admin@modulussample.com", UserName = "admin", FirstName = "System",
                LastName = "Administrator",
                UserType = UserType.Admin, RoleId = adminRole.Id,
                RoleName = Roles.Admin,
                EmailConfirmed = true, Password = "Admin123!"
            },
            new
            {
                Email = "user1@modulussample.com", UserName = "user1", FirstName = "Jane",
                LastName = "Doe",
                UserType = UserType.User, RoleId = userRole.Id,
                RoleName = Roles.User,
                EmailConfirmed = true, Password = "User123!"
            }
        };

        foreach (var data in userData)
        {
            User? user = CreateSeedUser(
                data.Email,
                data.UserName,
                data.FirstName,
                data.LastName,
                data.UserType,
                data.RoleId,
                data.EmailConfirmed);

            if (user != null)
            {
                users.Add(user);
                userCredentials.Add((data.UserName, data.Password, data.Email));
            }
        }

        if (users.Count > 0)
        {
            await context.Users.AddRangeAsync(users);
            await context.SaveChangesAsync();

            logger.LogInformation("Seeded {Count} users in local database", users.Count);

            if (environment == DevelopmentEnvironment)
            {
                logger.LogWarning("==================== DEVELOPMENT: TEST USER CREDENTIALS ====================");
                foreach ((string username, string password, string email) in userCredentials)
                {
                    logger.LogWarning("Email: {Email} | Username: {Username} | Password: {Password}",
                        email, username, password);
                }
                logger.LogWarning("===============================================================================");
            }
        }
        else
        {
            logger.LogWarning("No users were created successfully");
        }
    }

    private static User? CreateSeedUser(
        string email,
        string userName,
        string firstName,
        string lastName,
        UserType userType,
        RoleId roleId,
        bool emailConfirmed = false)
    {
        Result<Email> emailResult = Email.Create(email);
        if (!emailResult.IsSuccess)
        {
            return null;
        }

        var user = User.Create(
            UserId.Create(),
            emailResult.Value,
            UserName.Create(userName),
            firstName,
            lastName,
            userType,
            emailConfirmed);

        user.AddRole(roleId);
        user.UpdateProfileImage($"https://api.dicebear.com/7.x/initials/svg?seed={userName}");

        return user;
    }

    private static string GetCategoryFromPermissionCode(string permissionCode)
    {
        if (string.IsNullOrWhiteSpace(permissionCode))
        {
            return "General";
        }

        string[] parts = permissionCode.Split(':');
        if (parts.Length > 0)
        {
            return parts[0].Replace("_", " ");
        }

        return "General";
    }

    private static string GetNameFromPermissionCode(string permissionCode)
    {
        if (string.IsNullOrWhiteSpace(permissionCode))
        {
            return "Unknown Permission";
        }

        string[] parts = permissionCode.Split(':');
        if (parts.Length >= 2)
        {
            string action = parts[^1];
            string resource = parts.Length >= 3 ? parts[^2] : parts[^1];
            return $"{FormatWord(action)} {FormatWord(resource)}";
        }

        return permissionCode.Replace(":", " ").Replace("_", " ");
    }

    private static string GetDescriptionFromPermissionCode(string permissionCode)
    {
        if (string.IsNullOrWhiteSpace(permissionCode))
        {
            return "Unknown permission";
        }

        string[] parts = permissionCode.Split(':');
        if (parts.Length >= 2)
        {
            string module = parts[0];
            string action = parts[^1];
            string resource = parts.Length >= 3 ? parts[^2] : "";
            string formattedModule = FormatWord(module);
            string formattedAction = action.ToLowerInvariant();

            if (!string.IsNullOrEmpty(resource))
            {
                return
                    $"Allows to {formattedAction} {resource.ToLowerInvariant()} in {formattedModule} module";
            }
            else
            {
                return $"Allows to {formattedAction} in {formattedModule} module";
            }
        }

        return $"Permission: {permissionCode}";
    }

    private static string FormatWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return string.Empty;
        }

        word = word.Replace("_", " ");
        return char.ToUpperInvariant(word[0]) + word[1..];
    }
}
