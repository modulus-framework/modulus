using System.Data.Common;
using Dapper;
using ProcureFlow.Modules.Identity.Application.Abstractions.Authentication;
using ProcureFlow.Modules.Identity.Application.Users.Dtos;
using ProcureFlow.Modules.Identity.Domain.Errors;
using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Shared.Application.Caching;
using ProcureFlow.Shared.Application.Data;
using ProcureFlow.Shared.Domain;
using ProcureFlow.Shared.Domain.ValueObjects;

namespace ProcureFlow.Modules.Identity.Application.Users.Queries;

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
        Guid userId = userContext.UserId;

        if (userId == Guid.Empty)
        {
            return Result.Failure<UserProfileResponse>(IdentityErrors.User.NotFound);
        }

        UserProfileResponse response = await cacheService.GetOrCreateAsync(
            CacheKeys.User.UserProfile(userId),
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
                    new { UserId = userId }))!;
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
