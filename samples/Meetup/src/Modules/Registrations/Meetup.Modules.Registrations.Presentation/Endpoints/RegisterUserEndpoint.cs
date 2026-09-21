using Meetup.Modules.Registrations.Application.Commands.RegisterNewUser;
using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Registrations.Presentation.Endpoints;

/// <summary>REPR endpoints for user registration (thin API — no application logic).</summary>
public sealed class RegisterUserRequest
{
    public string Login { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}

public sealed class RegisterUserEndpoint(IMediator mediator) : Endpoint<RegisterUserRequest, Guid>
{
    public override void Configure()
    {
        Post("/api/registrations");
        Permissions(RegistrationsPermissions.RegisterUser);
        Summary("Registers a new user (creates a pending registration)");
    }

    public override async Task HandleAsync(RegisterUserRequest req, CancellationToken ct)
    {
        var id = await mediator.SendAsync(
            new RegisterNewUserCommand(req.Login, req.Email, req.Password, req.FirstName, req.LastName), ct);
        await SendCreatedAsync(id, $"/api/registrations/{id}", ct);
    }
}
