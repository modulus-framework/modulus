
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Application.Users.Commands;

/// <summary>
/// Command to logout a user with back-channel logout support.
/// Clears tokens, invalidates sessions, and performs Keycloak back-channel logout.
/// </summary>
public sealed record LogoutCommand(string? IdTokenHint) : Modulus.Mediator.Abstractions.ICommand<Result<LogoutResponse>>;

/// <summary>
/// Response from logout operation.
/// </summary>
public sealed record LogoutResponse(
    bool Success,
    string Message,
    DateTime LoggedOutAtUtc);
