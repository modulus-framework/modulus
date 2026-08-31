using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Domain.Entities;

/// <summary>
/// Per-user notification preferences: channel opt-in/out per event category,
/// quiet hours, digest frequency, and language. Admin can mark certain rules
/// as mandatory (user cannot mute — e.g. security alerts).
/// </summary>
public sealed class NotificationPreference : AggregateRoot
{
    private NotificationPreference() { }

    internal NotificationPreference(
        NotificationPreferenceId id,
        Guid tenantId,
        Guid userId,
        string eventCategory,
        NotificationChannel enabledChannels,
        bool isMandatory,
        string? quietHoursStart,
        string? quietHoursEnd,
        string? timeZoneId,
        string? digestFrequency,
        string? locale)
    {
        Id = id;
        TenantId = tenantId;
        UserId = userId;
        EventCategory = eventCategory;
        EnabledChannels = enabledChannels;
        IsMandatory = isMandatory;
        QuietHoursStart = quietHoursStart;
        QuietHoursEnd = quietHoursEnd;
        TimeZoneId = timeZoneId ?? "Asia/Dhaka";
        DigestFrequency = digestFrequency;
        Locale = locale ?? "en";
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public new NotificationPreferenceId Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string EventCategory { get; private set; } = null!;
    public NotificationChannel EnabledChannels { get; private set; }
    public bool IsMandatory { get; private set; }
    public string? QuietHoursStart { get; private set; }
    public string? QuietHoursEnd { get; private set; }
    public string? TimeZoneId { get; private set; }
    public string? DigestFrequency { get; private set; }
    public string? Locale { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<NotificationPreference> Create(
        NotificationPreferenceId id,
        Guid tenantId,
        Guid userId,
        string eventCategory,
        NotificationChannel enabledChannels,
        bool isMandatory = false,
        string? quietHoursStart = null,
        string? quietHoursEnd = null,
        string? timeZoneId = null,
        string? digestFrequency = null,
        string? locale = null)
    {
        if (string.IsNullOrWhiteSpace(eventCategory))
            return Result.Failure<NotificationPreference>(Error.Validation("NotificationPreference.EmptyCategory", "Event category is required"));

        return Result.Success(new NotificationPreference(id, tenantId, userId, eventCategory.Trim(), enabledChannels, isMandatory, quietHoursStart, quietHoursEnd, timeZoneId, digestFrequency, locale));
    }

    public void UpdateChannels(NotificationChannel enabledChannels)
    {
        if (IsMandatory)
            return;

        EnabledChannels = enabledChannels;
        UpdatedAtUtc = DateTime.UtcNow;
        IncrementVersion();
    }

    public void UpdateQuietHours(string? start, string? end, string? timeZoneId)
    {
        QuietHoursStart = start;
        QuietHoursEnd = end;
        TimeZoneId = timeZoneId ?? TimeZoneId;
        UpdatedAtUtc = DateTime.UtcNow;
        IncrementVersion();
    }

    public void UpdateDigest(string? frequency)
    {
        DigestFrequency = frequency;
        UpdatedAtUtc = DateTime.UtcNow;
        IncrementVersion();
    }

    public void UpdateLocale(string? locale)
    {
        Locale = locale ?? Locale;
        UpdatedAtUtc = DateTime.UtcNow;
        IncrementVersion();
    }
}
