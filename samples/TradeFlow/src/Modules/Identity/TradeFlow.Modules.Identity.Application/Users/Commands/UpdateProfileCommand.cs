using Modulus.Mediator.Abstractions.Attributes;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Application.Users.Commands;

[RequirePermission(AppPermissions.IdentityProfileManageOwn)]
public sealed record UpdateProfileCommand(
    Guid UserId,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? ProfileImageUrl) : Modulus.Mediator.Abstractions.ICommand<Result>;
