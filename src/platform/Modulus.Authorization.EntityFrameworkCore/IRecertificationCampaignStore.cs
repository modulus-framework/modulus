namespace Modulus.Authorization.EntityFrameworkCore;

using Modulus.Authorization.Governance;

/// <summary>
/// Persistent store for recertification campaigns.
/// </summary>
public interface IRecertificationCampaignStore
{
    /// <summary>Gets an active campaign by id, or null if not found or completed.</summary>
    Task<RecertificationCampaign?> GetAsync(Guid campaignId, CancellationToken ct);

    /// <summary>Lists all non-completed campaigns.</summary>
    Task<List<(Guid Id, string Name, int PendingCount, int TotalCount)>> ListActiveAsync(CancellationToken ct);

    /// <summary>Creates a new campaign with the given items.</summary>
    Task<Guid> CreateAsync(string name, List<RecertificationItem> items, Guid createdBy, CancellationToken ct);

    /// <summary>Records a review decision on one item.</summary>
    Task UpdateDecisionAsync(Guid campaignId, Guid userId, string permission,
        RecertificationDecision decision, Guid reviewedBy, CancellationToken ct);

    /// <summary>Marks a campaign as complete.</summary>
    Task CompleteAsync(Guid campaignId, CancellationToken ct);
}
