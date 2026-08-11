using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Dtos;
using ModulusSample.Modules.Billing.Application.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Presentation.Invoices;

internal sealed class GetInvoiceByIdEndpoint : Endpoint<InvoiceDto>
{
    private readonly IMediator _mediator;

    public GetInvoiceByIdEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/api/invoices/{id:guid}");
        Tag("Billing");
        Summary("Get invoice details");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        Result<InvoiceDto> result = await _mediator.QueryAsync(new GetInvoiceByIdQuery(id), ct);

        if (result.IsFailure || result.Value is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }
}
