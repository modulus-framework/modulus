using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Orders.Dtos;
using ModulusSample.Modules.Purchasing.Application.Orders.Queries;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Purchasing.Presentation.Orders;

internal sealed class GetPurchaseOrderByIdEndpoint : Endpoint<GetPurchaseOrderByIdEndpoint.GetOrderByIdRequest, PurchaseOrderDto>
{
    private readonly IMediator _mediator;

    public GetPurchaseOrderByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/purchase-orders/{id:guid}");
        Tag("Purchasing");
        Summary("Get purchase order details");
    }

    public override async Task HandleAsync(GetOrderByIdRequest req, CancellationToken ct)
    {
        Result<PurchaseOrderDto?> result = await _mediator.QueryAsync(new GetOrderByIdQuery(req.Id), ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        if (result.Value is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetOrderByIdRequest
    {
        public Guid Id { get; set; }
    }
}
