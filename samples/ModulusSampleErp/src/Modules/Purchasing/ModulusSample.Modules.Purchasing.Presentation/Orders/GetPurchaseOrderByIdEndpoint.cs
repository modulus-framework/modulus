using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Dtos;
using ModulusSample.Modules.Purchasing.Application.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Presentation.Orders;

internal sealed class GetPurchaseOrderByIdEndpoint : Endpoint<OrderDto>
{
    private readonly IMediator _mediator;

    public GetPurchaseOrderByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/purchase-orders/{id:guid}");
        Tag("Purchasing");
        Summary("Get purchase order details");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        Result<OrderDto> result = await _mediator.QueryAsync(new GetOrderByIdQuery(id), ct);

        if (result.IsFailure || result.Value is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
