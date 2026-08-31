using FluentValidation;
using TradeFlow.Modules.Notifications.Application.Notifications.Commands;

namespace TradeFlow.Modules.Notifications.Application.Notifications.Validators;

// ── Rule Validators ──

public sealed class CreateNotificationRuleCommandValidator : AbstractValidator<CreateNotificationRuleCommand>
{
    public CreateNotificationRuleCommandValidator()
    {
        RuleFor(x => x.EventKey)
            .NotEmpty()
            .MaximumLength(200);
        RuleFor(x => x.AudienceJson)
            .NotEmpty();
        RuleFor(x => x.Channels)
            .NotEqual(Domain.ValueObjects.NotificationChannel.None);
        RuleFor(x => x.Severity)
            .IsInEnum();
    }
}

public sealed class UpdateNotificationRuleCommandValidator : AbstractValidator<UpdateNotificationRuleCommand>
{
    public UpdateNotificationRuleCommandValidator()
    {
        RuleFor(x => x.RuleId)
            .NotEmpty();
        RuleFor(x => x.AudienceJson)
            .NotEmpty();
        RuleFor(x => x.Channels)
            .NotEqual(Domain.ValueObjects.NotificationChannel.None);
        RuleFor(x => x.Severity)
            .IsInEnum();
    }
}

// ── Template Validators ──

public sealed class CreateNotificationTemplateCommandValidator : AbstractValidator<CreateNotificationTemplateCommand>
{
    public CreateNotificationTemplateCommandValidator()
    {
        RuleFor(x => x.TemplateKey)
            .NotEmpty()
            .MaximumLength(200);
        RuleFor(x => x.Channel)
            .IsInEnum();
        RuleFor(x => x.Locale)
            .NotEmpty()
            .MaximumLength(10);
        RuleFor(x => x.Body)
            .NotEmpty();
    }
}

public sealed class UpdateNotificationTemplateCommandValidator : AbstractValidator<UpdateNotificationTemplateCommand>
{
    public UpdateNotificationTemplateCommandValidator()
    {
        RuleFor(x => x.TemplateId)
            .NotEmpty();
        RuleFor(x => x.Body)
            .NotEmpty();
    }
}

// ── Preference Validators ──

public sealed class CreateNotificationPreferenceCommandValidator : AbstractValidator<CreateNotificationPreferenceCommand>
{
    public CreateNotificationPreferenceCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();
        RuleFor(x => x.EventCategory)
            .NotEmpty()
            .MaximumLength(200);
    }
}

public sealed class UpdateNotificationPreferenceCommandValidator : AbstractValidator<UpdateNotificationPreferenceCommand>
{
    public UpdateNotificationPreferenceCommandValidator()
    {
        RuleFor(x => x.PreferenceId)
            .NotEmpty();
    }
}

// ── Event Processing Validator ──

public sealed class ProcessNotificationEventCommandValidator : AbstractValidator<ProcessNotificationEventCommand>
{
    public ProcessNotificationEventCommandValidator()
    {
        RuleFor(x => x.EventKey)
            .NotEmpty()
            .MaximumLength(200);
    }
}
