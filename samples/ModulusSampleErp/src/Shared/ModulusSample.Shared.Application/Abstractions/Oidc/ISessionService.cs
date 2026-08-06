using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Domain.ValueObjects;

namespace ModulusSample.Shared.Application.Abstractions.Oidc;

/// <summary>
/// Service for managing user sessions.
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Creates a new session for the user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="ExternalSessionId">The Authentik session state (JWT 'sid' claim).</param>
    /// <param name="accessTokenJti">The access token JTI.</param>
    /// <param name="deviceInfoJson">The device information as JSON.</param>
    /// <param name="ipAddress">The IP address of the client.</param>
    /// <param name="expiresAtUtc">The expiration time in UTC.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing the session ID.</returns>
    Task<Result<Guid>> CreateSessionAsync(
        Guid userId,
        string ExternalSessionId,
        string accessTokenJti,
        string? userAgent,
        string? ipAddress,
        DateTime expiresAtUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if a session is valid (not revoked and not expired).
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="ExternalSessionId">The Authentik session state.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the session is valid, false otherwise.</returns>
    Task<bool> IsSessionValidAsync(
        Guid userId,
        string? ExternalSessionId,
        CancellationToken ct = default);

    /// <summary>
    /// Validates the session and auto-creates one if it doesn't exist.
    /// Returns false only if an existing session is explicitly revoked.
    /// </summary>
    Task<bool> EnsureSessionAsync(
        Guid userId,
        string ExternalSessionId,
        string? accessTokenJti,
        string? userAgent,
        string? ipAddress,
        DateTime expiresAtUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a session by ID.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing the session information.</returns>
    Task<Result<SessionInfo>> GetSessionAsync(
        Guid sessionId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets a session by the Authentik session state (the 'sid' claim).
    /// </summary>
    /// <param name="ExternalSessionId">The Authentik session state identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing the session information.</returns>
    Task<Result<SessionInfo>> GetSessionByExternalSessionIdAsync(
        string ExternalSessionId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets all active sessions for a user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing the list of active sessions.</returns>
    Task<Result<List<SessionInfo>>> GetActiveSessionsAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes a specific session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="reason">The reason for revocation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the operation.</returns>
    Task<Result> RevokeSessionAsync(
        Guid sessionId,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes all sessions except the specified one.
    /// </summary>
    /// <param name="currentSessionId">The current session ID to keep active.</param>
    /// <param name="reason">The reason for revocation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing the number of revoked sessions.</returns>
    Task<Result<int>> RevokeOtherSessionsAsync(
        Guid currentSessionId,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Revokes all sessions for a user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="reason">The reason for revocation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of the operation.</returns>
    Task<Result> RevokeAllUserSessionsAsync(
        Guid userId,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the last activity time for a session, sliding-renews the expiry, and
    /// backfills device info / IP when the session lacks them.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="userAgent">The User-Agent header of the current request (for device-info backfill).</param>
    /// <param name="ipAddress">The client IP address of the current request (for IP backfill).</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateLastActivityAsync(
        Guid sessionId,
        string? userAgent,
        string? ipAddress,
        CancellationToken ct = default);

    /// <summary>
    /// Stores the id_token hash for logout.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="idTokenHash">The hashed id_token.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetIdTokenHashAsync(
        Guid sessionId,
        string idTokenHash,
        CancellationToken ct = default);

    /// <summary>
    /// Clears the id_token hash after logout.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ClearIdTokenHashAsync(
        Guid sessionId,
        CancellationToken ct = default);
}

/// <summary>
/// Session information DTO.
/// </summary>
public sealed record SessionInfo(
    Guid Id,
    Guid UserId,
    string ExternalSessionId,
    string DeviceInfoJson,
    string? IpAddress,
    DateTime LoginTimeUtc,
    DateTime LastActivityTimeUtc,
    DateTime ExpiresAtUtc,
    bool IsRevoked,
    bool IsCurrent);
