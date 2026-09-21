using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

namespace Meetup.Modules.Meetings.Application.Commands.CreateMeeting;

/// <summary>Creates a meeting inside a group (organizer only, paid group only).</summary>
// Single-commit write on one module context: EF's implicit SaveChanges
// transaction plus the transactional outbox suffice. [Transactional] would
// couple Application to the Infrastructure DbContext type.
[SkipTransaction]
public sealed record CreateMeetingCommand(
    Guid GroupId,
    string Title,
    string Description,
    DateTime StartUtc,
    DateTime EndUtc,
    int? AttendeesLimit,
    int GuestsLimit,
    decimal EventFee,
    string EventFeeCurrency,
    string CreatorLogin) : ICommand<Guid>;
