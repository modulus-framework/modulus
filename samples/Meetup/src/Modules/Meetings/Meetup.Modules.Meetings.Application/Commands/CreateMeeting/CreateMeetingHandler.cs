using Meetup.Modules.Meetings.Domain.Entities;
using Meetup.Modules.Meetings.Domain.Repositories;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Meetings.Application.Commands.CreateMeeting;

public sealed class CreateMeetingHandler(
    IMeetingGroupRepository groups,
    IMeetingGroupMemberRepository members,
    IMeetingRepository meetings,
    IMeetingAttendeeRepository attendees,
    IUnitOfWork unitOfWork)
    : ICommandHandler<CreateMeetingCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateMeetingCommand command, CancellationToken ct)
    {
        var group = await groups.GetByIdAsync(command.GroupId, ct)
            ?? throw new KeyNotFoundException($"Meeting group {command.GroupId} not found.");

        var creatorIsOrganizer = group.CreatorLogin == command.CreatorLogin
            || await members.IsOrganizerAsync(command.GroupId, command.CreatorLogin, ct);

        // The contract promises UTC but JSON clients legitimately send
        // local-offset ISO strings (deserialized as Local/Unspecified),
        // which Npgsql rejects for timestamptz. Normalize, preserving the instant.
        var startUtc = command.StartUtc.ToUniversalTime();
        var endUtc = command.EndUtc.ToUniversalTime();

        var meeting = Meeting.Create(
            command.GroupId, command.Title, command.Description,
            startUtc, endUtc, command.AttendeesLimit,
            command.GuestsLimit, command.EventFee, command.EventFeeCurrency,
            command.CreatorLogin,
            group.IsPaidFor(DateTime.UtcNow),
            creatorIsOrganizer);

        await meetings.AddAsync(meeting, ct);

        // The creator becomes the first host (a meeting must have ≥1 host).
        await attendees.AddAsync(MeetingAttendee.Join(
            meeting.Id, command.CreatorLogin, 0, meeting.GuestsLimit,
            0, meeting.AttendeesLimit, alreadyAttending: false, isHost: true), ct);

        await unitOfWork.CommitAsync(ct);
        return meeting.Id;
    }
}
