using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Notifications.Application.Notifications.Dtos;
using TradeFlow.Modules.Notifications.Application.Notifications.Queries;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Presentation.Rules;

internal sealed class GetNotificationRulesEndpoint(
    IMediator mediator) : Endpoint<GetNotificationRulesEndpoint.GetRulesRequest, IReadOnlyList<NotificationRuleResponse>>
{
    public override void Configure()
    {
        Get("/notifications/rules");
        Tag(Tags.Notifications);
        Summary("List all notification rules for the current tenant");
    }

    public override async Task HandleAsync(GetRulesRequest req, CancellationToken ct)
    {
        Result<IReadOnlyList<NotificationRuleResponse>> result = await mediator.QueryAsync(
            new GetNotificationRulesQuery(), ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class GetRulesRequest { }
}
