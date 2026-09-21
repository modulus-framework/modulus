using Meetup.Modules.Payments.Application.Commands.BuySubscription;
using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Payments.Presentation.Endpoints;

public sealed class BuySubscriptionRequest
{
    public string PayerLogin { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
}

public sealed class BuySubscriptionEndpoint(IMediator mediator) : Endpoint<BuySubscriptionRequest, Guid>
{
    public override void Configure()
    {
        Post("/api/payments/subscriptions");
        Permissions(PaymentsPermissions.BuySubscription);
        Summary("Buys a subscription (extends the payer's groups via integration event)");
    }

    public override async Task HandleAsync(BuySubscriptionRequest req, CancellationToken ct)
    {
        var id = await mediator.SendAsync(
            new BuySubscriptionCommand(req.PayerLogin, req.Price, req.Currency), ct);
        await SendCreatedAsync(id, $"/api/payments/subscriptions/{id}", ct);
    }
}
