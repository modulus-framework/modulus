using Modulus.Mediator.Abstractions.Attributes;
using ProcureFlow.Modules.Identity.Application.Users.Dtos;
using ProcureFlow.Shared.Domain;
namespace ProcureFlow.Modules.Identity.Application.Users.Queries;

[RequirePermission(AppPermissions.IdentityProfileViewOwn)]
public sealed record GetUserProfileQuery(Guid? UserId = null) : Modulus.Mediator.Abstractions.IQuery<Result<UserProfileResponse>>;
