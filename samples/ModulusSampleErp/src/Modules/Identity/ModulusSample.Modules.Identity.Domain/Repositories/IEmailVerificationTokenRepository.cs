using ModulusSample.Modules.Identity.Domain.Entities;

namespace ModulusSample.Modules.Identity.Domain.Repositories;

/// <summary>
/// Repository interface for email verification tokens.
/// </summary>
public interface IEmailVerificationTokenRepository
{
    /// <summary>
    /// Gets the latest unused token for a specific user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The latest unused token, or null if none exists.</returns>
    Task<EmailVerificationToken?> GetLatestUnusedTokenAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Gets a token by its hash value.
    /// </summary>
    /// <param name="tokenHash">The SHA-256 hash of the token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The token if found, null otherwise.</returns>
    Task<EmailVerificationToken?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>
    /// Adds a new token to the repository.
    /// </summary>
    /// <param name="token">The token to add.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddAsync(EmailVerificationToken token, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing token.
    /// </summary>
    /// <param name="token">The token to update.</param>
    void Update(EmailVerificationToken token);

    /// <summary>
    /// Deletes a token.
    /// </summary>
    /// <param name="token">The token to delete.</param>
    void Delete(EmailVerificationToken token);

    /// <summary>
    /// Invalidates all unused tokens for a specific user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task InvalidateAllUserTokensAsync(Guid userId, CancellationToken ct = default);
}
