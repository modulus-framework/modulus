using TradeFlow.Modules.Identity.Application;
using TradeFlow.Modules.Identity.Domain.Entities;
using TradeFlow.Modules.Identity.Domain.Errors;
using TradeFlow.Modules.Identity.Domain.Repositories;
using TradeFlow.Modules.Identity.Infrastructure.Configuration;
using TradeFlow.Shared.Application.Abstractions.Oidc;
using TradeFlow.Shared.Application.Caching;
using System.Text.Json;
using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Infrastructure.Services;

internal sealed class SessionService(
    IUserSessionRepository sessionRepository,
    IUnitOfWork unitOfWork,
    ICacheService cache,
    IOptions<SessionOptions> options,
    IOptions<UsersSettings> usersSettings,
    ILogger<SessionService> logger)
    : ISessionService
{
    private const int DefaultMaxSessions = 5;
    private const string CacheValueValid = "valid";
    private const string CacheValueRevoked = "revoked";
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromHours(1);

    private readonly SessionOptions _options = options.Value;

    /// <summary>
    /// Effective session lifetime — uses the shorter of <see cref="SessionOptions.SessionLifetime"/>
    /// and <see cref="AuthenticationSettings.SessionTimeoutMinutes"/> from the Users module config
    /// so that the admin-configurable timeout actually takes effect.
    /// </summary>
    private TimeSpan EffectiveSessionLifetime
    {
        get
        {
            int timeoutMinutes = usersSettings.Value.Authentication.SessionTimeoutMinutes;
            if (timeoutMinutes <= 0)
            {
                timeoutMinutes = 60;
            }

            var moduleTimeout = TimeSpan.FromMinutes(timeoutMinutes);
            return moduleTimeout < _options.SessionLifetime ? moduleTimeout : _options.SessionLifetime;
        }
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> CreateSessionAsync(
        Guid userId,
        string ExternalSessionId,
        string accessTokenJti,
        string? userAgent,
        string? ipAddress,
        DateTime expiresAtUtc,
        CancellationToken ct = default)
    {
        try
        {
            DeviceInfo deviceInfo = ParseDeviceInfoFromUserAgent(userAgent);

            // Create session — expiry tracks the SSO session, not the access token.
            var session = UserSession.Create(
                UserId.Create(userId),
                ExternalSessionId,
                accessTokenJti,
                deviceInfo,
                ipAddress,
                ComputeSessionExpiry(expiresAtUtc));

            // Check concurrent session limit
            int activeCount = await sessionRepository.GetActiveCountByUserIdAsync(UserId.Create(userId), ct);
            int maxSessions = _options.MaxSessions ?? DefaultMaxSessions;

            // Validate MaxSessions is positive
            if (maxSessions <= 0)
            {
                logger.LogWarning(
                    "Invalid MaxSessions value {MaxSessions} configured, using default {DefaultMaxSessions}",
                    maxSessions, DefaultMaxSessions);
                maxSessions = DefaultMaxSessions;
            }

            if (activeCount >= maxSessions)
            {
                UserSession? oldestSession = await sessionRepository.GetOldestActiveSessionAsync(UserId.Create(userId), ct);
                if (oldestSession != null)
                {
                    Result revokeResult = oldestSession.Revoke("Session limit exceeded");
                    if (revokeResult.IsSuccess)
                    {
                        await InvalidateCacheAsync(oldestSession, ct);
                        logger.LogInformation(
                            "Revoked oldest session {SessionId} for user {Guid} due to limit",
                            oldestSession.Id, userId);
                    }
                }
            }

            await sessionRepository.AddAsync(session, ct);
            await unitOfWork.CommitAsync(ct);

            // Cache the new session
            await CacheSessionAsync(session, ct);

            logger.LogInformation(
                "Created session {SessionId} for user {Guid}",
                session.Id, userId);

            return Result.Success(session.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating session for user {Guid}", userId);
            return Result.Failure<Guid>(
                Error.Failure("Session.CreateFailed", "Failed to create session"));
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsSessionValidAsync(
        Guid userId,
        string? ExternalSessionId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(ExternalSessionId))
        {
            return true;
        }

        string cacheKey = GetSessionCacheKey(userId, ExternalSessionId);
        string? cached = await cache.GetStringAsync(cacheKey, ct);

        if (cached == CacheValueValid)
        {
            return true;
        }

        if (cached == CacheValueRevoked)
        {
            UserSession? revokedSession = await sessionRepository.GetByExternalSessionIdAsync(ExternalSessionId, ct);
            if (revokedSession == null)
            {
                await cache.RemoveAsync(cacheKey, ct);
                return true;
            }

            return false;
        }

        UserSession? session = await sessionRepository.GetByExternalSessionIdAsync(ExternalSessionId, ct);

        if (session == null)
        {
            return true;
        }

        bool isValid = session.UserId.Value == userId && session.IsActive;

        await cache.SetStringAsync(
            cacheKey,
            isValid ? CacheValueValid : CacheValueRevoked,
            CacheExpiration,
            ct);

        return isValid;
    }

    /// <inheritdoc />
    public async Task<bool> EnsureSessionAsync(
        Guid userId,
        string ExternalSessionId,
        string? accessTokenJti,
        string? userAgent,
        string? ipAddress,
        DateTime expiresAtUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(ExternalSessionId))
        {
            return true;
        }

        string cacheKey = GetSessionCacheKey(userId, ExternalSessionId);
        string? cached = await cache.GetStringAsync(cacheKey, ct);

        if (cached == CacheValueValid)
        {
            return true;
        }

        UserSession? session = await sessionRepository.GetByExternalSessionIdAsync(ExternalSessionId, ct);

        if (cached == CacheValueRevoked)
        {
            if (session == null)
            {
                logger.LogInformation("Stale 'revoked' cache for session {SessionState}, auto-creating", ExternalSessionId);
                await cache.RemoveAsync(cacheKey, ct);
            }
            else
            {
                // Only an explicitly revoked (or user-mismatched) session blocks the request.
                // A stale "revoked" cache entry for a non-revoked row means the session expired
                // against the old (access-token-derived) expiry while the SSO session is still
                // valid — renew it and allow the request through.
                if (session.IsRevoked || session.UserId.Value != userId)
                {
                    return false;
                }

                await TouchAsync(session, userAgent, ipAddress, ct);
                await cache.SetStringAsync(cacheKey, CacheValueValid, CacheExpiration, ct);
                return true;
            }
        }

        if (session != null)
        {
            if (session.IsRevoked || session.UserId.Value != userId)
            {
                await cache.SetStringAsync(cacheKey, CacheValueRevoked, CacheExpiration, ct);
                return false;
            }

            // The SSO session is alive. Slide the expiry and backfill device info, but never
            // block on a stale access-token-derived expiry (the token was just validated).
            await TouchAsync(session, userAgent, ipAddress, ct);
            await cache.SetStringAsync(cacheKey, CacheValueValid, CacheExpiration, ct);
            return true;
        }

        try
        {
            DeviceInfo deviceInfo = ParseDeviceInfoFromUserAgent(userAgent);

            var newSession = UserSession.Create(
                UserId.Create(userId),
                ExternalSessionId,
                accessTokenJti ?? Guid.NewGuid().ToString(),
                deviceInfo,
                ipAddress,
                ComputeSessionExpiry(expiresAtUtc));

            int activeCount = await sessionRepository.GetActiveCountByUserIdAsync(UserId.Create(userId), ct);
            int maxSessions = _options.MaxSessions ?? DefaultMaxSessions;

            if (maxSessions <= 0)
            {
                maxSessions = DefaultMaxSessions;
            }

            if (activeCount >= maxSessions)
            {
                UserSession? oldestSession = await sessionRepository.GetOldestActiveSessionAsync(userId, ct);
                if (oldestSession != null)
                {
                    oldestSession.Revoke("Session limit exceeded");
                    await InvalidateCacheAsync(oldestSession, ct);
                }
            }

            await sessionRepository.AddAsync(newSession, ct);
            await unitOfWork.CommitAsync(ct);
            await CacheSessionAsync(newSession, ct);

            logger.LogInformation(
                "Auto-created session {SessionId} for user {Guid} during auth pipeline",
                newSession.Id, userId);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to auto-create session for user {Guid}, allowing request through", userId);
            return true;
        }
    }

    /// <inheritdoc />
    public async Task<Result<SessionInfo>> GetSessionAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        UserSession? session = await sessionRepository.GetByIdAsync(sessionId, ct);
        if (session == null)
        {
            return Result.Failure<SessionInfo>(SessionErrors.NotFound);
        }

        return Result.Success(MapToSessionInfo(session, isCurrent: false));
    }

    /// <inheritdoc />
    public async Task<Result<SessionInfo>> GetSessionByExternalSessionIdAsync(
        string ExternalSessionId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(ExternalSessionId))
        {
            return Result.Failure<SessionInfo>(SessionErrors.InvalidSessionState);
        }

        UserSession? session = await sessionRepository.GetByExternalSessionIdAsync(ExternalSessionId, ct);
        if (session == null)
        {
            return Result.Failure<SessionInfo>(SessionErrors.NotFound);
        }

        return Result.Success(MapToSessionInfo(session, isCurrent: false));
    }

    /// <inheritdoc />
    public async Task<Result<List<SessionInfo>>> GetActiveSessionsAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        List<UserSession> sessions = await sessionRepository.GetActiveByUserIdAsync(UserId.Create(userId), ct);
        return Result.Success(sessions.Select(s => MapToSessionInfo(s, isCurrent: false)).ToList());
    }

    /// <inheritdoc />
    public async Task<Result> RevokeSessionAsync(
        Guid sessionId,
        string reason,
        CancellationToken ct = default)
    {
        UserSession? session = await sessionRepository.GetByIdAsync(sessionId, ct);
        if (session == null)
        {
            return Result.Failure(SessionErrors.NotFound);
        }

        Result result = session.Revoke(reason);
        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.CommitAsync(ct);
        await InvalidateCacheAsync(session, ct);

        logger.LogInformation("Revoked session {SessionId} for reason: {Reason}", sessionId, reason);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<int>> RevokeOtherSessionsAsync(
        Guid currentSessionId,
        string reason,
        CancellationToken ct = default)
    {
        UserSession? currentSession = await sessionRepository.GetByIdAsync(currentSessionId, ct);
        if (currentSession == null)
        {
            return Result.Failure<int>(SessionErrors.NotFound);
        }

        List<UserSession> activeSessions = await sessionRepository.GetActiveByUserIdAsync(currentSession.UserId, ct);
        var otherSessions = activeSessions.Where(s => s.Id != currentSessionId).ToList();

        int revokedCount = 0;
        foreach (UserSession session in otherSessions)
        {
            session.Revoke(reason);
            await sessionRepository.UpdateAsync(session, ct);
            await InvalidateCacheAsync(session, ct);
            revokedCount++;
        }

        await unitOfWork.CommitAsync(ct);

        logger.LogInformation(
            "Revoked {Count} other sessions for user {Guid}",
            revokedCount, currentSession.UserId.Value);

        return Result.Success(revokedCount);
    }

    /// <inheritdoc />
    public async Task<Result> RevokeAllUserSessionsAsync(
        Guid userId,
        string reason,
        CancellationToken ct = default)
    {
        List<UserSession> activeSessions = await sessionRepository.GetActiveByUserIdAsync(UserId.Create(userId), ct);

        foreach (UserSession session in activeSessions)
        {
            session.Revoke(reason);
            await sessionRepository.UpdateAsync(session, ct);
            await InvalidateCacheAsync(session, ct);
        }

        await unitOfWork.CommitAsync(ct);

        logger.LogInformation(
            "Revoked sessions for user {Guid}",
            userId);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task UpdateLastActivityAsync(
        Guid sessionId,
        string? userAgent,
        string? ipAddress,
        CancellationToken ct = default)
    {
        try
        {
            UserSession? session = await sessionRepository.GetByIdAsync(sessionId, ct);
            if (session == null)
            {
                return;
            }

            await TouchAsync(session, userAgent, ipAddress, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating last activity for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Updates last activity, sliding-renews the expiry when within the renewal window, and
    /// backfills device info / IP when the session was created without a User-Agent.
    /// Persists via the change tracker (entity must be tracked or attachable).
    /// </summary>
    private async Task TouchAsync(
        UserSession session,
        string? userAgent,
        string? ipAddress,
        CancellationToken ct)
    {
        try
        {
            bool changed = false;

            session.UpdateLastActivity();
            changed = true;

            if (NeedsRenewal(session))
            {
                session.Renew(ComputeSessionExpiry());
                changed = true;
            }

            DeviceInfo parsed = ParseDeviceInfoFromUserAgent(userAgent);
            if (session.DeviceInfo.IsUnknown && !parsed.IsUnknown)
            {
                session.UpdateDeviceInfo(parsed, ipAddress);
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            // UpdateAsync attaches an AsNoTracking entity as Modified so the touch persists.
            await sessionRepository.UpdateAsync(session, ct);
            await unitOfWork.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error touching session {SessionId}", session.Id);
        }
    }

    /// <inheritdoc />
    public async Task SetIdTokenHashAsync(
        Guid sessionId,
        string idTokenHash,
        CancellationToken ct = default)
    {
        try
        {
            UserSession? session = await sessionRepository.GetByIdAsync(sessionId, ct);
            if (session != null)
            {
                session.SetIdTokenHash(idTokenHash);
                await unitOfWork.CommitAsync(ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error setting ID token hash for session {SessionId}", sessionId);
        }
    }

    /// <inheritdoc />
    public async Task ClearIdTokenHashAsync(
        Guid sessionId,
        CancellationToken ct = default)
    {
        try
        {
            UserSession? session = await sessionRepository.GetByIdAsync(sessionId, ct);
            if (session != null)
            {
                session.ClearIdTokenHash();
                await unitOfWork.CommitAsync(ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error clearing ID token hash for session {SessionId}", sessionId);
        }
    }

    private static SessionInfo MapToSessionInfo(UserSession session, bool isCurrent)
    {
        return new SessionInfo(
            session.Id,
            session.UserId.Value,
            session.ExternalSessionId,
            session.DeviceInfo.ToJson(),
            session.IpAddress,
            session.LoginTimeUtc,
            session.LastActivityTimeUtc,
            session.ExpiresAtUtc,
            session.IsRevoked,
            isCurrent);
    }

    private async Task CacheSessionAsync(UserSession session, CancellationToken ct)
    {
        string cacheKey = GetSessionCacheKey(session.UserId.Value, session.ExternalSessionId);
        await cache.SetStringAsync(cacheKey, CacheValueValid, CacheExpiration, ct);

        string userIdKey = GetUserSessionsCacheKey(session.UserId.Value);
        await cache.SetStringAsync(userIdKey, session.Id.ToString(), CacheExpiration, ct);
    }

    private async Task InvalidateCacheAsync(UserSession session, CancellationToken ct)
    {
        string cacheKey = GetSessionCacheKey(session.UserId.Value, session.ExternalSessionId);
        await cache.SetStringAsync(cacheKey, CacheValueRevoked, CacheExpiration, ct);
    }

    private static string GetSessionCacheKey(Guid userId, string ExternalSessionId) =>
        $"session:{userId}:{ExternalSessionId}";

    private static string GetUserSessionsCacheKey(Guid userId) =>
        $"sessions:user:{userId}";

    /// <summary>
    /// Effective session expiry = now + effective session lifetime.
    /// The local session tracks the SSO session, which outlives the
    /// short-lived access token — so we do NOT cap at the token's <c>exp</c> claim.
    /// The <paramref name="tokenExpiryUtc"/> is accepted for API compatibility but ignored.
    /// </summary>
    private DateTime ComputeSessionExpiry(DateTime? tokenExpiryUtc = null)
    {
        _ = tokenExpiryUtc; // intentionally ignored — session tracks SSO, not access token
        return DateTime.UtcNow.Add(EffectiveSessionLifetime);
    }

    /// <summary>
    /// True when the session should be sliding-renewed (remaining lifetime below the
    /// configured threshold) so we don't write to the DB on every request.
    /// </summary>
    private bool NeedsRenewal(UserSession session)
    {
        double threshold = EffectiveSessionLifetime.TotalSeconds * _options.RenewalThreshold;
        return session.ExpiresAtUtc - DateTime.UtcNow < TimeSpan.FromSeconds(threshold);
    }

    private static DeviceInfo ParseDeviceInfoFromUserAgent(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
        {
            return DeviceInfo.Empty;
        }

        string browser = "Unknown";
        string? browserVersion = null;
        string os = "Unknown";
        string? osVersion = null;
        string deviceType;

        if (userAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase))
        {
            deviceType = "mobile";
        }
        else if (userAgent.Contains("Tablet", StringComparison.OrdinalIgnoreCase))
        {
            deviceType = "tablet";
        }
        else
        {
            deviceType = "desktop";
        }

        if (userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase))
        {
            os = "Windows";
        }
        else if (userAgent.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase))
        {
            os = "macOS";
        }
        else if (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase))
        {
            os = "Android";
        }
        else if (userAgent.Contains("iOS", StringComparison.OrdinalIgnoreCase) ||
                 userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
                 userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase))
        {
            os = "iOS";
        }
        else if (userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase))
        {
            os = "Linux";
        }

        if (userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase))
        {
            browser = "Edge";
        }
        else if (userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) &&
                 !userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase))
        {
            browser = "Chrome";
        }
        else if (userAgent.Contains("Firefox/", StringComparison.OrdinalIgnoreCase))
        {
            browser = "Firefox";
        }
        else if (userAgent.Contains("Safari", StringComparison.OrdinalIgnoreCase) &&
                 !userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase))
        {
            browser = "Safari";
        }

        string json = JsonSerializer.Serialize(new
        {
            browser,
            browserVersion,
            os,
            osVersion,
            deviceType
        });

        return DeviceInfo.FromJson(json);
    }
}

/// <summary>
/// Session service options.
/// </summary>
public sealed class SessionOptions
{
    public int? MaxSessions { get; set; } = 5;

    /// <summary>
    /// Local session lifetime (tracks the SSO / refresh-token session, NOT the
    /// short-lived access token). Sessions are renewed on a sliding window while the user
    /// is active. Default 30 days to match the refresh-token lifetime.
    /// </summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Expiry is renewed only when the remaining lifetime drops below this fraction of
    /// <see cref="SessionLifetime"/>, to avoid a DB write on every request.
    /// </summary>
    public double RenewalThreshold { get; set; } = 0.25;
}
