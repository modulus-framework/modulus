using Meetup.Modules.Meetings.Domain.Entities;
using Meetup.Modules.Meetings.Domain.Repositories;

namespace Meetup.Modules.Meetings.Infrastructure.Repositories;

public sealed class MeetingCommentRepository(MeetingsDbContext db) : IMeetingCommentRepository
{
    public async Task AddAsync(MeetingComment entity, CancellationToken ct)
        => await db.Comments.AddAsync(entity, ct);
}
