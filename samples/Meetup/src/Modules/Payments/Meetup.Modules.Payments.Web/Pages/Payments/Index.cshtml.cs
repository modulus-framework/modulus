using Meetup.Modules.Payments.Application.Commands.BuySubscription;
using Meetup.Modules.Payments.Application.Dtos;
using Meetup.Modules.Payments.Application.Queries.GetSubscriptions;
using Microsoft.AspNetCore.Mvc;
using Modulus.Mediator.Abstractions;
using Modulus.UI;

namespace Meetup.Modules.Payments.Web.Pages.Payments;

/// <summary>
/// Subscriptions admin page (<c>/Payments</c>): subscription list with an
/// inline buy form over the Payments mediator handlers.
/// </summary>
public sealed class IndexModel(IMediator mediator) : HtmxPageModel
{
    private readonly IMediator _mediator = mediator;

    public IReadOnlyList<SubscriptionDto> Items { get; private set; } = [];

    [BindProperty]
    public CreateInput Input { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        return await HandleAsync(
            async () =>
            {
                await _mediator.SendAsync(new BuySubscriptionCommand(
                    Input.PayerLogin, Input.Price, Input.Currency), ct);
                await LoadAsync(ct);
            },
            "_CreateForm",
            () =>
            {
                if (!IsHtmxRequest)
                    return RedirectToPage();

                HtmxToast("Subscription purchased.");
                return HtmxPartial("_Table", Items);
            });
    }

    private async Task LoadAsync(CancellationToken ct)
        => Items = await _mediator.QueryAsync(new GetSubscriptionsQuery(), ct);

    public sealed class CreateInput
    {
        public string PayerLogin { get; set; } = string.Empty;
        public decimal Price { get; set; } = 49;
        public string Currency { get; set; } = "USD";
    }
}
