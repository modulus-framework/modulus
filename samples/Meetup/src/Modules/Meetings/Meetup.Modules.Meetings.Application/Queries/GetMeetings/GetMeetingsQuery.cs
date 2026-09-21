using Meetup.Modules.Meetings.Application.Dtos;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Meetings.Application.Queries.GetMeetings;

public sealed record GetMeetingsQuery(Guid GroupId) : IQuery<IReadOnlyList<MeetingDto>>;
