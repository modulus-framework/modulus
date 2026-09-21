using Meetup.Modules.Registrations.Application.Dtos;
using Meetup.Modules.Registrations.Application.Queries.GetRegistrations;
using Meetup.Shared.Presentation;
using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Registrations.Presentation.Endpoints;

public sealed class GetRegistrationsEndpoint(IMediator mediator)
    : EndpointWithoutRequest<IReadOnlyList<UserRegistrationDto>>
{
    public override void Configure()
    {
        Get("/api/registrations");
        Roles(MeetupRoles.Admins);
        Permissions(RegistrationsPermissions.ViewRegistrations);
        Summary("Lists user registrations");
    }

    protected override async Task HandleAsync(CancellationToken ct)
    {
        var items = await mediator.QueryAsync(new GetRegistrationsQuery(), ct);
        await SendOkAsync(items, ct);
    }
}
