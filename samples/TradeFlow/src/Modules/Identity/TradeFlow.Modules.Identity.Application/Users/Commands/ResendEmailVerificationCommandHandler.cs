using TradeFlow.Modules.Identity.Application.Abstractions.Authentication;
using TradeFlow.Modules.Identity.Application.Abstractions.Identity;
using TradeFlow.Modules.Identity.Domain.Entities;
using TradeFlow.Modules.Identity.Domain.Repositories;
using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Shared.Domain;
using TradeFlow.Shared.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace TradeFlow.Modules.Identity.Application.Users.Commands;

/// <summary>
/// Handles resending email verification tokens with rate limiting.
/// </summary>
internal sealed class ResendEmailVerificationCommandHandler(
    IUserContext userContext,
    IUserRepository userRepository,
    IEmailVerificationTokenService tokenService,
    IEmailVerificationTokenRepository tokenRepository,
    IRateLimitingService rateLimitingService,
    ILogger<ResendEmailVerificationCommandHandler> logger)
    : Modulus.Mediator.Abstractions.ICommandHandler<ResendEmailVerificationCommand, Result>
{
    public async Task<Result> HandleAsync(
        ResendEmailVerificationCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get current user ID from context
            Guid userId = userContext.UserId;

            // Check rate limit (15 minutes)
            Result<bool> isAllowedResult = await rateLimitingService.IsAllowedAsync(
                userId,
                "email_verification_resend",
                TimeSpan.FromMinutes(15),
                cancellationToken);

            if (isAllowedResult.IsFailure)
            {
                logger.LogInformation("Email verification resend rate limit exceeded for user: {UserId}", userId);
                return Result.Failure(isAllowedResult.Error);
            }

            // Get the user
            User? user = await userRepository.GetByIdAsync(new UserId(userId), cancellationToken);
            if (user is null)
            {
                return Result.Failure(
                    Error.NotFound("ResendVerification.UserNotFound", "User not found"));
            }

            // Check if email is already verified
            if (user.EmailConfirmed)
            {
                return Result.Failure(
                    Error.Validation("ResendVerification.AlreadyVerified", "Email is already verified"));
            }

            // Invalidate all existing unused tokens for user
            await tokenRepository.InvalidateAllUserTokensAsync(userId, cancellationToken);
            logger.LogInformation("Invalidated existing tokens for user: {UserId}", userId);

            // Generate new verification token
            Result<string> tokenResult = await tokenService.GenerateTokenAsync(userId, cancellationToken);
            if (tokenResult.IsFailure)
            {
                return Result.Failure(tokenResult.Error);
            }

            string rawToken = tokenResult.Value;

            // Record the rate limit action
            await rateLimitingService.RecordActionAsync(
                userId,
                "email_verification_resend",
                TimeSpan.FromMinutes(15),
                cancellationToken);

            logger.LogInformation("Email verification token resent for user: {UserId}", userId);

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error resending email verification for user: {UserId}", userContext.UserId);
            return Result.Failure(
                Error.Failure("ResendVerification.Failed", "Failed to resend verification email"));
        }
    }
}
