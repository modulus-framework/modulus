namespace Modulus.Authorization.EntityFrameworkCore;

using Modulus.Authorization.Governance;

/// <summary>
/// EF Core entity for a recertification campaign.
/// </summary>
public sealed class RecertificationCampaignRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime? CompletedAt { get; set; }

    public List<RecertificationItemRow> Items { get; set; } = [];
}

/// <summary>
/// EF Core entity for one reviewable access line in a campaign.
/// </summary>
public sealed class RecertificationItemRow
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public Guid UserId { get; set; }
    public string Permission { get; set; } = "";
    public AccessSource Source { get; set; }
    public RecertificationDecision Decision { get; set; } = RecertificationDecision.Pending;
    public DateTime? ReviewedAt { get; set; }
    public Guid? ReviewedBy { get; set; }

    public RecertificationCampaignRow Campaign { get; set; } = null!;
}
