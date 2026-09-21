using Meetup.Modules.UserAccess.Application.Commands.DeactivateUser;
using Meetup.Modules.UserAccess.Application.Dtos;
using Meetup.Modules.UserAccess.Application.Queries.GetUsers;
using Microsoft.AspNetCore.Mvc;
using Modulus.Mediator.Abstractions;
using Modulus.UI;

namespace Meetup.Modules.UserAccess.Web.Pages.UserAccess;

/// <summary>
/// Users admin page (<c>/UserAccess</c>): system-user list with per-row
/// deactivate over the UserAccess mediator handlers. Users are created by
/// the Registrations flow, so this page has no create form.
/// </summary>
public sealed class IndexModel(IMediator mediator) : HtmxPageModel
{
    private readonly IMediator _mediator = mediator;

    public IReadOnlyList<UserDto> Items { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostDeactivateAsync(Guid id, CancellationToken ct)
    {
        await _mediator.SendAsync(new DeactivateUserCommand(id), ct);
        await LoadAsync(ct);

        if (!IsHtmxRequest)
            return RedirectToPage();

        HtmxToast("User deactivated.");
        return HtmxPartial("_Table", Items);
    }

    private async Task LoadAsync(CancellationToken ct)
        => Items = await _mediator.QueryAsync(new GetUsersQuery(), ct);
}
