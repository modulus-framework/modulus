using Meetup.Modules.Registrations.Application.Commands.ConfirmRegistration;
using Meetup.Shared.Presentation;
using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Registrations.Presentation.Endpoints;

public sealed class ConfirmRegistrationRequest
{
    public Guid Id { get; set; }
}

public sealed class ConfirmRegistrationEndpoint(IMediator mediator) : Endpoint<ConfirmRegistrationRequest>
{
    public override void Configure()
    {
        Post("/api/registrations/{id}/confirm");
        Roles(MeetupRoles.Admins);
        Permissions(RegistrationsPermissions.ConfirmRegistration);
        Summary("Confirms a pending registration");
    }

    public override async Task HandleAsync(ConfirmRegistrationRequest req, CancellationToken ct)
    {
        await mediator.SendAsync(new ConfirmRegistrationCommand(req.Id), ct);
        await SendNoContentAsync(ct);
    }
}
