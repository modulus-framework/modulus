using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Payments.Dtos;
using ModulusSample.Modules.Billing.Application.Payments.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Presentation.Payments;

internal sealed class ListPaymentsEndpoint : Endpoint<ListPaymentsEndpoint.ListPaymentsRequest, PagedResult<PaymentDto>>
{
    private readonly IMediator _mediator;

    public ListPaymentsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/payments");
        Tag("Billing");
        Summary("List all payments");
    }

    public override async Task HandleAsync(ListPaymentsRequest req, CancellationToken ct)
    {
        PagedResult<PaymentDto> result = await _mediator.QueryAsync(
            new ListPaymentsQuery(req.PageNumber, req.PageSize), ct);

        await SendOkAsync(result, ct);
    }

    internal sealed class ListPaymentsRequest
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}
