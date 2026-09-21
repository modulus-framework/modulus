using FluentValidation;

namespace Meetup.Modules.Administration.Application.Commands.ProposeMeetingGroup;

public sealed class ProposeMeetingGroupValidator : AbstractValidator<ProposeMeetingGroupCommand>
{
    public ProposeMeetingGroupValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CountryCode).NotEmpty().Length(2);
        RuleFor(x => x.ProposerLogin).NotEmpty();
    }
}
