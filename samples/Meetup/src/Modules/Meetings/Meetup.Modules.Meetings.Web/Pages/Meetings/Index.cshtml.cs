using Meetup.Modules.Meetings.Application.Commands.CreateMeeting;
using Meetup.Modules.Meetings.Application.Commands.JoinMeeting;
using Meetup.Modules.Meetings.Application.Dtos;
using Meetup.Modules.Meetings.Application.Queries.GetMeetingGroups;
using Meetup.Modules.Meetings.Application.Queries.GetMeetings;
using Microsoft.AspNetCore.Mvc;
using Modulus.Mediator.Abstractions;
using Modulus.UI;

namespace Meetup.Modules.Meetings.Web.Pages.Meetings;

/// <summary>
/// Meetings admin page (<c>/Meetings</c>): group picker + meeting list with
/// inline create and per-row join over the Meetings mediator handlers.
/// There is no signed-in user on this companion UI, so the acting login is
/// entered per form (the API endpoints resolve it from the JWT instead).
/// </summary>
public sealed class IndexModel(IMediator mediator) : HtmxPageModel
{
    private readonly IMediator _mediator = mediator;

    public IReadOnlyList<MeetingGroupDto> Groups { get; private set; } = [];
    public IReadOnlyList<MeetingDto> Meetings { get; private set; } = [];
    public Guid? SelectedGroupId { get; private set; }

    [BindProperty]
    public CreateInput Input { get; set; } = new();

    [BindProperty]
    public JoinInput Join { get; set; } = new();

    public async Task OnGetAsync(Guid? groupId, CancellationToken ct)
        => await LoadAsync(groupId, ct);

    public async Task<IActionResult> OnGetTableAsync(Guid groupId, CancellationToken ct)
    {
        await LoadAsync(groupId, ct);
        return Partial("_MeetingsTable", this);
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        return await HandleAsync(
            async () =>
            {
                await _mediator.SendAsync(new CreateMeetingCommand(
                    Input.GroupId,
                    Input.Title,
                    Input.Description ?? string.Empty,
                    Input.StartUtc,
                    Input.EndUtc,
                    Input.AttendeesLimit,
                    Input.GuestsLimit,
                    Input.EventFee,
                    Input.EventFeeCurrency,
                    Input.CreatorLogin), ct);
                await LoadAsync(Input.GroupId, ct);
            },
            "_CreateForm",
            () =>
            {
                if (!IsHtmxRequest)
                    return RedirectToPage(new { groupId = SelectedGroupId });

                HtmxToast("Meeting created.");
                return HtmxPartial("_MeetingsTable", this);
            });
    }

    public async Task<IActionResult> OnPostJoinAsync(Guid meetingId, Guid groupId, CancellationToken ct)
    {
        return await HandleAsync(
            async () =>
            {
                var status = await _mediator.SendAsync(
                    new JoinMeetingCommand(meetingId, Join.Login, Join.GuestsCount), ct);
                HtmxToast($"Join: {status}.");
                await LoadAsync(groupId, ct);
            },
            "_MeetingsTable",
            () =>
            {
                if (!IsHtmxRequest)
                    return RedirectToPage(new { groupId });

                return HtmxPartial("_MeetingsTable", this);
            });
    }

    private async Task LoadAsync(Guid? groupId, CancellationToken ct)
    {
        Groups = await _mediator.QueryAsync(new GetMeetingGroupsQuery(), ct);
        SelectedGroupId = groupId ?? Groups.FirstOrDefault()?.Id;
        Meetings = SelectedGroupId is null
            ? []
            : await _mediator.QueryAsync(new GetMeetingsQuery(SelectedGroupId.Value), ct);
    }

    public sealed class CreateInput
    {
        public Guid GroupId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime StartUtc { get; set; } = DateTime.UtcNow.AddDays(7);
        public DateTime EndUtc { get; set; } = DateTime.UtcNow.AddDays(7).AddHours(2);
        public int? AttendeesLimit { get; set; }
        public int GuestsLimit { get; set; }
        public decimal EventFee { get; set; }
        public string EventFeeCurrency { get; set; } = "USD";
        public string CreatorLogin { get; set; } = string.Empty;
    }

    public sealed class JoinInput
    {
        public string Login { get; set; } = string.Empty;
        public int GuestsCount { get; set; }
    }
}
