using FluentValidation;

namespace Meetup.Modules.Registrations.Application.Commands.RegisterNewUser;

/// <summary>Input validation (kgrzybek: ValidationCommandHandlerDecorator).</summary>
public sealed class RegisterNewUserValidator : AbstractValidator<RegisterNewUserCommand>
{
    public RegisterNewUserValidator()
    {
        RuleFor(x => x.Login).NotEmpty().MinimumLength(3).MaximumLength(50);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(255);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6).MaximumLength(100);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
    }
}
