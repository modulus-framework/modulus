using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Identity.Application.Users.Commands;
using ModulusSample.Modules.Identity.Application.Users.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Presentation.Users;

internal sealed class RegisterUserEndpoint : Endpoint<RegisterUserEndpoint.RegisterUserRequest, RegisterUserResponse>
{
    private readonly IMediator _mediator;

    public RegisterUserEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/auth/register");
        AllowAnonymous();
        Tag(Tags.Authentication);
        Summary("Register new user");
    }

    public override async Task HandleAsync(RegisterUserRequest req, CancellationToken ct)
    {
        var command = new RegisterUserCommand(
            req.Email, req.Password, req.UserName, req.FirstName, req.LastName, req.PhoneNumber);

        Result<RegisterUserResponse> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/v1/users/{result.Value.UserId}", ct);
    }

    internal sealed class RegisterUserRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
    }
}
