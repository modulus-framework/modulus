
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Application.Sessions.Commands;

public sealed record RevokeOtherSessionsCommand() : Modulus.Mediator.Abstractions.ICommand<Result<RevokeOtherSessionsResponse>>;

public sealed record RevokeOtherSessionsResponse(
    int RevokedCount,
    string Message);
