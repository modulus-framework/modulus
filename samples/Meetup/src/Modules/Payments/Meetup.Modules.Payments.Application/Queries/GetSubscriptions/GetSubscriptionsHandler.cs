using Meetup.Modules.Payments.Application.Dtos;
using Meetup.Modules.Payments.Domain.Repositories;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Payments.Application.Queries.GetSubscriptions;

public sealed class GetSubscriptionsHandler(ISubscriptionRepository subs)
    : IQueryHandler<GetSubscriptionsQuery, IReadOnlyList<SubscriptionDto>>
{
    public async Task<IReadOnlyList<SubscriptionDto>> HandleAsync(
        GetSubscriptionsQuery query, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var items = await subs.FindByPayerAsync(query.PayerLogin, ct);
        return items
            .Select(x => new SubscriptionDto(
                x.Id, x.PayerLogin, x.ValidFrom, x.ValidTo,
                x.Price, x.Currency, x.IsActiveAt(now)))
            .ToList();
    }
}
