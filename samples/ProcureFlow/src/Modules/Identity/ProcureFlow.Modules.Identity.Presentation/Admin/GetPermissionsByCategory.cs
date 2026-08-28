using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Identity.Application.Permissions.Dtos;
using ProcureFlow.Modules.Identity.Application.Permissions.Queries;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Presentation.Admin;

internal sealed class GetPermissionsByCategoryEndpoint : Endpoint<GetPermissionsByCategoryEndpoint.GetPermissionsByCategoryRequest, PermissionCategoryResponse>
{
    private readonly IMediator _mediator;

    public GetPermissionsByCategoryEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/admin/permissions/by-category/{category}");
        Tag(Tags.AdminUsers);
        Summary("Get permissions by category (Admin)");
    }

    public override async Task HandleAsync(GetPermissionsByCategoryRequest req, CancellationToken ct)
    {
        Result<PermissionCategoryResponse> result = await _mediator.QueryAsync(new GetPermissionsByCategoryQuery(req.Category), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetPermissionsByCategoryRequest
    {
        public string Category { get; set; } = string.Empty;
    }
}
