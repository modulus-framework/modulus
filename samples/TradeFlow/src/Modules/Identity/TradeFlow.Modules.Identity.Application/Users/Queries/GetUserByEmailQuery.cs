using System.Data.Common;
using Dapper;
using TradeFlow.Modules.Identity.Application.Users.Dtos;
using TradeFlow.Modules.Identity.Domain.Errors;
using Modulus.Mediator.Abstractions.Attributes;
using TradeFlow.Shared.Application.Data;
using TradeFlow.Shared.Domain;
using TradeFlow.Shared.Domain.ValueObjects;

namespace TradeFlow.Modules.Identity.Application.Users.Queries;

[RequirePermission(AppPermissions.IdentityUserViewAll)]
public sealed record GetUserByEmailQuery(string Email) : Modulus.Mediator.Abstractions.IQuery<Result<UserProfileResponse>>;

internal sealed class GetUserByEmailQueryHandler(IDbConnectionFactory dbConnectionFactory)
    : Modulus.Mediator.Abstractions.IQueryHandler<GetUserByEmailQuery, Result<UserProfileResponse>>
{
    public async Task<Result<UserProfileResponse>> HandleAsync(
        GetUserByEmailQuery request,
        CancellationToken cancellationToken)
    {
        Result<Email> emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
        {
            return Result.Failure<UserProfileResponse>(emailResult.Error);
        }

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
            WHERE email = @Email AND is_deleted = false";

        UserProfileResponse? response = await connection.QueryFirstOrDefaultAsync<UserProfileResponse>(
            sql,
            new { Email = emailResult.Value.Value });

        if (response is null)
        {
            return Result.Failure<UserProfileResponse>(IdentityErrors.User.NotFound);
        }

        return Result.Success(response);
    }
}
