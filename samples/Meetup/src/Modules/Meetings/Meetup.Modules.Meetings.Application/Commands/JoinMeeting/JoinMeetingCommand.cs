using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

namespace Meetup.Modules.Meetings.Application.Commands.JoinMeeting;

/// <summary>Joins a meeting as attendee (or waitlist when the limit is reached).</summary>
// Single-commit write on one module context: EF's implicit SaveChanges
// transaction plus the transactional outbox suffice. [Transactional] would
// couple Application to the Infrastructure DbContext type.
[SkipTransaction]
public sealed record JoinMeetingCommand(Guid MeetingId, string Login, int GuestsCount)
    : ICommand<string>;
