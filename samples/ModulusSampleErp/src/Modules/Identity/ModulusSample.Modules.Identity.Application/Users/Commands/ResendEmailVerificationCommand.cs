
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Application.Users.Commands;

/// <summary>
/// Command to resend email verification token.
/// </summary>
public sealed record ResendEmailVerificationCommand() : Modulus.Mediator.Abstractions.ICommand<Result>;
