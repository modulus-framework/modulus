using Meetup.Modules.Registrations.Application.Dtos;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Registrations.Application.Queries.GetRegistrations;

/// <summary>Lists registrations (read model — kgrzybek: raw-SQL view queries).</summary>
public sealed record GetRegistrationsQuery : IQuery<IReadOnlyList<UserRegistrationDto>>;
