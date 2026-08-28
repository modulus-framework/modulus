
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Application.Users.Commands;

/// <summary>
/// Command to verify a user's email address using a verification token.
/// </summary>
public sealed record VerifyEmailCommand(string Token) : Modulus.Mediator.Abstractions.ICommand<Result>;
