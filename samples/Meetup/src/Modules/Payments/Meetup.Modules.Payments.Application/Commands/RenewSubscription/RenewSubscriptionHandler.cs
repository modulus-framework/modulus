using Meetup.Modules.Payments.Application.IntegrationEvents;
using Meetup.Modules.Payments.Domain.Repositories;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Payments.Application.Commands.RenewSubscription;

public sealed class RenewSubscriptionHandler(
    ISubscriptionRepository subs, IUnitOfWork unitOfWork)
    : ICommandHandler<RenewSubscriptionCommand, Guid>
{
    public async Task<Guid> HandleAsync(RenewSubscriptionCommand command, CancellationToken ct)
    {
        var subscription = await subs.FindLatestByPayerAsync(command.PayerLogin, ct)
            ?? throw new KeyNotFoundException($"No subscription found for '{command.PayerLogin}'.");

        subscription.Renew();

        // Transactional outbox: attach BEFORE CommitAsync so ModuleDbContext
        // writes the outbox row in the same DB transaction as the aggregate.
        subscription.AddIntegrationEvent(new SubscriptionPurchasedIntegrationEvent(
            subscription.Id, subscription.PayerLogin, subscription.ValidTo));

        await unitOfWork.CommitAsync(ct);

        return subscription.Id;
    }
}
