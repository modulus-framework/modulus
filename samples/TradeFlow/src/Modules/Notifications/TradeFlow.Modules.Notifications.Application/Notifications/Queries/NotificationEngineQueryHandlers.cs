using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Notifications.Application.Notifications.Dtos;
using TradeFlow.Modules.Notifications.Application.Notifications.Queries;
using TradeFlow.Modules.Notifications.Domain.Constants;
using TradeFlow.Modules.Notifications.Domain.Entities;
using TradeFlow.Modules.Notifications.Domain.Repositories;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Application.Notifications.Queries;

// ── Notification Rules ──

public sealed class GetNotificationRuleByIdHandler(
    INotificationRuleRepository ruleRepository,
    ICurrentTenant currentTenant) : IQueryHandler<GetNotificationRuleByIdQuery, Result<NotificationRuleResponse>>
{
    public async Task<Result<NotificationRuleResponse>> HandleAsync(GetNotificationRuleByIdQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var rule = await ruleRepository.GetByIdAsync(NotificationRuleId.From(request.RuleId), tenantId, ct);

        if (rule is null)
            return Result.Failure<NotificationRuleResponse>(NotificationErrors.RuleNotFound);

        return Result.Success(ToResponse(rule));
    }

    private static NotificationRuleResponse ToResponse(Domain.Entities.NotificationRule r) => new(
        r.Id.Value, r.TenantId, r.EventKey, r.AudienceJson, r.Channels,
        r.Severity, r.TemplateKey, r.ThrottleJson, r.Enabled,
        r.CreatedAtUtc, r.UpdatedAtUtc);
}

public sealed class GetNotificationRulesHandler(
    INotificationRuleRepository ruleRepository,
    ICurrentTenant currentTenant) : IQueryHandler<GetNotificationRulesQuery, Result<IReadOnlyList<NotificationRuleResponse>>>
{
    public async Task<Result<IReadOnlyList<NotificationRuleResponse>>> HandleAsync(GetNotificationRulesQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var rules = await ruleRepository.GetAllAsync(tenantId, ct);
        return Result.Success<IReadOnlyList<NotificationRuleResponse>>(rules.Select(ToResponse).ToList());
    }

    private static NotificationRuleResponse ToResponse(Domain.Entities.NotificationRule r) => new(
        r.Id.Value, r.TenantId, r.EventKey, r.AudienceJson, r.Channels,
        r.Severity, r.TemplateKey, r.ThrottleJson, r.Enabled,
        r.CreatedAtUtc, r.UpdatedAtUtc);
}

public sealed class GetNotificationRulesByEventKeyHandler(
    INotificationRuleRepository ruleRepository,
    ICurrentTenant currentTenant) : IQueryHandler<GetNotificationRulesByEventKeyQuery, Result<IReadOnlyList<NotificationRuleResponse>>>
{
    public async Task<Result<IReadOnlyList<NotificationRuleResponse>>> HandleAsync(GetNotificationRulesByEventKeyQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var rules = await ruleRepository.GetByEventKeyAsync(request.EventKey, tenantId, ct);
        return Result.Success<IReadOnlyList<NotificationRuleResponse>>(rules.Select(ToResponse).ToList());
    }

    private static NotificationRuleResponse ToResponse(Domain.Entities.NotificationRule r) => new(
        r.Id.Value, r.TenantId, r.EventKey, r.AudienceJson, r.Channels,
        r.Severity, r.TemplateKey, r.ThrottleJson, r.Enabled,
        r.CreatedAtUtc, r.UpdatedAtUtc);
}

// ── Notification Templates ──

public sealed class GetNotificationTemplateByIdHandler(
    INotificationTemplateRepository templateRepository,
    ICurrentTenant currentTenant) : IQueryHandler<GetNotificationTemplateByIdQuery, Result<NotificationTemplateResponse>>
{
    public async Task<Result<NotificationTemplateResponse>> HandleAsync(GetNotificationTemplateByIdQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var template = await templateRepository.GetByIdAsync(NotificationTemplateId.From(request.TemplateId), tenantId, ct);

        if (template is null)
            return Result.Failure<NotificationTemplateResponse>(NotificationErrors.TemplateNotFound);

        return Result.Success(ToResponse(template));
    }

    private static NotificationTemplateResponse ToResponse(Domain.Entities.NotificationTemplate t) => new(
        t.Id.Value, t.TenantId, t.TemplateKey, t.Channel, t.Locale,
        t.Subject, t.Body, t.VariablesJsonSchema, t.Version, t.IsActive,
        t.CreatedAtUtc, t.UpdatedAtUtc);
}

public sealed class GetNotificationTemplatesHandler(
    INotificationTemplateRepository templateRepository,
    ICurrentTenant currentTenant) : IQueryHandler<GetNotificationTemplatesQuery, Result<IReadOnlyList<NotificationTemplateResponse>>>
{
    public async Task<Result<IReadOnlyList<NotificationTemplateResponse>>> HandleAsync(GetNotificationTemplatesQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var templates = await templateRepository.GetAllAsync(tenantId, ct);
        return Result.Success<IReadOnlyList<NotificationTemplateResponse>>(templates.Select(ToResponse).ToList());
    }

    private static NotificationTemplateResponse ToResponse(Domain.Entities.NotificationTemplate t) => new(
        t.Id.Value, t.TenantId, t.TemplateKey, t.Channel, t.Locale,
        t.Subject, t.Body, t.VariablesJsonSchema, t.Version, t.IsActive,
        t.CreatedAtUtc, t.UpdatedAtUtc);
}

// ── Notification Preferences ──

public sealed class GetNotificationPreferenceByIdHandler(
    INotificationPreferenceRepository preferenceRepository,
    ICurrentTenant currentTenant) : IQueryHandler<GetNotificationPreferenceByIdQuery, Result<NotificationPreferenceResponse>>
{
    public async Task<Result<NotificationPreferenceResponse>> HandleAsync(GetNotificationPreferenceByIdQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var pref = await preferenceRepository.GetByIdAsync(NotificationPreferenceId.From(request.PreferenceId), tenantId, ct);

        if (pref is null)
            return Result.Failure<NotificationPreferenceResponse>(NotificationErrors.PreferenceNotFound);

        return Result.Success(ToResponse(pref));
    }

    private static NotificationPreferenceResponse ToResponse(Domain.Entities.NotificationPreference p) => new(
        p.Id.Value, p.TenantId, p.UserId, p.EventCategory,
        p.EnabledChannels, p.IsMandatory, p.QuietHoursStart, p.QuietHoursEnd,
        p.TimeZoneId, p.DigestFrequency, p.Locale, p.CreatedAtUtc, p.UpdatedAtUtc);
}

public sealed class GetMyNotificationPreferencesHandler(
    INotificationPreferenceRepository preferenceRepository,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : IQueryHandler<GetMyNotificationPreferencesQuery, Result<IReadOnlyList<NotificationPreferenceResponse>>>
{
    public async Task<Result<IReadOnlyList<NotificationPreferenceResponse>>> HandleAsync(GetMyNotificationPreferencesQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        Guid userId = currentUser.UserId ?? Guid.Empty;
        var prefs = await preferenceRepository.GetByUserAsync(userId, tenantId, ct);
        return Result.Success<IReadOnlyList<NotificationPreferenceResponse>>(prefs.Select(ToResponse).ToList());
    }

    private static NotificationPreferenceResponse ToResponse(Domain.Entities.NotificationPreference p) => new(
        p.Id.Value, p.TenantId, p.UserId, p.EventCategory,
        p.EnabledChannels, p.IsMandatory, p.QuietHoursStart, p.QuietHoursEnd,
        p.TimeZoneId, p.DigestFrequency, p.Locale, p.CreatedAtUtc, p.UpdatedAtUtc);
}

// ── Notification Logs ──

public sealed class GetNotificationLogsByNotificationHandler(
    INotificationLogRepository logRepository,
    ICurrentTenant currentTenant) : IQueryHandler<GetNotificationLogsByNotificationQuery, Result<IReadOnlyList<NotificationLogResponse>>>
{
    public async Task<Result<IReadOnlyList<NotificationLogResponse>>> HandleAsync(GetNotificationLogsByNotificationQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var logs = await logRepository.GetByNotificationAsync(request.NotificationId, tenantId, ct);
        return Result.Success<IReadOnlyList<NotificationLogResponse>>(logs.Select(ToResponse).ToList());
    }

    private static NotificationLogResponse ToResponse(Domain.Entities.NotificationLog l) => new(
        l.Id.Value, l.TenantId, l.NotificationId, l.EventKey,
        l.RecipientUserId, l.Channel, l.Status, l.ProviderMessageId,
        l.ProviderResponse, l.ErrorMessage, l.RetryCount, l.NextRetryAtUtc,
        l.SentAtUtc, l.DeliveredAtUtc, l.ReadAtUtc, l.CreatedAtUtc);
}

public sealed class GetFailedNotificationLogsHandler(
    INotificationLogRepository logRepository,
    ICurrentTenant currentTenant) : IQueryHandler<GetFailedNotificationLogsQuery, Result<PagedResult<NotificationLogResponse>>>
{
    public async Task<Result<PagedResult<NotificationLogResponse>>> HandleAsync(GetFailedNotificationLogsQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var paged = await logRepository.GetFailedAsync(tenantId, request.PageNumber, request.PageSize, ct);

        var responses = paged.Items.Select(ToResponse).ToList();
        return Result.Success(new PagedResult<NotificationLogResponse>(
            responses, paged.TotalCount, request.PageNumber, request.PageSize));
    }

    private static NotificationLogResponse ToResponse(Domain.Entities.NotificationLog l) => new(
        l.Id.Value, l.TenantId, l.NotificationId, l.EventKey,
        l.RecipientUserId, l.Channel, l.Status, l.ProviderMessageId,
        l.ProviderResponse, l.ErrorMessage, l.RetryCount, l.NextRetryAtUtc,
        l.SentAtUtc, l.DeliveredAtUtc, l.ReadAtUtc, l.CreatedAtUtc);
}
