using FluentValidation;

namespace ModulusSample.Modules.Identity.Application.Users.Commands;

internal sealed class UpdateUserTypeCommandValidator : AbstractValidator<UpdateUserTypeCommand>
{
    public UpdateUserTypeCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.UserType).IsInEnum();
    }
}
