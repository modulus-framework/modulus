using Meetup.Modules.Meetings.Domain.Entities;
using Meetup.Modules.Meetings.Domain.Enums;
using Meetup.Modules.Meetings.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Meetup.Modules.Meetings.Infrastructure.Repositories;

public sealed class MeetingGroupMemberRepository(MeetingsDbContext db) : IMeetingGroupMemberRepository
{
    public async Task<bool> IsOrganizerAsync(Guid groupId, string login, CancellationToken ct)
        => await db.GroupMembers.AnyAsync(
            x => x.GroupId == groupId && x.Login == login && x.Role == GroupMemberRole.Organizer, ct);

    public async Task AddAsync(MeetingGroupMember entity, CancellationToken ct)
        => await db.GroupMembers.AddAsync(entity, ct);
}
