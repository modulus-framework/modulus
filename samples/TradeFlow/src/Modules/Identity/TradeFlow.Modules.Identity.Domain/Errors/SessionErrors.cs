using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Domain.Errors;

public static class SessionErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "Session.NotFound",
        "Session not found");

    public static readonly Error Revoked = Error.Unauthorized(
        "Session.Revoked",
        "Session has been revoked");

    public static readonly Error CannotRevokeCurrent = Error.Validation(
        "Session.CannotRevokeCurrent",
        "Cannot revoke current session. Use logout instead.");

    public static readonly Error LimitExceeded = Error.Validation(
        "Session.LimitExceeded",
        "Maximum session limit exceeded");

    public static readonly Error InvalidSessionState = Error.Validation(
        "Session.InvalidSessionState",
        "Invalid session state identifier");
}
