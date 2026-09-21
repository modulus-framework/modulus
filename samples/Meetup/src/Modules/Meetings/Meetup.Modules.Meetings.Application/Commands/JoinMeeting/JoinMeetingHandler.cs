using Meetup.Modules.Meetings.Domain.Entities;
using Meetup.Modules.Meetings.Domain.Repositories;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Meetings.Application.Commands.JoinMeeting;

public sealed class JoinMeetingHandler(
    IMeetingRepository meetings,
    IMeetingAttendeeRepository attendees,
    IUnitOfWork unitOfWork)
    : ICommandHandler<JoinMeetingCommand, string>
{
    public async Task<string> HandleAsync(JoinMeetingCommand command, CancellationToken ct)
    {
        var meeting = await meetings.GetByIdAsync(command.MeetingId, ct)
            ?? throw new KeyNotFoundException($"Meeting {command.MeetingId} not found.");

        var alreadyAttending = await attendees.IsAttendingAsync(command.MeetingId, command.Login, ct);
        var attendeeCount = await attendees.CountAttendeesAsync(command.MeetingId, ct);

        var attendee = MeetingAttendee.Join(
            meeting.Id, command.Login, command.GuestsCount, meeting.GuestsLimit,
            attendeeCount, meeting.AttendeesLimit, alreadyAttending);

        await attendees.AddAsync(attendee, ct);
        await unitOfWork.CommitAsync(ct);
        return attendee.Status.ToString();
    }
}
