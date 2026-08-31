using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Identity.Application.Users.Dtos;
using TradeFlow.Modules.Identity.Application.Users.Queries;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Presentation.Admin;

internal sealed class GetAdminUserByIdEndpoint : Endpoint<GetAdminUserByIdEndpoint.GetAdminUserByIdRequest, AdminUserDetailResponse>
{
    private readonly IMediator _mediator;

    public GetAdminUserByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/admin/users/{userId:guid}");
        Tag(Tags.AdminUsers);
        Summary("Get user by ID (Admin)");
    }

    public override async Task HandleAsync(GetAdminUserByIdRequest req, CancellationToken ct)
    {
        Result<AdminUserDetailResponse> result = await _mediator.QueryAsync(new GetAdminUserByIdQuery(req.UserId), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetAdminUserByIdRequest
    {
        public Guid UserId { get; set; }
    }
}
