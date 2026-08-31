using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Domain.Entities;

/// <summary>
/// A channel-specific, locale-specific notification template.
/// Templates use Handlebars-style {{variable}} placeholders validated against
/// the event payload schema at save time. Per-tenant overrides layer above
/// platform defaults. Versioned for audit.
/// </summary>
public sealed class NotificationTemplate : AggregateRoot
{
    private NotificationTemplate() { }

    internal NotificationTemplate(
        NotificationTemplateId id,
        Guid tenantId,
        string templateKey,
        NotificationChannel channel,
        string locale,
        string subject,
        string body,
        string? variablesJsonSchema,
        int version)
    {
        Id = id;
        TenantId = tenantId;
        TemplateKey = templateKey;
        Channel = channel;
        Locale = locale;
        Subject = subject;
        Body = body;
        VariablesJsonSchema = variablesJsonSchema;
        Version = version;
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public new NotificationTemplateId Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string TemplateKey { get; private set; } = null!;
    public NotificationChannel Channel { get; private set; }
    public string Locale { get; private set; } = null!;
    public string Subject { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public string? VariablesJsonSchema { get; private set; }
    public new int Version { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<NotificationTemplate> Create(
        NotificationTemplateId id,
        Guid tenantId,
        string templateKey,
        NotificationChannel channel,
        string locale,
        string subject,
        string body,
        string? variablesJsonSchema = null)
    {
        if (string.IsNullOrWhiteSpace(templateKey))
            return Result.Failure<NotificationTemplate>(Error.Validation("NotificationTemplate.EmptyKey", "Template key is required"));

        if (string.IsNullOrWhiteSpace(locale))
            return Result.Failure<NotificationTemplate>(Error.Validation("NotificationTemplate.EmptyLocale", "Locale is required"));

        if (string.IsNullOrWhiteSpace(body))
            return Result.Failure<NotificationTemplate>(Error.Validation("NotificationTemplate.EmptyBody", "Body is required"));

        return Result.Success(new NotificationTemplate(id, tenantId, templateKey.Trim(), channel, locale, subject ?? string.Empty, body, variablesJsonSchema, 1));
    }

    public void UpdateContent(string subject, string body, string? variablesJsonSchema)
    {
        Subject = subject ?? string.Empty;
        Body = body;
        VariablesJsonSchema = variablesJsonSchema;
        Version++;
        UpdatedAtUtc = DateTime.UtcNow;
        IncrementVersion();
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAtUtc = DateTime.UtcNow;
        IncrementVersion();
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAtUtc = DateTime.UtcNow;
        IncrementVersion();
    }
}
