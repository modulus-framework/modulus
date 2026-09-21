using Meetup.Modules.Meetings.Application.Dtos;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Meetings.Application.Queries.GetMeetingGroups;

public sealed record GetMeetingGroupsQuery : IQuery<IReadOnlyList<MeetingGroupDto>>;
