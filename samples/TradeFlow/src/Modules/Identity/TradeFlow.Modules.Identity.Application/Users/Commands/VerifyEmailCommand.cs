
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Application.Users.Commands;

/// <summary>
/// Command to verify a user's email address using a verification token.
/// </summary>
public sealed record VerifyEmailCommand(string Token) : Modulus.Mediator.Abstractions.ICommand<Result>;
