using Meetup.Modules.Meetings.Domain.Entities;

namespace Meetup.Modules.Meetings.Domain.Repositories;

public interface IMeetingGroupMemberRepository
{
    Task<bool> IsOrganizerAsync(Guid groupId, string login, CancellationToken ct);
    Task AddAsync(MeetingGroupMember entity, CancellationToken ct);
}
