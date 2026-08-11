using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Commands;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Billing.Presentation.Invoices;

internal sealed class CreateInvoiceEndpoint : Endpoint<CreateInvoiceCommand, Guid>
{
    private readonly IMediator _mediator;

    public CreateInvoiceEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/invoices");
        Tag("Billing");
        Summary("Create a new invoice");
    }

    public override async Task HandleAsync(CreateInvoiceCommand req, CancellationToken ct)
    {
        Result<Guid> result = await _mediator.SendAsync(req, ct);

        if (result.IsFailure)
        {
            await SendAsync(null, statusCode: StatusCodes.Status400BadRequest, cancellation: ct);
            return;
        }

        await SendCreatedAsync($"/api/invoices/{result.Value}", ct);
    }
}
