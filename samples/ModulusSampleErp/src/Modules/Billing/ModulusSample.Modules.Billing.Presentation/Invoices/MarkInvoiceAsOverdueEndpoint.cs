using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Presentation.Invoices;

internal sealed class MarkInvoiceAsOverdueEndpoint : Endpoint
{
    private readonly IMediator _mediator;

    public MarkInvoiceAsOverdueEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/invoices/{id:guid}/overdue");
        Tag("Billing");
        Summary("Mark an invoice as overdue");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var command = new MarkInvoiceAsOverdueCommand(id);
        Result result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendOkAsync(ct);
    }
}
