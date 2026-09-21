using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

namespace Meetup.Modules.Payments.Application.Commands.BuySubscription;

/// <summary>Buys a subscription for the payer (12 months by default).</summary>
// Single-commit write on one module context: EF's implicit SaveChanges
// transaction plus the transactional outbox suffice. [Transactional] would
// couple Application to the Infrastructure DbContext type.
[SkipTransaction]
public sealed record BuySubscriptionCommand(string PayerLogin, decimal Price, string Currency)
    : ICommand<Guid>;
