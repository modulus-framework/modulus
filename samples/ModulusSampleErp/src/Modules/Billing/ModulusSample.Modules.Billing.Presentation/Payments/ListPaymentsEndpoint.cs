using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Dtos;
using ModulusSample.Modules.Billing.Application.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Presentation.Payments;

internal sealed class ListPaymentsEndpoint : Endpoint<PagedResult<PaymentDto>>
{
    private readonly IMediator _mediator;

    public ListPaymentsEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/payments");
        Tag("Billing");
        Summary("List all payments");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        int page = Query<int>("pageNumber", 1);
        int pageSize = Query<int>("pageSize", 10);

        Result<PagedResult<PaymentDto>> result = await _mediator.QueryAsync(new ListPaymentsQuery(page, pageSize), ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
