using Meetup.Modules.Meetings.Application.Dtos;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Meetings.Application.Queries.GetAttendees;

public sealed record GetAttendeesQuery(Guid MeetingId) : IQuery<IReadOnlyList<MeetingAttendeeDto>>;
