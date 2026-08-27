using ProcureFlow.Modules.Identity.Application.Abstractions.Identity;
using ProcureFlow.Modules.Identity.Domain.Entities;
using ProcureFlow.Modules.Identity.Domain.Repositories;
using ProcureFlow.Modules.Identity.Domain.ValueObjects;
using ProcureFlow.Shared.Domain;
using ProcureFlow.Shared.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ProcureFlow.Modules.Identity.Infrastructure.Services;

/// <summary>
/// Service for managing email verification tokens with SHA-256 hashing and security best practices.
/// </summary>
internal sealed class EmailVerificationTokenService(
    IEmailVerificationTokenRepository tokenRepository,
    IUserRepository userRepository,
    ILogger<EmailVerificationTokenService> logger) : IEmailVerificationTokenService
{
    /// <inheritdoc />
    public async Task<Result<string>> GenerateTokenAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            // Validate user exists
            UserId userIdValue = userId;
            User? user = await userRepository.GetByIdAsync(userIdValue, ct);
            if (user is null)
            {
                logger.LogWarning("Attempted to generate email verification token for non-existent user: {UserId}", userId);
                return Result.Failure<string>(
                    Error.NotFound("User.NotFound", $"User with ID {userId} not found"));
            }

            // Invalidate any existing unused tokens for this user
            await tokenRepository.InvalidateAllUserTokensAsync(userId, ct);

            // Generate new token
            (EmailVerificationToken token, string rawToken) = EmailVerificationToken.Create(userId);

            await tokenRepository.AddAsync(token, ct);

            logger.LogInformation("Generated email verification token for user: {UserId}", userId);

            return Result.Success(rawToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating email verification token for user: {UserId}", userId);
            return Result.Failure<string>(
                Error.Failure("EmailVerificationToken.GenerationFailed", "Failed to generate verification token"));
        }
    }

    /// <inheritdoc />
    public async Task<Result> VerifyTokenAsync(string rawToken, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                return Result.Failure(
                    Error.Validation("EmailVerificationToken.Empty", "Verification token cannot be empty"));
            }

            // Hash the raw token to look up in database
            string tokenHash = ComputeSha256Hash(rawToken);

            // Find token by hash
            EmailVerificationToken? token = await tokenRepository.GetByTokenHashAsync(tokenHash, ct);
            if (token is null)
            {
                logger.LogWarning("Attempted to verify non-existent email verification token");
                return Result.Failure(
                    Error.Validation("EmailVerificationToken.Invalid", "Invalid or expired verification token"));
            }

            // Verify token using constant-time comparison
            Result<bool> verifyResult = token.Verify(rawToken);
            if (verifyResult.IsFailure)
            {
                logger.LogWarning("Email verification token validation failed for user: {UserId}", token.UserId);
                return Result.Failure(verifyResult.Error);
            }

            // Mark token as used
            token.MarkAsUsed();

            logger.LogInformation("Email verification token verified successfully for user: {UserId}", token.UserId);

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error verifying email verification token");
            return Result.Failure(
                Error.Failure("EmailVerificationToken.VerificationFailed", "Failed to verify token"));
        }
    }

    /// <inheritdoc />
    public async Task<Result> InvalidateUserTokensAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            await tokenRepository.InvalidateAllUserTokensAsync(userId, ct);

            logger.LogInformation("Invalidated all email verification tokens for user: {UserId}", userId);

            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error invalidating email verification tokens for user: {UserId}", userId);
            return Result.Failure(
                Error.Failure("EmailVerificationToken.InvalidationFailed", "Failed to invalidate tokens"));
        }
    }

    /// <summary>
    /// Computes SHA-256 hash of the input string.
    /// </summary>
    private static string ComputeSha256Hash(string input)
    {
        byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = System.Security.Cryptography.SHA256.HashData(inputBytes);

        // Convert to hexadecimal string
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
