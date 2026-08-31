using FluentValidation;
using TradeFlow.Modules.Identity.Application.Sessions.Commands;

namespace TradeFlow.Modules.Identity.Application.Sessions.Commands;

public sealed class RevokeSessionCommandValidator : AbstractValidator<RevokeSessionCommand>
{
    public RevokeSessionCommandValidator()
    {
        RuleFor(x => x.SessionId)
            .NotEmpty()
            .WithMessage("Session ID is required");
    }
}
