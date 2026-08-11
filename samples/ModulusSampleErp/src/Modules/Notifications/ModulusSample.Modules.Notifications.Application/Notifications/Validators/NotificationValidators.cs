using FluentValidation;
using ModulusSample.Modules.Notifications.Application.Notifications.Commands;

namespace ModulusSample.Modules.Notifications.Application.Notifications.Validators;

public sealed class CreateNotificationCommandValidator : AbstractValidator<CreateNotificationCommand>
{
    public CreateNotificationCommandValidator()
    {
        RuleFor(x => x.RecipientUserId)
            .NotEmpty();
        RuleFor(x => x.Title)
            .NotEmpty()
            .MaximumLength(255);
        RuleFor(x => x.Message)
            .NotEmpty();
    }
}
