using Modulus.Mediator.Abstractions.Attributes;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Application.Users.Commands;

[RequirePermission(AppPermissions.IdentityPasswordChangeOwn)]
public sealed record ChangePasswordCommand(
    string CurrentPassword,
    string NewPassword) : Modulus.Mediator.Abstractions.ICommand<Result>;

