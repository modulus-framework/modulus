using Modulus.Mediator.Abstractions.Attributes;
using ModulusSample.Modules.Identity.Application.Users.Dtos;
using ModulusSample.Shared.Domain;
namespace ModulusSample.Modules.Identity.Application.Users.Queries;

[RequirePermission(AppPermissions.IdentityProfileViewOwn)]
public sealed record GetUserProfileQuery(Guid? UserId = null) : Modulus.Mediator.Abstractions.IQuery<Result<UserProfileResponse>>;
