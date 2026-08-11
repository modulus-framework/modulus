using System.Data.Common;
using Dapper;
using ModulusSample.Modules.Identity.Application.Abstractions.Authentication;
using ModulusSample.Modules.Identity.Application.Users.Dtos;
using ModulusSample.Modules.Identity.Domain.Errors;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Application.Caching;
using ModulusSample.Shared.Application.Data;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Domain.ValueObjects;

namespace ModulusSample.Modules.Identity.Application.Users.Queries;

internal sealed class GetUserProfileQueryHandler(
    IDbConnectionFactory dbConnectionFactory,
    IUserContext userContext,
    ICacheService cacheService)
    : Modulus.Mediator.Abstractions.IQueryHandler<GetUserProfileQuery, Result<UserProfileResponse>>
{
    public async Task<Result<UserProfileResponse>> HandleAsync(
        GetUserProfileQuery request,
        CancellationToken cancellationToken)
    {
        UserId userId = userContext.UserId ?? throw new InvalidOperationException("User not authenticated");

        if (userId is null)
        {
            return Result.Failure<UserProfileResponse>(IdentityErrors.User.NotFound);
        }

        UserProfileResponse response = await cacheService.GetOrCreateAsync(
            CacheKeys.User.UserProfile(userId.Value),
            async () =>
            {
                await using DbConnection connection = await dbConnectionFactory.OpenConnectionAsync();

                const string sql = @"
                    SELECT
                        id AS UserId,
                        email AS Email,
                        user_name AS UserName,
                        first_name AS FirstName,
                        last_name AS LastName,
                        phone_number AS PhoneNumber,
                        profile_image_url AS ProfileImageUrl,
                        user_type AS UserType,
                        status AS Status,
                        email_confirmed AS EmailConfirmed,
                        phone_number_confirmed AS PhoneNumberConfirmed,
                        created_at_utc AS CreatedAtUtc,
                        last_login_at_utc AS LastLoginAtUtc
                    FROM identity.users
                    WHERE id = @UserId AND is_deleted = false";

                return (await connection.QueryFirstOrDefaultAsync<UserProfileResponse>(
                    sql,
                    new { UserId = userId.Value }))!;
            },
            TimeSpan.FromMinutes(30),
            cancellationToken);

        if (response == null)
        {
            return Result.Failure<UserProfileResponse>(IdentityErrors.User.NotFound);
        }

        return Result.Success(response);
    }
}
