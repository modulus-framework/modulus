namespace Modulus.Authorization.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.Governance;

/// <summary>
/// EF Core implementation of the recertification campaign store. Registered as a
/// singleton over <see cref="IDbContextFactory{TContext}"/>; each operation opens
/// a short-lived context so the store stays thread-safe.
/// </summary>
public sealed class EfRecertificationCampaignStore(
    IDbContextFactory<AuthorizationStoreDbContext> factory)
    : IRecertificationCampaignStore
{
    public async Task<RecertificationCampaign?> GetAsync(Guid campaignId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = await db.RecertificationCampaigns
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == campaignId && c.CompletedAt == null, ct);

        if (entity is null) return null;

        return BuildCampaignFromEntity(entity);
    }

    public async Task<List<(Guid Id, string Name, int PendingCount, int TotalCount)>> ListActiveAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var results = await db.RecertificationCampaigns
            .Where(c => c.CompletedAt == null)
            .Select(c => new
            {
                c.Id,
                c.Name,
                PendingCount = c.Items.Count(i => i.Decision == RecertificationDecision.Pending),
                TotalCount = c.Items.Count,
            })
            .AsNoTracking()
            .ToListAsync(ct);

        return results.Select(x => (x.Id, x.Name, x.PendingCount, x.TotalCount)).ToList();
    }

    public async Task<Guid> CreateAsync(string name, List<RecertificationItem> items, Guid createdBy, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await using var db = await factory.CreateDbContextAsync(ct);
        var entity = new RecertificationCampaignRow
        {
            Id = id,
            Name = name,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
            Items = items.Select(i => new RecertificationItemRow
            {
                Id = Guid.NewGuid(),
                CampaignId = id,
                UserId = i.UserId,
                Permission = i.Permission,
                Source = i.Source,
                Decision = RecertificationDecision.Pending,
            }).ToList(),
        };

        db.RecertificationCampaigns.Add(entity);
        await db.SaveChangesAsync(ct);
        return id;
    }

    public async Task UpdateDecisionAsync(Guid campaignId, Guid userId, string permission,
        RecertificationDecision decision, Guid reviewedBy, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var items = await db.RecertificationItems
            .Where(i => i.CampaignId == campaignId
                     && i.UserId == userId
                     && i.Permission == permission)
            .ToListAsync(ct);

        foreach (var item in items)
        {
            item.Decision = decision;
            item.ReviewedAt = DateTime.UtcNow;
            item.ReviewedBy = reviewedBy;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task CompleteAsync(Guid campaignId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var campaign = await db.RecertificationCampaigns.FirstOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is not null)
        {
            campaign.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    private static RecertificationCampaign BuildCampaignFromEntity(RecertificationCampaignRow entity)
    {
        // Construct minimal EffectiveAccessReport snapshots from the stored items
        // (just enough to pass to RecertificationCampaign constructor)
        var userReports = entity.Items
            .GroupBy(i => i.UserId)
            .Select(g => new EffectiveAccessReport(
                UserId: g.Key,
                DirectPermissions: g.Where(i => i.Source == AccessSource.Direct)
                    .Select(i => i.Permission)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                DelegatedPermissions: g.Where(i => i.Source == AccessSource.Delegated)
                    .Select(i => i.Permission)
                    .GroupBy(p => p)
                    .Select(pg => new DelegatedPermission(pg.Key, g.Key, Guid.Empty))
                    .ToList(),
                AllPermissions: g.Select(i => i.Permission)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                SodViolations: []))
            .ToList();

        var campaign = new RecertificationCampaign(entity.Name, userReports);

        // Restore decisions by calling public Certify/Revoke methods
        foreach (var item in entity.Items)
        {
            if (item.Decision == RecertificationDecision.Certified)
                campaign.Certify(item.UserId, item.Permission);
            else if (item.Decision == RecertificationDecision.Revoked)
                campaign.Revoke(item.UserId, item.Permission);
        }

        return campaign;
    }
}
