using Meetup.Modules.Meetings.Domain.Repositories;
using Meetup.Modules.Payments.Application.IntegrationEvents;
using Modulus.Events.Abstractions;

namespace Meetup.Modules.Meetings.Application.IntegrationEventHandlers;

/// <summary>Extends group payment coverage when the organizer's subscription is purchased.</summary>
public sealed class ExtendGroupPaymentOnSubscriptionHandler(
    IMeetingGroupRepository groups, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<SubscriptionPurchasedIntegrationEvent>
{
    public async Task HandleAsync(SubscriptionPurchasedIntegrationEvent @event, CancellationToken ct)
    {
        var owned = await groups.FindByCreatorAsync(@event.PayerLogin, ct);

        foreach (var group in owned)
        {
            group.MarkAsPaid(@event.ValidTo);
        }

        if (owned.Count > 0)
        {
            await unitOfWork.CommitAsync(ct);
        }
    }
}
