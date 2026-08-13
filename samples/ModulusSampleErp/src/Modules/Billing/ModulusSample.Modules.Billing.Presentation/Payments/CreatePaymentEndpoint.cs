using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Billing.Application.Payments.Commands;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Presentation;

namespace ModulusSample.Modules.Billing.Presentation.Payments;

internal sealed class CreatePaymentEndpoint : Endpoint<CreatePaymentEndpoint.CreatePaymentRequest, Guid>
{
    private readonly IMediator _mediator;

    public CreatePaymentEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Post("/api/payments");
        Tag("Billing");
        Summary("Create a new payment");
    }

    public override async Task HandleAsync(CreatePaymentRequest req, CancellationToken ct)
    {
        Result<Guid> result = await _mediator.SendAsync(
            new CreatePaymentCommand(req.PaymentNumber, req.InvoiceId, req.Amount, req.PaymentMethod), ct);

        if (result.IsFailure)
        {
            await EndpointFailure.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendCreatedAsync(result.Value, $"/api/payments/{result.Value}", ct);
    }

    internal sealed class CreatePaymentRequest
    {
        public string PaymentNumber { get; set; } = string.Empty;
        public Guid InvoiceId { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
    }
}
