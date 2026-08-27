using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Purchasing.Application.Requisitions.Dtos;
using ModulusSample.Modules.Purchasing.Application.Requisitions.Queries;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Purchasing.Presentation.Requisitions;

internal sealed class ListRequisitionsEndpoint : Endpoint<ListRequisitionsEndpoint.ListRequisitionsRequest, PagedResult<PurchaseRequisitionDto>>
{
    private readonly IMediator _mediator;

    public ListRequisitionsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/purchase-requisitions");
        Tag("Purchasing");
        Summary("List all purchase requisitions");
    }

    public override async Task HandleAsync(ListRequisitionsRequest req, CancellationToken ct)
    {
        Result<PagedResult<PurchaseRequisitionDto>> result = await _mediator.QueryAsync(new ListRequisitionsQuery(req.PageNumber, req.PageSize), ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class ListRequisitionsRequest
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}
