using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Identity.Application.Permissions.Dtos;
using ProcureFlow.Modules.Identity.Application.Permissions.Queries;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Presentation.Admin;

internal sealed class GetPermissionByCodeEndpoint : Endpoint<GetPermissionByCodeEndpoint.GetPermissionByCodeRequest, PermissionResponse>
{
    private readonly IMediator _mediator;

    public GetPermissionByCodeEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/admin/permissions/{code}");
        Tag(Tags.AdminUsers);
        Summary("Get permission by code (Admin)");
    }

    public override async Task HandleAsync(GetPermissionByCodeRequest req, CancellationToken ct)
    {
        Result<PermissionResponse> result = await _mediator.QueryAsync(new GetPermissionByCodeQuery(req.Code), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetPermissionByCodeRequest
    {
        public string Code { get; set; } = string.Empty;
    }
}
