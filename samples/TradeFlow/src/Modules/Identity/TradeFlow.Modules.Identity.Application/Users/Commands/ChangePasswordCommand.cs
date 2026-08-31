using Modulus.Mediator.Abstractions.Attributes;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Application.Users.Commands;

[RequirePermission(AppPermissions.IdentityPasswordChangeOwn)]
public sealed record ChangePasswordCommand(
    string CurrentPassword,
    string NewPassword) : Modulus.Mediator.Abstractions.ICommand<Result>;

