using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Dtos;
using ModulusSample.Modules.Purchasing.Application.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Purchasing.Presentation.Receipts;

internal sealed class GetGoodsReceiptByIdEndpoint : Endpoint<ReceiptDto>
{
    private readonly IMediator _mediator;

    public GetGoodsReceiptByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/goods-receipts/{id:guid}");
        Tag("Purchasing");
        Summary("Get goods receipt details");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        Result<ReceiptDto> result = await _mediator.QueryAsync(new GetReceiptByIdQuery(id), ct);

        if (result.IsFailure || result.Value is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
