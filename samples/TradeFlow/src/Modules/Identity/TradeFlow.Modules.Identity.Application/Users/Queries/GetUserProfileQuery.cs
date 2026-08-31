using Modulus.Mediator.Abstractions.Attributes;
using TradeFlow.Modules.Identity.Application.Users.Dtos;
using TradeFlow.Shared.Domain;
namespace TradeFlow.Modules.Identity.Application.Users.Queries;

[RequirePermission(AppPermissions.IdentityProfileViewOwn)]
public sealed record GetUserProfileQuery(Guid? UserId = null) : Modulus.Mediator.Abstractions.IQuery<Result<UserProfileResponse>>;
