using Meetup.Modules.Meetings.Domain.Entities;

namespace Meetup.Modules.Meetings.Domain.Repositories;

/// <summary>Persistence abstractions (implemented in Infrastructure with EF Core).</summary>
public interface IMeetingGroupRepository
{
    Task<MeetingGroup?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<bool> ExistsAsync(string name, string creatorLogin, CancellationToken ct);
    Task<IReadOnlyList<MeetingGroup>> GetAllAsync(CancellationToken ct);
    Task<IReadOnlyList<MeetingGroup>> FindByCreatorAsync(string creatorLogin, CancellationToken ct);
    Task AddAsync(MeetingGroup entity, CancellationToken ct);
}
