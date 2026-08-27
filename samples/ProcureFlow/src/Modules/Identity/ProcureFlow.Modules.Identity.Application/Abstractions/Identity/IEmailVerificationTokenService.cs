using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Application.Abstractions.Identity;

/// <summary>
/// Service interface for email verification token operations.
/// </summary>
public interface IEmailVerificationTokenService
{
    /// <summary>
    /// Generates a new email verification token for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw token string to be sent to the user.</returns>
    Task<Result<string>> GenerateTokenAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Verifies an email verification token.
    /// </summary>
    /// <param name="rawToken">The raw token to verify.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating success or failure with appropriate error.</returns>
    Task<Result> VerifyTokenAsync(string rawToken, CancellationToken ct = default);

    /// <summary>
    /// Invalidates all existing unused tokens for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result> InvalidateUserTokensAsync(Guid userId, CancellationToken ct = default);
}
