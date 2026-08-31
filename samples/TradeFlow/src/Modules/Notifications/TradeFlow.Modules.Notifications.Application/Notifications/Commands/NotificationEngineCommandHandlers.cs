using System.Text.Json;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Notifications.Application.Notifications.Commands;
using TradeFlow.Modules.Notifications.Application.Notifications.Dtos;
using TradeFlow.Modules.Notifications.Domain.Constants;
using TradeFlow.Modules.Notifications.Domain.Entities;
using TradeFlow.Modules.Notifications.Domain.Repositories;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Application.Notifications.Commands;

// ─────────────────────────────────────────────────────────────
//  Notification Rules
// ─────────────────────────────────────────────────────────────

public sealed class CreateNotificationRuleCommandHandler(
    INotificationRuleRepository ruleRepository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateNotificationRuleCommand, Result<NotificationRuleResponse>>
{
    public async Task<Result<NotificationRuleResponse>> HandleAsync(CreateNotificationRuleCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var id = NotificationRuleId.Create();

        var result = NotificationRule.Create(
            id, tenantId, request.EventKey, request.AudienceJson,
            request.Channels, request.Severity, request.TemplateKey,
            request.ThrottleJson, request.Enabled);

        if (result.IsFailure)
            return Result.Failure<NotificationRuleResponse>(result.Error);

        await ruleRepository.AddAsync(result.Value, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(ToResponse(result.Value));
    }

    private static NotificationRuleResponse ToResponse(NotificationRule r) => new(
        r.Id.Value, r.TenantId, r.EventKey, r.AudienceJson, r.Channels,
        r.Severity, r.TemplateKey, r.ThrottleJson, r.Enabled,
        r.CreatedAtUtc, r.UpdatedAtUtc);
}

public sealed class UpdateNotificationRuleCommandHandler(
    INotificationRuleRepository ruleRepository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<UpdateNotificationRuleCommand, Result<NotificationRuleResponse>>
{
    public async Task<Result<NotificationRuleResponse>> HandleAsync(UpdateNotificationRuleCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var rule = await ruleRepository.GetByIdAsync(NotificationRuleId.From(request.RuleId), tenantId, ct);

        if (rule is null)
            return Result.Failure<NotificationRuleResponse>(NotificationErrors.RuleNotFound);

        rule.Update(request.AudienceJson, request.Channels, request.Severity,
            request.TemplateKey, request.ThrottleJson, request.Enabled);

        await ruleRepository.UpdateAsync(rule, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(ToResponse(rule));
    }

    private static NotificationRuleResponse ToResponse(NotificationRule r) => new(
        r.Id.Value, r.TenantId, r.EventKey, r.AudienceJson, r.Channels,
        r.Severity, r.TemplateKey, r.ThrottleJson, r.Enabled,
        r.CreatedAtUtc, r.UpdatedAtUtc);
}

public sealed class DeleteNotificationRuleCommandHandler(
    INotificationRuleRepository ruleRepository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<DeleteNotificationRuleCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteNotificationRuleCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var rule = await ruleRepository.GetByIdAsync(NotificationRuleId.From(request.RuleId), tenantId, ct);

        if (rule is null)
            return Result.Failure(NotificationErrors.RuleNotFound);

        await ruleRepository.DeleteAsync(rule, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success();
    }
}

// ─────────────────────────────────────────────────────────────
//  Notification Templates
// ─────────────────────────────────────────────────────────────

public sealed class CreateNotificationTemplateCommandHandler(
    INotificationTemplateRepository templateRepository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateNotificationTemplateCommand, Result<NotificationTemplateResponse>>
{
    public async Task<Result<NotificationTemplateResponse>> HandleAsync(CreateNotificationTemplateCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var id = NotificationTemplateId.Create();

        var result = NotificationTemplate.Create(
            id, tenantId, request.TemplateKey, request.Channel,
            request.Locale, request.Subject, request.Body, request.VariablesJsonSchema);

        if (result.IsFailure)
            return Result.Failure<NotificationTemplateResponse>(result.Error);

        await templateRepository.AddAsync(result.Value, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(ToResponse(result.Value));
    }

    private static NotificationTemplateResponse ToResponse(NotificationTemplate t) => new(
        t.Id.Value, t.TenantId, t.TemplateKey, t.Channel, t.Locale,
        t.Subject, t.Body, t.VariablesJsonSchema, t.Version, t.IsActive,
        t.CreatedAtUtc, t.UpdatedAtUtc);
}

public sealed class UpdateNotificationTemplateCommandHandler(
    INotificationTemplateRepository templateRepository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<UpdateNotificationTemplateCommand, Result<NotificationTemplateResponse>>
{
    public async Task<Result<NotificationTemplateResponse>> HandleAsync(UpdateNotificationTemplateCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var template = await templateRepository.GetByIdAsync(NotificationTemplateId.From(request.TemplateId), tenantId, ct);

        if (template is null)
            return Result.Failure<NotificationTemplateResponse>(NotificationErrors.TemplateNotFound);

        template.UpdateContent(request.Subject, request.Body, request.VariablesJsonSchema);

        await templateRepository.UpdateAsync(template, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(ToResponse(template));
    }

    private static NotificationTemplateResponse ToResponse(NotificationTemplate t) => new(
        t.Id.Value, t.TenantId, t.TemplateKey, t.Channel, t.Locale,
        t.Subject, t.Body, t.VariablesJsonSchema, t.Version, t.IsActive,
        t.CreatedAtUtc, t.UpdatedAtUtc);
}

public sealed class DeleteNotificationTemplateCommandHandler(
    INotificationTemplateRepository templateRepository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<DeleteNotificationTemplateCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteNotificationTemplateCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var template = await templateRepository.GetByIdAsync(NotificationTemplateId.From(request.TemplateId), tenantId, ct);

        if (template is null)
            return Result.Failure(NotificationErrors.TemplateNotFound);

        await templateRepository.DeleteAsync(template, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success();
    }
}

// ─────────────────────────────────────────────────────────────
//  Notification Preferences
// ─────────────────────────────────────────────────────────────

public sealed class CreateNotificationPreferenceCommandHandler(
    INotificationPreferenceRepository preferenceRepository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant) : ICommandHandler<CreateNotificationPreferenceCommand, Result<NotificationPreferenceResponse>>
{
    public async Task<Result<NotificationPreferenceResponse>> HandleAsync(CreateNotificationPreferenceCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var id = NotificationPreferenceId.Create();

        var result = NotificationPreference.Create(
            id, tenantId, request.UserId, request.EventCategory,
            request.EnabledChannels, false, request.QuietHoursStart,
            request.QuietHoursEnd, request.TimeZoneId,
            request.DigestFrequency, request.Locale);

        if (result.IsFailure)
            return Result.Failure<NotificationPreferenceResponse>(result.Error);

        await preferenceRepository.AddAsync(result.Value, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(ToResponse(result.Value));
    }

    private static NotificationPreferenceResponse ToResponse(NotificationPreference p) => new(
        p.Id.Value, p.TenantId, p.UserId, p.EventCategory,
        p.EnabledChannels, p.IsMandatory, p.QuietHoursStart, p.QuietHoursEnd,
        p.TimeZoneId, p.DigestFrequency, p.Locale, p.CreatedAtUtc, p.UpdatedAtUtc);
}

public sealed class UpdateNotificationPreferenceCommandHandler(
    INotificationPreferenceRepository preferenceRepository,
    IUnitOfWork unitOfWork,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser) : ICommandHandler<UpdateNotificationPreferenceCommand, Result<NotificationPreferenceResponse>>
{
    public async Task<Result<NotificationPreferenceResponse>> HandleAsync(UpdateNotificationPreferenceCommand request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;
        var pref = await preferenceRepository.GetByIdAsync(NotificationPreferenceId.From(request.PreferenceId), tenantId, ct);

        if (pref is null)
            return Result.Failure<NotificationPreferenceResponse>(NotificationErrors.PreferenceNotFound);

        if (pref.UserId != currentUser.UserId)
            return Result.Failure<NotificationPreferenceResponse>(NotificationErrors.NotOwnedByUser);

        if (pref.IsMandatory)
            return Result.Failure<NotificationPreferenceResponse>(NotificationErrors.PreferenceMandatory);

        pref.UpdateChannels(request.EnabledChannels);
        pref.UpdateQuietHours(request.QuietHoursStart, request.QuietHoursEnd, request.TimeZoneId);
        pref.UpdateDigest(request.DigestFrequency);
        pref.UpdateLocale(request.Locale);

        await preferenceRepository.UpdateAsync(pref, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(ToResponse(pref));
    }

    private static NotificationPreferenceResponse ToResponse(NotificationPreference p) => new(
        p.Id.Value, p.TenantId, p.UserId, p.EventCategory,
        p.EnabledChannels, p.IsMandatory, p.QuietHoursStart, p.QuietHoursEnd,
        p.TimeZoneId, p.DigestFrequency, p.Locale, p.CreatedAtUtc, p.UpdatedAtUtc);
}
