using Microsoft.EntityFrameworkCore;
using Meetup.Modules.Administration.Domain;

namespace Meetup.Modules.Administration.Infrastructure;

public sealed class MeetingGroupProposalRepository(AdministrationDbContext db)
    : IMeetingGroupProposalRepository
{
    public async Task<MeetingGroupProposal?> GetByIdAsync(Guid id, CancellationToken ct)
        => await db.Proposals.FindAsync([id], ct);

    public async Task<IReadOnlyList<MeetingGroupProposal>> GetAllAsync(CancellationToken ct)
        => await db.Proposals.OrderByDescending(x => x.ProposedAt).ToListAsync(ct);

    public async Task AddAsync(MeetingGroupProposal entity, CancellationToken ct)
        => await db.Proposals.AddAsync(entity, ct);
}
