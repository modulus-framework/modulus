
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Application.Sessions.Commands;

public sealed record RevokeOtherSessionsCommand() : Modulus.Mediator.Abstractions.ICommand<Result<RevokeOtherSessionsResponse>>;

public sealed record RevokeOtherSessionsResponse(
    int RevokedCount,
    string Message);
