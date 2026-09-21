using Meetup.Modules.Payments.Application.Dtos;
using Meetup.Modules.Payments.Application.Queries.GetSubscriptions;
using Meetup.Shared.Presentation;
using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Payments.Presentation.Endpoints;

public sealed class GetSubscriptionsEndpoint(IMediator mediator)
    : EndpointWithoutRequest<IReadOnlyList<SubscriptionDto>>
{
    public override void Configure()
    {
        Get("/api/payments/subscriptions");
        Roles(MeetupRoles.Admins);
        Permissions(PaymentsPermissions.ViewPayments);
        Summary("Lists subscriptions");
    }

    protected override async Task HandleAsync(CancellationToken ct)
    {
        var items = await mediator.QueryAsync(new GetSubscriptionsQuery(), ct);
        await SendOkAsync(items, ct);
    }
}
