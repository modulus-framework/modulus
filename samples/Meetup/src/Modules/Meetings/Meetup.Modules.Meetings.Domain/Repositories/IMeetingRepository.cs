using Meetup.Modules.Meetings.Domain.Entities;

namespace Meetup.Modules.Meetings.Domain.Repositories;

public interface IMeetingRepository
{
    Task<Meeting?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Meeting>> FindByGroupAsync(Guid groupId, CancellationToken ct);
    Task AddAsync(Meeting entity, CancellationToken ct);
}
