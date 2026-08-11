using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Presentation.Invoices;

internal sealed class SendInvoiceEndpoint : Endpoint
{
    private readonly IMediator _mediator;

    public SendInvoiceEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/invoices/{id:guid}/send");
        Tag("Billing");
        Summary("Send an invoice to customer");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var command = new SendInvoiceCommand(id);
        Result result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendOkAsync(ct);
    }
}
