namespace Meetup.Modules.Administration.Domain;

/// <summary>Persistence abstraction (implemented in Infrastructure with EF Core).</summary>
public interface IMeetingGroupProposalRepository
{
    Task<MeetingGroupProposal?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<MeetingGroupProposal>> GetAllAsync(CancellationToken ct);
    Task AddAsync(MeetingGroupProposal entity, CancellationToken ct);
}
