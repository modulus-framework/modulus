using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Domain.Entities;

/// <summary>
/// Device token for push notifications.
/// </summary>
public sealed class DeviceToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Token { get; private set; } = default!;
    public string DeviceType { get; private set; } = default!; // iOS, Android, Web
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastUsedAt { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public bool IsActive { get; private set; }

    private const int TokenExpiryDays = 90;

    private DeviceToken() { } // EF Core

    public static Result<DeviceToken> Create(
        Guid userId,
        string token,
        string deviceType)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<DeviceToken>(Error.Validation(
                "DeviceToken.UserIdRequired", "User ID cannot be empty"));
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Failure<DeviceToken>(Error.Validation(
                "DeviceToken.TokenRequired", "Token cannot be empty"));
        }

        if (!IsValidDeviceType(deviceType))
        {
            return Result.Failure<DeviceToken>(Error.Validation(
                "DeviceToken.InvalidDeviceType",
                $"Invalid device type: {deviceType}. Must be iOS, Android, or Web"));
        }

        return Result.Success(new DeviceToken(userId, token, deviceType));
    }

    private DeviceToken(
        Guid userId,
        string token,
        string deviceType)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Token = token;
        DeviceType = deviceType;
        CreatedAt = DateTime.UtcNow;
        ExpiresAt = DateTime.UtcNow.AddDays(TokenExpiryDays);
        IsActive = true;
    }

    public void UpdateLastUsed()
    {
        LastUsedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Reactivate()
    {
        IsActive = true;
        LastUsedAt = DateTime.UtcNow;
    }

    public bool IsExpired()
    {
        return ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;
    }

    private static bool IsValidDeviceType(string deviceType)
    {
        return deviceType is "iOS" or "Android" or "Web";
    }
}
