using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Presentation.Invoices;

internal sealed class MarkInvoiceAsPaidEndpoint : Endpoint
{
    private readonly IMediator _mediator;

    public MarkInvoiceAsPaidEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/invoices/{id:guid}/pay");
        Tag("Billing");
        Summary("Mark an invoice as paid");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var command = new MarkInvoiceAsPaidCommand(id);
        Result result = await _mediator.SendAsync(command, ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendOkAsync(ct);
    }
}
