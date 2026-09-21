using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

namespace Meetup.Modules.Payments.Application.Commands.RenewSubscription;

/// <summary>Renews the payer's latest subscription.</summary>
// Single-commit write on one module context: EF's implicit SaveChanges
// transaction plus the transactional outbox suffice. [Transactional] would
// couple Application to the Infrastructure DbContext type.
[SkipTransaction]
public sealed record RenewSubscriptionCommand(string PayerLogin) : ICommand<Guid>;
