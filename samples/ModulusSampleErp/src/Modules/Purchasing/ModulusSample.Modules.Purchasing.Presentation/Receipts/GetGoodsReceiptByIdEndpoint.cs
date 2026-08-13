using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Receipts.Dtos;
using ModulusSample.Modules.Purchasing.Application.Receipts.Queries;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Purchasing.Presentation.Receipts;

internal sealed class GetGoodsReceiptByIdEndpoint : Endpoint<GetGoodsReceiptByIdEndpoint.GetReceiptByIdRequest, GoodsReceiptDto>
{
    private readonly IMediator _mediator;

    public GetGoodsReceiptByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/goods-receipts/{id:guid}");
        Tag("Purchasing");
        Summary("Get goods receipt details");
    }

    public override async Task HandleAsync(GetReceiptByIdRequest req, CancellationToken ct)
    {
        Result<GoodsReceiptDto?> result = await _mediator.QueryAsync(new GetReceiptByIdQuery(req.Id), ct);

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

    internal sealed class GetReceiptByIdRequest
    {
        public Guid Id { get; set; }
    }
}
