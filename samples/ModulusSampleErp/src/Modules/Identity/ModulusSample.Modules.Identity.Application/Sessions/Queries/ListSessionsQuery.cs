using ModulusSample.Modules.Identity.Application.Sessions.Dtos;

using ModulusSample.Shared.Domain;
namespace ModulusSample.Modules.Identity.Application.Sessions.Queries;

public sealed record ListSessionsQuery() : Modulus.Mediator.Abstractions.IQuery<Result<List<SessionDto>>>;
