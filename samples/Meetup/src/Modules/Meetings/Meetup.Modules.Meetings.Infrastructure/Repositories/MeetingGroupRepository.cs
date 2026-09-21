using Meetup.Modules.Meetings.Domain.Entities;
using Meetup.Modules.Meetings.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Meetup.Modules.Meetings.Infrastructure.Repositories;

public sealed class MeetingGroupRepository(MeetingsDbContext db) : IMeetingGroupRepository
{
    public async Task<MeetingGroup?> GetByIdAsync(Guid id, CancellationToken ct)
        => await db.Groups.FindAsync([id], ct);

    public async Task<bool> ExistsAsync(string name, string creatorLogin, CancellationToken ct)
        => await db.Groups.AnyAsync(x => x.Name == name && x.CreatorLogin == creatorLogin, ct);

    public async Task<IReadOnlyList<MeetingGroup>> GetAllAsync(CancellationToken ct)
        => await db.Groups.OrderBy(x => x.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<MeetingGroup>> FindByCreatorAsync(string creatorLogin, CancellationToken ct)
        => await db.Groups.Where(x => x.CreatorLogin == creatorLogin).ToListAsync(ct);

    public async Task AddAsync(MeetingGroup entity, CancellationToken ct)
        => await db.Groups.AddAsync(entity, ct);
}
