using ModulusSample.Modules.Identity.Domain.Repositories;
using ModulusSample.Shared.Application.Abstractions;
using ModulusSample.Shared.Application.Authorization;
using ModulusSample.Shared.Domain;
using Microsoft.Extensions.Logging;
using UserId = ModulusSample.Modules.Identity.Domain.ValueObjects.UserId;
using ModulusSample.Shared.Application.Caching;

namespace ModulusSample.Modules.Identity.Infrastructure.Authentication;

/// <summary>
/// Implementation of IPermissionService that retrieves user permissions from the database.
/// Optimized for .NET 8 and EF Core performance with projection-based queries.
/// </summary>
internal sealed class PermissionService(
    IUserRepository userRepository,
    IUserIdentifierMapper userIdentifierMapper,
    ICacheService cacheService,
    ILogger<PermissionService> logger) : IPermissionService
{
    /// <inheritdoc />
    public async Task<Result<PermissionsResponse>> GetUserPermissionsAsync(
        string identityId,
        CancellationToken ct = default)
    {
        try
        {
            // Parse the Users ID as a GUID
            if (!Guid.TryParse(identityId, out Guid userIdGuid))
            {
                logger.LogWarning("Invalid user ID format: {IdentityId}", identityId);
                return Result.Failure<PermissionsResponse>(
                    Error.Validation("Permission.InvalidUserId", "Invalid user ID format"));
            }

            PermissionsResponse? response = await cacheService.GetOrCreateAsync(
                CacheKeys.User.UserPermissions(userIdGuid),
                async () =>
                {
                    var userId = UserId.Create(userIdGuid);

                    // Try to get permissions directly first (most efficient path)
                    // Uses optimized projection query - only fetches permission codes
                    IReadOnlyCollection<string> permissions = await userRepository.GetUserPermissionCodesAsync(userId, ct);

                    if (permissions.Count > 0)
                    {
                        logger.LogDebug(
                            "Retrieved {PermissionCount} permissions for user {UserId}",
                            permissions.Count, userIdGuid);

                        return new PermissionsResponse(userIdGuid, permissions.ToHashSet());
                    }

                    // Check if user exists but has no permissions
                    bool userExists = await userRepository.ExistsByIdAsync(userId, ct);

                    if (userExists)
                    {
                        logger.LogDebug("User {UserId} exists but has no permissions assigned", userIdGuid);
                        return new PermissionsResponse(userIdGuid, new HashSet<string>());
                    }

                    // User doesn't exist, try identifier mapper for external provider users
                    logger.LogDebug(
                        "User {UserId} not found, attempting identifier mapping for external providers",
                        userIdGuid);

                    Result<Guid> mappedUserIdResult = await userIdentifierMapper
                        .GetApplicationUserIdFromExternalIdAsync(userIdGuid.ToString(), "Keycloak", ct);

                    if (mappedUserIdResult.IsFailure)
                    {
                        logger.LogWarning(
                            "User not found and identifier mapping failed: {UserId} - {Error}",
                            userIdGuid,
                            mappedUserIdResult.Error?.Message);

                        // Return empty permissions for non-existent users instead of failing.
                        // This allows the provision endpoint to create new users without auth failures.
                        // The user will get proper permissions after provisioning completes.
                        return new PermissionsResponse(userIdGuid, new HashSet<string>());
                    }

                    logger.LogDebug(
                        "Successfully mapped external user {ExternalId} to internal user {InternalId}",
                        userIdGuid,
                        mappedUserIdResult.Value);

                    // Get permissions for the mapped user
                    var mappedUserId = UserId.Create(mappedUserIdResult.Value);
                    permissions = await userRepository.GetUserPermissionCodesAsync(mappedUserId, ct);

                    logger.LogDebug(
                        "Retrieved {PermissionCount} permissions for mapped user {UserId}",
                        permissions.Count, mappedUserIdResult.Value);

                    return new PermissionsResponse(userIdGuid, permissions.ToHashSet());
                },
                TimeSpan.FromMinutes(30),
                ct);

            if (response == null)
            {
                return Result.Failure<PermissionsResponse>(
                    Error.NotFound("Permission.UserNotFound", "User not found in the system"));
            }

            return Result.Success(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving permissions for user: {IdentityId}", identityId);
            return Result.Failure<PermissionsResponse>(
                Error.Failure("Permission.RetrievalFailed", "Failed to retrieve user permissions"));
        }
    }
}
