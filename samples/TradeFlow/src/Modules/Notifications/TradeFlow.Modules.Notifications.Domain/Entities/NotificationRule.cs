using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Domain.Entities;

/// <summary>
/// Defines when and how a business event triggers notifications.
/// One rule per (tenant, event_key). Audience, channels, severity, template, and throttling
/// are all stored as JSON for maximum flexibility.
/// </summary>
public sealed class NotificationRule : AggregateRoot
{
    private NotificationRule() { }

    internal NotificationRule(
        NotificationRuleId id,
        Guid tenantId,
        string eventKey,
        string audienceJson,
        NotificationChannel channels,
        NotificationSeverity severity,
        string? templateKey,
        string? throttleJson,
        bool enabled)
    {
        Id = id;
        TenantId = tenantId;
        EventKey = eventKey;
        AudienceJson = audienceJson;
        Channels = channels;
        Severity = severity;
        TemplateKey = templateKey;
        ThrottleJson = throttleJson;
        Enabled = enabled;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public new NotificationRuleId Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string EventKey { get; private set; } = null!;
    public string AudienceJson { get; private set; } = null!;
    public NotificationChannel Channels { get; private set; }
    public NotificationSeverity Severity { get; private set; }
    public string? TemplateKey { get; private set; }
    public string? ThrottleJson { get; private set; }
    public bool Enabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<NotificationRule> Create(
        NotificationRuleId id,
        Guid tenantId,
        string eventKey,
        string audienceJson,
        NotificationChannel channels,
        NotificationSeverity severity,
        string? templateKey,
        string? throttleJson,
        bool enabled = true)
    {
        if (string.IsNullOrWhiteSpace(eventKey))
            return Result.Failure<NotificationRule>(Error.Validation("NotificationRule.EmptyEventKey", "Event key is required"));

        if (channels == NotificationChannel.None)
            return Result.Failure<NotificationRule>(Error.Validation("NotificationRule.NoChannels", "At least one channel must be specified"));

        if (string.IsNullOrWhiteSpace(audienceJson))
            return Result.Failure<NotificationRule>(Error.Validation("NotificationRule.EmptyAudience", "Audience configuration is required"));

        return Result.Success(new NotificationRule(id, tenantId, eventKey.Trim(), audienceJson, channels, severity, templateKey, throttleJson, enabled));
    }

    public void Update(
        string audienceJson,
        NotificationChannel channels,
        NotificationSeverity severity,
        string? templateKey,
        string? throttleJson,
        bool enabled)
    {
        AudienceJson = audienceJson;
        Channels = channels;
        Severity = severity;
        TemplateKey = templateKey;
        ThrottleJson = throttleJson;
        Enabled = enabled;
        UpdatedAtUtc = DateTime.UtcNow;
        IncrementVersion();
    }

    public void Disable()
    {
        Enabled = false;
        UpdatedAtUtc = DateTime.UtcNow;
        IncrementVersion();
    }

    public void Enable()
    {
        Enabled = true;
        UpdatedAtUtc = DateTime.UtcNow;
        IncrementVersion();
    }
}
