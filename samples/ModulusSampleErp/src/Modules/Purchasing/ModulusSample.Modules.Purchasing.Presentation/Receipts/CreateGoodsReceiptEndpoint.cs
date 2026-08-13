using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Receipts.Commands;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Purchasing.Presentation.Receipts;

internal sealed class CreateGoodsReceiptEndpoint : Endpoint<CreateGoodsReceiptEndpoint.CreateReceiptRequest, Guid>
{
    private readonly IMediator _mediator;

    public CreateGoodsReceiptEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/goods-receipts");
        Tag("Purchasing");
        Summary("Create a new goods receipt");
    }

    public override async Task HandleAsync(CreateReceiptRequest req, CancellationToken ct)
    {
        var command = new CreateGoodsReceiptCommand(req.ReceiptNumber, req.PurchaseOrderId, req.OrgUnitId);
        Result<Guid> result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/goods-receipts/{result.Value}", ct);
    }

    internal sealed class CreateReceiptRequest
    {
        public string ReceiptNumber { get; set; } = string.Empty;
        public Guid PurchaseOrderId { get; set; }
        public Guid OrgUnitId { get; set; }
    }
}
