using Meetup.Modules.Registrations.Application.Dtos;
using Meetup.Modules.Registrations.Domain;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Registrations.Application.Queries.GetRegistrations;

public sealed class GetRegistrationsHandler(IUserRegistrationRepository repo)
    : IQueryHandler<GetRegistrationsQuery, IReadOnlyList<UserRegistrationDto>>
{
    public async Task<IReadOnlyList<UserRegistrationDto>> HandleAsync(
        GetRegistrationsQuery query, CancellationToken ct)
    {
        var items = await repo.GetAllAsync(ct);
        return items
            .Select(x => new UserRegistrationDto(
                x.Id, x.Login, x.Email, x.FirstName, x.LastName,
                x.Status.ToString(), x.RegisteredAt, x.ConfirmedAt))
            .ToList();
    }
}
