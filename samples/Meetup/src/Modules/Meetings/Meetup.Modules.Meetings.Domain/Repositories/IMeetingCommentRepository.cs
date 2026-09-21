using Meetup.Modules.Meetings.Domain.Entities;

namespace Meetup.Modules.Meetings.Domain.Repositories;

public interface IMeetingCommentRepository
{
    Task AddAsync(MeetingComment entity, CancellationToken ct);
}
