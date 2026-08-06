using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Identity.Application.Roles.Dtos;
using ModulusSample.Modules.Identity.Application.Roles.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Presentation.Roles;

internal sealed class GetRoleByIdEndpoint : Endpoint<GetRoleByIdEndpoint.GetRoleByIdRequest, RoleDetailResponse>
{
    private readonly IMediator _mediator;

    public GetRoleByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/roles/{id:guid}");
        Tag(Tags.Roles);
        Summary("Get role by ID");
    }

    public override async Task HandleAsync(GetRoleByIdRequest req, CancellationToken ct)
    {
        Result<RoleDetailResponse> result = await _mediator.QueryAsync(new GetRoleByIdQuery(req.Id), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetRoleByIdRequest
    {
        public Guid Id { get; set; }
    }
}
