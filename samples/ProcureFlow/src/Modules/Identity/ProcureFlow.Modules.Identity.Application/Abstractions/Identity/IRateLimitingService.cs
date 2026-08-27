using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Application.Abstractions.Identity;

/// <summary>
/// Service for rate limiting operations to prevent abuse.
/// </summary>
public interface IRateLimitingService
{
    /// <summary>
    /// Checks if an action is allowed for a given user and action type.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="actionType">The type of action being rate limited.</param>
    /// <param name="duration">The duration of the rate limit window.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating if the action is allowed.</returns>
    Task<Result<bool>> IsAllowedAsync(Guid userId, string actionType, TimeSpan duration, CancellationToken ct = default);

    /// <summary>
    /// Records an action for rate limiting purposes.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="actionType">The type of action being rate limited.</param>
    /// <param name="duration">The duration of the rate limit window.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordActionAsync(Guid userId, string actionType, TimeSpan duration, CancellationToken ct = default);

    /// <summary>
    /// Gets the remaining time until the rate limit resets.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="actionType">The type of action.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The remaining time, or null if no rate limit is active.</returns>
    Task<TimeSpan?> GetRemainingTimeAsync(Guid userId, string actionType, CancellationToken ct = default);
}
