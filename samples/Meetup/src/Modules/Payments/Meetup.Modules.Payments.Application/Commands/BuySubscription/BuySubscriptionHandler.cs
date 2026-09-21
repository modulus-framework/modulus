using Meetup.Modules.Payments.Application.IntegrationEvents;
using Meetup.Modules.Payments.Domain.Entities;
using Meetup.Modules.Payments.Domain.Repositories;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Payments.Application.Commands.BuySubscription;

public sealed class BuySubscriptionHandler(
    ISubscriptionRepository subs, IUnitOfWork unitOfWork)
    : ICommandHandler<BuySubscriptionCommand, Guid>
{
    public async Task<Guid> HandleAsync(BuySubscriptionCommand command, CancellationToken ct)
    {
        var subscription = Subscription.Purchase(command.PayerLogin, command.Price, command.Currency);

        // Transactional outbox: attach BEFORE CommitAsync so ModuleDbContext
        // writes the outbox row in the same DB transaction as the aggregate.
        subscription.AddIntegrationEvent(new SubscriptionPurchasedIntegrationEvent(
            subscription.Id, subscription.PayerLogin, subscription.ValidTo));

        await subs.AddAsync(subscription, ct);
        await unitOfWork.CommitAsync(ct);

        return subscription.Id;
    }
}
