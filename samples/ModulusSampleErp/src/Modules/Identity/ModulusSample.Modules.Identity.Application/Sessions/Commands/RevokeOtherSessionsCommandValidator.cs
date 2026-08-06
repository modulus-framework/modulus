using FluentValidation;
using ModulusSample.Modules.Identity.Application.Sessions.Commands;

namespace ModulusSample.Modules.Identity.Application.Sessions.Commands;

public sealed class RevokeOtherSessionsCommandValidator : AbstractValidator<RevokeOtherSessionsCommand>
{
    public RevokeOtherSessionsCommandValidator()
    {
        // No validation needed - this is a no-parameter command
    }
}
