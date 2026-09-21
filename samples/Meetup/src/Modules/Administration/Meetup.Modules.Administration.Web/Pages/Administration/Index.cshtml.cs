using Meetup.Modules.Administration.Application.Commands.AcceptProposal;
using Meetup.Modules.Administration.Application.Commands.ProposeMeetingGroup;
using Meetup.Modules.Administration.Application.Commands.RejectProposal;
using Meetup.Modules.Administration.Application.Dtos;
using Meetup.Modules.Administration.Application.Queries.GetProposals;
using Microsoft.AspNetCore.Mvc;
using Modulus.Mediator.Abstractions;
using Modulus.UI;

namespace Meetup.Modules.Administration.Web.Pages.Administration;

/// <summary>
/// Proposals admin page (<c>/Administration</c>): meeting-group proposal
/// list with an inline propose form and per-row accept/reject over the
/// Administration mediator handlers. Accepting notifies Meetings via
/// integration event, which creates the group.
/// </summary>
public sealed class IndexModel(IMediator mediator) : HtmxPageModel
{
    private readonly IMediator _mediator = mediator;

    public IReadOnlyList<MeetingGroupProposalDto> Items { get; private set; } = [];

    [BindProperty]
    public CreateInput Input { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        return await HandleAsync(
            async () =>
            {
                await _mediator.SendAsync(new ProposeMeetingGroupCommand(
                    Input.Name,
                    Input.Description ?? string.Empty,
                    Input.City,
                    Input.CountryCode,
                    Input.ProposerLogin), ct);
                await LoadAsync(ct);
            },
            "_CreateForm",
            () =>
            {
                if (!IsHtmxRequest)
                    return RedirectToPage();

                HtmxToast("Proposal submitted.");
                return HtmxPartial("_Table", Items);
            });
    }

    public async Task<IActionResult> OnPostAcceptAsync(Guid id, CancellationToken ct)
    {
        await _mediator.SendAsync(new AcceptProposalCommand(id), ct);
        await LoadAsync(ct);

        if (!IsHtmxRequest)
            return RedirectToPage();

        HtmxToast("Proposal accepted — the group is being created.");
        return HtmxPartial("_Table", Items);
    }

    public async Task<IActionResult> OnPostRejectAsync(Guid id, CancellationToken ct)
    {
        await _mediator.SendAsync(new RejectProposalCommand(id), ct);
        await LoadAsync(ct);

        if (!IsHtmxRequest)
            return RedirectToPage();

        HtmxToast("Proposal rejected.");
        return HtmxPartial("_Table", Items);
    }

    private async Task LoadAsync(CancellationToken ct)
        => Items = await _mediator.QueryAsync(new GetProposalsQuery(), ct);

    public sealed class CreateInput
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string City { get; set; } = string.Empty;
        public string CountryCode { get; set; } = "US";
        public string ProposerLogin { get; set; } = string.Empty;
    }
}
