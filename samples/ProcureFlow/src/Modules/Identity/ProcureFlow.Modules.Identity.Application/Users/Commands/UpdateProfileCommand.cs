using Modulus.Mediator.Abstractions.Attributes;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Application.Users.Commands;

[RequirePermission(AppPermissions.IdentityProfileManageOwn)]
public sealed record UpdateProfileCommand(
    Guid UserId,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? ProfileImageUrl) : Modulus.Mediator.Abstractions.ICommand<Result>;
