using Meetup.Modules.Registrations.Application.Commands.ConfirmRegistration;
using Meetup.Modules.Registrations.Application.Commands.RegisterNewUser;
using Meetup.Modules.Registrations.Application.Dtos;
using Meetup.Modules.Registrations.Application.Queries.GetRegistrations;
using Microsoft.AspNetCore.Mvc;
using Modulus.Mediator.Abstractions;
using Modulus.UI;

namespace Meetup.Modules.Registrations.Web.Pages.Registrations;

/// <summary>
/// Registrations admin page (<c>/Registrations</c>): registration list with
/// inline register form and per-row confirm over the Registrations mediator
/// handlers.
/// </summary>
public sealed class IndexModel(IMediator mediator) : HtmxPageModel
{
    private readonly IMediator _mediator = mediator;

    public IReadOnlyList<UserRegistrationDto> Items { get; private set; } = [];

    [BindProperty]
    public CreateInput Input { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        return await HandleAsync(
            async () =>
            {
                await _mediator.SendAsync(new RegisterNewUserCommand(
                    Input.Login, Input.Email, Input.Password,
                    Input.FirstName, Input.LastName), ct);
                await LoadAsync(ct);
            },
            "_CreateForm",
            () =>
            {
                if (!IsHtmxRequest)
                    return RedirectToPage();

                HtmxToast("Registration created.");
                return HtmxPartial("_Table", Items);
            });
    }

    public async Task<IActionResult> OnPostConfirmAsync(Guid id, CancellationToken ct)
    {
        await _mediator.SendAsync(new ConfirmRegistrationCommand(id), ct);
        await LoadAsync(ct);

        if (!IsHtmxRequest)
            return RedirectToPage();

        HtmxToast("Registration confirmed.");
        return HtmxPartial("_Table", Items);
    }

    private async Task LoadAsync(CancellationToken ct)
        => Items = await _mediator.QueryAsync(new GetRegistrationsQuery(), ct);

    public sealed class CreateInput
    {
        public string Login { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }
}
