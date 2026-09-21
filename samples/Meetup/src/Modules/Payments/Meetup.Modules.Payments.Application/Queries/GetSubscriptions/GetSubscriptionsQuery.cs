using Meetup.Modules.Payments.Application.Dtos;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Payments.Application.Queries.GetSubscriptions;

public sealed record GetSubscriptionsQuery(string? PayerLogin = null)
    : IQuery<IReadOnlyList<SubscriptionDto>>;
