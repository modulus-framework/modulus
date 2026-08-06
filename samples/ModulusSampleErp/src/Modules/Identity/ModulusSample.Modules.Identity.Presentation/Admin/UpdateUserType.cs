using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Identity.Application.Users.Commands;
using ModulusSample.Modules.Identity.Domain.Enums;
using ModulusSample.Shared.Domain;
using Microsoft.AspNetCore.Http;

namespace ModulusSample.Modules.Identity.Presentation.Admin;

internal sealed class UpdateUserTypeEndpoint : Endpoint<UpdateUserTypeEndpoint.UpdateUserTypeRequest>
{
    private readonly IMediator _mediator;

    public UpdateUserTypeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Put("/admin/users/{userId:guid}/type");
        Tag(Tags.AdminUsers);
        Summary("Update user type");
    }

    public override async Task HandleAsync(UpdateUserTypeRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<UserType>(req.UserType, ignoreCase: true, out var userType))
        {
            await SendErrorAsync(StatusCodes.Status400BadRequest, $"'{req.UserType}' is not a valid UserType.", ct);
            return;
        }

        Result result = await _mediator.SendAsync(new UpdateUserTypeCommand(req.UserId, userType), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class UpdateUserTypeRequest
    {
        public Guid UserId { get; set; }
        public string UserType { get; set; } = string.Empty;
    }
}
