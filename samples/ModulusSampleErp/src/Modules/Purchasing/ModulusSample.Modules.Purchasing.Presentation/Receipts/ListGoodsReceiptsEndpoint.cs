using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Dtos;
using ModulusSample.Modules.Purchasing.Application.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Presentation.Receipts;

internal sealed class ListGoodsReceiptsEndpoint : Endpoint<PagedResult<ReceiptDto>>
{
    private readonly IMediator _mediator;

    public ListGoodsReceiptsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/goods-receipts");
        Tag("Purchasing");
        Summary("List all goods receipts");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        int page = Query<int>("page", 1);
        int pageSize = Query<int>("pageSize", 10);

        Result<PagedResult<ReceiptDto>> result = await _mediator.QueryAsync(new ListReceiptsQuery(page, pageSize), ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
