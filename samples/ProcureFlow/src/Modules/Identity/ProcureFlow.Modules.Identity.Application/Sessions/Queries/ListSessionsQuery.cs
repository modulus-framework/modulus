using ProcureFlow.Modules.Identity.Application.Sessions.Dtos;

using ProcureFlow.Shared.Domain;
namespace ProcureFlow.Modules.Identity.Application.Sessions.Queries;

public sealed record ListSessionsQuery() : Modulus.Mediator.Abstractions.IQuery<Result<List<SessionDto>>>;
