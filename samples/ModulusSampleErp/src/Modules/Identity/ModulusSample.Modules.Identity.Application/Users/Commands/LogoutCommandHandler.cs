using ModulusSample.Modules.Identity.Application.Abstractions.Authentication;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Shared.Application.Abstractions.Oidc;
using ModulusSample.Shared.Domain;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;

namespace ModulusSample.Modules.Identity.Application.Users.Commands;

/// <summary>
/// Handles user logout with back-channel logout support.
/// Implements secure token invalidation and session cleanup.
/// </summary>
internal sealed class LogoutCommandHandler(
    ITokenBlacklistService blacklistService,
    ISessionService sessionService,
    IUserContext userContext,
    ILogger<LogoutCommandHandler> logger)
    : Modulus.Mediator.Abstractions.ICommandHandler<LogoutCommand, Result<LogoutResponse>>
{
    public async Task<Result<LogoutResponse>> HandleAsync(
        LogoutCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Processing logout for user: {UserId}", userContext.UserId);

            // 1. Extract and blacklist the current access token
            string? accessToken = userContext.AccessToken;
            if (!string.IsNullOrEmpty(accessToken))
            {
                try
                {
                    var jwtToken = new JwtSecurityToken(accessToken);
                    string tokenId = jwtToken.Id ?? accessToken;

                    await blacklistService.BlacklistTokenAsync(
                        tokenId,
                        jwtToken.ValidTo,
                        cancellationToken);

                    logger.LogInformation("Blacklisted access token: {TokenId} for user: {UserId}",
                        tokenId, userContext.UserId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to blacklist access token for user: {UserId}",
                        userContext.UserId);
                }
            }

            // 2. Revoke current session
            if (!string.IsNullOrEmpty(userContext.ExternalSessionId))
            {
                try
                {
                    Result<SessionInfo> sessionResult = await sessionService.GetSessionByExternalSessionIdAsync(
                        userContext.ExternalSessionId,
                        cancellationToken);

                    if (sessionResult.IsSuccess)
                    {
                        await sessionService.RevokeSessionAsync(
                            sessionResult.Value.Id,
                            "User logout",
                            cancellationToken);

                        await sessionService.ClearIdTokenHashAsync(sessionResult.Value.Id, cancellationToken);

                        logger.LogInformation("Revoked session: {SessionId} for user: {UserId}",
                            sessionResult.Value.Id, userContext.UserId);
                    }
                    else
                    {
                        logger.LogWarning("Could not find session by external session id for user: {UserId}",
                            userContext.UserId);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to revoke session for user: {UserId}",
                        userContext.UserId);
                }
            }

            logger.LogInformation("Logout completed successfully for user: {UserId} at {Time}",
                userContext.UserId, DateTime.UtcNow);

            return Result.Success(new LogoutResponse(
                true,
                "Logged out successfully",
                DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing logout for user: {UserId}", userContext.UserId);
            return Result.Failure<LogoutResponse>(Error.Failure("Logout.Failed", "Failed to process logout"));
        }
    }
}
