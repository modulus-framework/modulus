using FluentValidation;
using ProcureFlow.Modules.Identity.Application.Sessions.Commands;

namespace ProcureFlow.Modules.Identity.Application.Sessions.Commands;

public sealed class RevokeSessionCommandValidator : AbstractValidator<RevokeSessionCommand>
{
    public RevokeSessionCommandValidator()
    {
        RuleFor(x => x.SessionId)
            .NotEmpty()
            .WithMessage("Session ID is required");
    }
}
