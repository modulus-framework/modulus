using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Identity.Application.Permissions.Dtos;
using ProcureFlow.Modules.Identity.Application.Permissions.Queries;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Presentation.Admin;

internal sealed class GetPermissionsEndpoint : EndpointWithoutRequest<PermissionListResponse>
{
    private readonly IMediator _mediator;

    public GetPermissionsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/admin/permissions");
        Tag(Tags.AdminUsers);
        Summary("Get all permissions (Admin)");
    }

    protected override async Task HandleAsync(CancellationToken ct)
    {
        Result<PermissionListResponse> result = await _mediator.QueryAsync(new GetPermissionsQuery(), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
