
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Application.Sessions.Commands;

public sealed record RevokeSessionCommand(Guid SessionId) : Modulus.Mediator.Abstractions.ICommand<Result>;
