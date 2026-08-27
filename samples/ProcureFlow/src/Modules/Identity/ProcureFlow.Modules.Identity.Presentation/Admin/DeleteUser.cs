using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Identity.Application.Users.Commands;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Presentation.Admin;

internal sealed class DeleteUserEndpoint : Endpoint<DeleteUserEndpoint.DeleteUserRequest>
{
    private readonly IMediator _mediator;

    public DeleteUserEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Delete("/admin/users/{userId:guid}");
        Tag(Tags.AdminUsers);
        Summary("Delete user account");
    }

    public override async Task HandleAsync(DeleteUserRequest req, CancellationToken ct)
    {
        Result result = await _mediator.SendAsync(new DeleteUserCommand(req.UserId, req.Reason), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendNoContentAsync(ct);
    }

    internal sealed class DeleteUserRequest
    {
        public Guid UserId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}
