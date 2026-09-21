using Modulus.Core.Abstractions.Domain;
using Modulus.Events.Abstractions;

namespace Meetup.Modules.Payments.Application.IntegrationEvents;

/// <summary>Published when a subscription is purchased or renewed (→ Meetings extends group coverage).</summary>
public sealed record SubscriptionPurchasedIntegrationEvent(
    Guid SubscriptionId,
    string PayerLogin,
    DateTime ValidTo)
    : IntegrationEventBase("payments.subscription-purchased.v1"), IDomainEvent;
