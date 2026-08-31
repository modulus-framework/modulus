using Modulus.EntityFrameworkCore.Abstractions;
using TradeFlow.Modules.Identity.Application.Abstractions.Identity;
using TradeFlow.Modules.Identity.Domain.Entities;
using TradeFlow.Modules.Identity.Domain.Repositories;
using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Shared.Domain;
using TradeFlow.Shared.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace TradeFlow.Modules.Identity.Application.Users.Commands;

/// <summary>
/// Handles email verification completion using verification tokens.
/// </summary>
internal sealed class VerifyEmailCommandHandler(
    IUserRepository userRepository,
    IEmailVerificationTokenService tokenService,
    IEmailVerificationTokenRepository tokenRepository,
    IUnitOfWork unitOfWork,
    ILogger<VerifyEmailCommandHandler> logger)
    : Modulus.Mediator.Abstractions.ICommandHandler<VerifyEmailCommand, Result>
{
    public async Task<Result> HandleAsync(
        VerifyEmailCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Verify token using the token service
            Result verifyResult = await tokenService.VerifyTokenAsync(request.Token, cancellationToken);

            if (verifyResult.IsFailure)
            {
                logger.LogWarning("Email verification failed: {Error}", verifyResult.Error.Message);
                // Return generic error for security
                return Result.Failure(
                    Error.Validation("VerifyEmail.InvalidToken", "Invalid or expired verification token"));
            }

            // Get the token hash to find the token record
            string tokenHash = ComputeSha256Hash(request.Token);

            // Get the token from database
            EmailVerificationToken? token = await tokenRepository.GetByTokenHashAsync(tokenHash, cancellationToken);
            if (token is null)
            {
                return Result.Failure(
                    Error.Validation("VerifyEmail.TokenNotFound", "Verification token not found"));
            }

            // Get the user
            User? user = await userRepository.GetByIdAsync(
                new UserId(token.UserId), cancellationToken);
            if (user is null)
            {
                return Result.Failure(
                    Error.NotFound("VerifyEmail.UserNotFound", "User not found"));
            }

            // Check if email is already verified
            if (user.EmailConfirmed)
            {
                return Result.Failure(
                    Error.Validation("VerifyEmail.AlreadyVerified", "Email is already verified"));
            }

            // Verify the email
            user.VerifyEmail();

            // Mark token as used
            token.MarkAsUsed();
            tokenRepository.Update(token);

            // Update the user
            await userRepository.UpdateAsync(user, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            // UserEmailVerified.v1 flows via the outbox: user.VerifyEmail() raised
            // UserEmailVerifiedEvent (an IIntegrationEvent) before the commit.
            logger.LogInformation("Email verified successfully for user: {UserId}", user.Id.Value);

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error verifying email");
            return Result.Failure(
                Error.Failure("VerifyEmail.Failed", "Failed to verify email"));
        }
    }

    private static string ComputeSha256Hash(string rawToken)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(rawToken);
        byte[] hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
