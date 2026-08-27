using FluentValidation;
using ProcureFlow.Modules.Identity.Application.Sessions.Commands;

namespace ProcureFlow.Modules.Identity.Application.Sessions.Commands;

public sealed class RevokeOtherSessionsCommandValidator : AbstractValidator<RevokeOtherSessionsCommand>
{
    public RevokeOtherSessionsCommandValidator()
    {
        // No validation needed - this is a no-parameter command
    }
}
