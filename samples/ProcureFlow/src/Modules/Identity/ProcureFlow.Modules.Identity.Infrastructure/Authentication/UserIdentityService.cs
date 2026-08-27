using ProcureFlow.Modules.Identity.Application;
using ProcureFlow.Modules.Identity.Application.Abstractions.Identity;
using ProcureFlow.Modules.Identity.Domain.Entities;
using ProcureFlow.Modules.Identity.Domain.Enums;
using ProcureFlow.Modules.Identity.Domain.Errors;
using ProcureFlow.Modules.Identity.Domain.Repositories;
using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Shared.Application.Caching;
using ProcureFlow.Shared.Domain;
using ProcureFlow.Shared.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ProcureFlow.Modules.Identity.Infrastructure.Authentication;

internal sealed class UserIdentityService(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IUnitOfWork unitOfWork,
    ICacheService cacheService,
    ILogger<UserIdentityService> logger) : IUserIdentityService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task<Result<User>> ProvisionUserAsync(
        string email,
        string userName,
        string firstName,
        string lastName,
        bool emailVerified,
        CancellationToken ct = default)
    {
        Result<Email> emailResult = Email.Create(email);
        if (emailResult.IsFailure)
        {
            return Result.Failure<User>(emailResult.Error);
        }

        User? existing = await userRepository.GetByEmailAsync(emailResult.Value, ct);
        if (existing is not null)
        {
            logger.LogDebug("Provision: user already exists with email {Email}", email);
            return Result.Success(existing);
        }

        Result<UserName> userNameResult = UserName.Create(userName);
        if (userNameResult.IsFailure)
        {
            return Result.Failure<User>(userNameResult.Error);
        }

        var user = User.Create(
            UserId.Create(),
            emailResult.Value,
            userNameResult.Value,
            firstName,
            lastName,
            UserType.User,
            emailVerified);

        Role? defaultRole = await roleRepository.GetByNameAsync("User", ct);
        if (defaultRole is not null)
        {
            user.AddRole(defaultRole.Id);
        }

        await userRepository.AddAsync(user, ct);
        await unitOfWork.CommitAsync(ct);
        await InvalidateUserCacheAsync(user.Id.Value);

        logger.LogInformation("Provision: created new user {UserId}", user.Id.Value);
        return Result.Success(user);
    }

    public async Task<Result<User>> ResolveUserAsync(string externalUserId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(externalUserId, out Guid userIdGuid))
        {
            return Result.Failure<User>(IdentityErrors.User.NotFound);
        }

        string cacheKey = CacheKeys.User.UserContext(userIdGuid);

        User? cached = await cacheService.GetAsync<User>(cacheKey, ct);
        if (cached is not null)
        {
            logger.LogTrace("Resolve: cache hit for user_id {UserId}", userIdGuid);
            return Result.Success(cached);
        }

        var userId = UserId.Create(userIdGuid);
        User? user = await userRepository.GetByIdAsync(userId, ct);

        if (user is null)
        {
            logger.LogWarning("Resolve: no user found for user_id {UserId}", userIdGuid);
            return Result.Failure<User>(IdentityErrors.User.NotFound);
        }

        await cacheService.SetAsync(cacheKey, user, CacheTtl, ct);
        return Result.Success(user);
    }

    private async Task InvalidateUserCacheAsync(Guid userId)
    {
        await cacheService.RemoveAsync(CacheKeys.User.UserContext(userId));
    }
}
