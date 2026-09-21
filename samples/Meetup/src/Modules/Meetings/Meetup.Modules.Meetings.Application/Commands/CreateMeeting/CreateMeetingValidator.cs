using FluentValidation;

namespace Meetup.Modules.Meetings.Application.Commands.CreateMeeting;

public sealed class CreateMeetingValidator : AbstractValidator<CreateMeetingCommand>
{
    public CreateMeetingValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.EndUtc).GreaterThan(x => x.StartUtc);
        RuleFor(x => x.GuestsLimit).GreaterThanOrEqualTo(0);
        RuleFor(x => x.EventFee).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CreatorLogin).NotEmpty();
    }
}
