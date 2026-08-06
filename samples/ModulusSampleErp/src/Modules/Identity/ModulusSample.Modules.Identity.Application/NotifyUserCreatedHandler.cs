using Modulus.Core.Abstractions.Common;
using Modulus.Mediator.Abstractions;

namespace ModulusSample.Modules.Identity.Application;

/// <summary>Handler for NotifyUserCreatedCommand</summary>
public sealed class NotifyUserCreatedHandler
    : ICommandHandler<NotifyUserCreatedCommand, Unit>
{
    public async Task<Unit> HandleAsync(
        NotifyUserCreatedCommand command,
        CancellationToken ct)
    {
        // TODO: Implement NotifyUserCreated logic here
        await Task.CompletedTask;
        return Unit.Value;
    }
}
