using ProcureFlow.Modules.Identity.Application.Abstractions.Authentication;
using ProcureFlow.Modules.Identity.Application.Sessions.Dtos;
using ProcureFlow.Modules.Identity.Domain.Errors;
using ProcureFlow.Shared.Application.Abstractions.Oidc;
using ProcureFlow.Shared.Domain;
using Microsoft.Extensions.Logging;

namespace ProcureFlow.Modules.Identity.Application.Sessions.Commands;

internal sealed class RevokeSessionCommandHandler(
    ISessionService sessionService,
    IUserContext userContext,
    ILogger<RevokeSessionCommandHandler> logger)
    : Modulus.Mediator.Abstractions.ICommandHandler<RevokeSessionCommand, Result>
{
    public async Task<Result> HandleAsync(
        RevokeSessionCommand request,
        CancellationToken cancellationToken)
    {
        Guid? currentDbSessionId = null;
        if (!string.IsNullOrEmpty(userContext.ExternalSessionId))
        {
            Result<SessionInfo> currentResult = await sessionService.GetSessionByExternalSessionIdAsync(
                userContext.ExternalSessionId, cancellationToken);
            if (currentResult.IsSuccess)
            {
                currentDbSessionId = currentResult.Value.Id;
            }
        }

        if (currentDbSessionId.HasValue && currentDbSessionId.Value == request.SessionId)
        {
            return Result.Failure(SessionErrors.CannotRevokeCurrent);
        }

        Result<SessionInfo> sessionResult = await sessionService.GetSessionAsync(request.SessionId, cancellationToken);
        if (sessionResult.IsFailure)
        {
            return Result.Failure(sessionResult.Error);
        }

        Guid userId = userContext.UserId;
        if (sessionResult.Value.UserId != userId)
        {
            logger.LogWarning(
                "User {UserId} attempted to revoke session {SessionId} belonging to another user {OwnerUserId}",
                userId, request.SessionId, sessionResult.Value.UserId);
            return Result.Failure(SessionErrors.NotFound);
        }

        if (sessionResult.Value.IsRevoked)
        {
            return Result.Success();
        }

        Result result = await sessionService.RevokeSessionAsync(
            request.SessionId,
            "User initiated revocation",
            cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning(
                "Failed to revoke session {SessionId} for user {UserId}: {Error}",
                request.SessionId, userContext.UserId, result.Error.Message);
        }

        return result;
    }
}
