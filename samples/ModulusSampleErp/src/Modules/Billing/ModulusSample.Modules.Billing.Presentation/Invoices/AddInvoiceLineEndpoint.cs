using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Presentation.Invoices;

internal sealed class AddInvoiceLineEndpoint : Endpoint<AddInvoiceLineCommand>
{
    private readonly IMediator _mediator;

    public AddInvoiceLineEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/invoices/{id:guid}/lines");
        Tag("Billing");
        Summary("Add a line item to an invoice");
    }

    public override async Task HandleAsync(AddInvoiceLineCommand req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var command = req with { InvoiceId = id };
        Result result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendOkAsync(ct);
    }
}
