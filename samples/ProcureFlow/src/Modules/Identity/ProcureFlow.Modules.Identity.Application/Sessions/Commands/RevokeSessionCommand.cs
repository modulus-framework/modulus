
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Application.Sessions.Commands;

public sealed record RevokeSessionCommand(Guid SessionId) : Modulus.Mediator.Abstractions.ICommand<Result>;
