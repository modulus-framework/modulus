using ProcureFlow.Modules.Identity.Application.Abstractions.Authentication;
using ProcureFlow.Modules.Identity.Application.Sessions.Commands;
using ProcureFlow.Modules.Identity.Domain.Errors;
using ProcureFlow.Shared.Application.Abstractions.Oidc;
using ProcureFlow.Shared.Domain;
using Microsoft.Extensions.Logging;

namespace ProcureFlow.Modules.Identity.Application.Sessions.Commands;

internal sealed class RevokeOtherSessionsCommandHandler(
    ISessionService sessionService,
    IUserContext userContext,
    ILogger<RevokeOtherSessionsCommandHandler> logger)
    : Modulus.Mediator.Abstractions.ICommandHandler<RevokeOtherSessionsCommand, Result<RevokeOtherSessionsResponse>>
{
    public async Task<Result<RevokeOtherSessionsResponse>> HandleAsync(
        RevokeOtherSessionsCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(userContext.ExternalSessionId))
        {
            return Result.Failure<RevokeOtherSessionsResponse>(SessionErrors.InvalidSessionState);
        }

        Result<SessionInfo> currentSessionResult = await sessionService.GetSessionByExternalSessionIdAsync(
            userContext.ExternalSessionId,
            cancellationToken);

        if (currentSessionResult.IsFailure)
        {
            return Result.Failure<RevokeOtherSessionsResponse>(currentSessionResult.Error);
        }

        if (currentSessionResult.Value.UserId != userContext.UserId)
        {
            logger.LogWarning(
                "User {UserId} attempted to revoke sessions using session {SessionId} belonging to another user",
                userContext.UserId, currentSessionResult.Value.Id);
            return Result.Failure<RevokeOtherSessionsResponse>(SessionErrors.NotFound);
        }

        Result<int> result = await sessionService.RevokeOtherSessionsAsync(
            currentSessionResult.Value.Id,
            "User initiated sign out all other devices",
            cancellationToken);

        if (result.IsFailure)
        {
            return Result.Failure<RevokeOtherSessionsResponse>(result.Error);
        }

        logger.LogInformation(
            "Revoked {Count} other sessions for user {UserId}",
            result.Value, userContext.UserId);

        return Result.Success(new RevokeOtherSessionsResponse(
            result.Value,
            $"Successfully revoked {result.Value} other session(s)"));
    }
}
