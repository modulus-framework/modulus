using Modulus.Mediator.Abstractions.Attributes;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Application.Users.Commands;

[RequirePermission(AppPermissions.IdentityPasswordChangeOwn)]
public sealed record ChangePasswordCommand(
    string CurrentPassword,
    string NewPassword) : Modulus.Mediator.Abstractions.ICommand<Result>;

