using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Receipts.Dtos;
using ModulusSample.Modules.Purchasing.Application.Receipts.Queries;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Purchasing.Presentation.Receipts;

internal sealed class ListGoodsReceiptsEndpoint : Endpoint<ListGoodsReceiptsEndpoint.ListReceiptsRequest, PagedResult<GoodsReceiptDto>>
{
    private readonly IMediator _mediator;

    public ListGoodsReceiptsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/goods-receipts");
        Tag("Purchasing");
        Summary("List all goods receipts");
    }

    public override async Task HandleAsync(ListReceiptsRequest req, CancellationToken ct)
    {
        Result<PagedResult<GoodsReceiptDto>> result = await _mediator.QueryAsync(new ListReceiptsQuery(req.PageNumber, req.PageSize), ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class ListReceiptsRequest
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}
