using Meetup.Modules.Meetings.Domain.Entities;
using Meetup.Modules.Meetings.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Meetup.Modules.Meetings.Infrastructure.Repositories;

public sealed class MeetingRepository(MeetingsDbContext db) : IMeetingRepository
{
    public async Task<Meeting?> GetByIdAsync(Guid id, CancellationToken ct)
        => await db.Meetings.FindAsync([id], ct);

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct)
        => await db.Meetings.AnyAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Meeting>> FindByGroupAsync(Guid groupId, CancellationToken ct)
        => await db.Meetings.Where(x => x.GroupId == groupId).OrderBy(x => x.StartUtc).ToListAsync(ct);

    public async Task AddAsync(Meeting entity, CancellationToken ct)
        => await db.Meetings.AddAsync(entity, ct);
}
