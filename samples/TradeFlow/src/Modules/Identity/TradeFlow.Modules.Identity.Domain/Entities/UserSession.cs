using TradeFlow.Modules.Identity.Domain.Events;
using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Shared.Domain;
using TradeFlow.Shared.Domain.ValueObjects;

namespace TradeFlow.Modules.Identity.Domain.Entities;

public sealed class UserSession : AggregateRoot
{
    private UserSession() { }

    private UserSession(
        Guid id,
        UserId userId,
        string externalSessionId,
        string accessTokenJti,
        DeviceInfo deviceInfo,
        string? ipAddress,
        DateTime expiresAtUtc)
    {
        Id = id;
        UserId = userId;
        ExternalSessionId = externalSessionId;
        AccessTokenJti = accessTokenJti;
        DeviceInfo = deviceInfo;
        IpAddress = ipAddress;
        LoginTimeUtc = DateTime.UtcNow;
        LastActivityTimeUtc = DateTime.UtcNow;
        ExpiresAtUtc = expiresAtUtc;
        IsRevoked = false;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public new Guid Id { get; private set; }
    public UserId UserId { get; private set; } = default!;
    public string ExternalSessionId { get; private set; } = null!;
    public string AccessTokenJti { get; private set; } = null!;
    public string? RefreshTokenJti { get; private set; }
    public DeviceInfo DeviceInfo { get; private set; } = default!;
    public string? IpAddress { get; private set; }
    public DateTime LoginTimeUtc { get; private set; }
    public DateTime LastActivityTimeUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public bool IsRevoked { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? RevokedReason { get; private set; }
    public string? IdTokenHash { get; private set; } // Temporary, for logout
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static UserSession Create(
        UserId userId,
        string externalSessionId,
        string accessTokenJti,
        DeviceInfo deviceInfo,
        string? ipAddress,
        DateTime expiresAtUtc)
    {
        return new UserSession(
            Guid.NewGuid(),
            userId,
            externalSessionId,
            accessTokenJti,
            deviceInfo,
            ipAddress,
            expiresAtUtc);
    }

    public void SetRefreshTokenJti(string refreshTokenJti)
    {
        RefreshTokenJti = refreshTokenJti;
        UpdatedAtUtc = DateTime.UtcNow;
        IncrementVersion();
    }

    public void SetIdTokenHash(string hash)
    {
        IdTokenHash = hash;
        UpdatedAtUtc = DateTime.UtcNow;
        IncrementVersion();
    }

    public void ClearIdTokenHash()
    {
        IdTokenHash = null;
        UpdatedAtUtc = DateTime.UtcNow;
        IncrementVersion();
    }

    /// <summary>
    /// High-frequency ping — intentionally does NOT increment Version
    /// to avoid flooding the concurrency token (same pattern as User.UpdateLastActivity).
    /// </summary>
    public void UpdateLastActivity()
    {
        LastActivityTimeUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Sliding renewal of the SSO session lifetime. Only pushes the expiry forward
    /// (never shortens an existing longer expiry). Increments the concurrency token.
    /// </summary>
    public void Renew(DateTime expiresAtUtc)
    {
        if (expiresAtUtc > ExpiresAtUtc)
        {
            ExpiresAtUtc = expiresAtUtc;
            UpdatedAtUtc = DateTime.UtcNow;
            IncrementVersion();
        }
    }

    /// <summary>
    /// Backfills device info / IP when the session was created without a User-Agent
    /// (e.g. first-creating request was a non-browser client). Idempotent.
    /// </summary>
    public void UpdateDeviceInfo(DeviceInfo deviceInfo, string? ipAddress)
    {
        if (deviceInfo is null)
        {
            throw new ArgumentNullException(nameof(deviceInfo));
        }

        bool deviceChanged = DeviceInfo.IsUnknown && !deviceInfo.IsUnknown;
        bool ipChanged = string.IsNullOrEmpty(IpAddress) && !string.IsNullOrEmpty(ipAddress);

        if (!deviceChanged && !ipChanged)
        {
            return;
        }

        if (deviceChanged)
        {
            DeviceInfo = deviceInfo;
        }

        if (ipChanged)
        {
            IpAddress = ipAddress;
        }

        UpdatedAtUtc = DateTime.UtcNow;
        IncrementVersion();
    }

    public Result Revoke(string reason)
    {
        if (IsRevoked)
        {
            return Result.Success(); // Already revoked, idempotent
        }

        IsRevoked = true;
        RevokedAtUtc = DateTime.UtcNow;
        RevokedReason = reason;
        UpdatedAtUtc = DateTime.UtcNow;
        IncrementVersion();

        Raise(new SessionRevokedDomainEvent(
            Id,
            UserId.Value,
            reason,
            RevokedAtUtc.Value));

        return Result.Success();
    }

    public bool VerifyIdTokenHash(string idToken, string salt)
    {
        if (string.IsNullOrEmpty(IdTokenHash))
        {
            return false;
        }

        string computedHash = ComputeHash(idToken, salt);
        return IdTokenHash == computedHash;
    }

    private static string ComputeHash(string input, string salt)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(input + salt);
        byte[] hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

    public static string HashIdToken(string idToken, string salt)
    {
        return ComputeHash(idToken, salt);
    }

    public bool IsActive =>
        !IsRevoked &&
        ExpiresAtUtc > DateTime.UtcNow;

    public TimeSpan TimeUntilExpiry =>
        ExpiresAtUtc - DateTime.UtcNow;
}
