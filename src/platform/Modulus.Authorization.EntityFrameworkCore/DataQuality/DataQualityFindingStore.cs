namespace Modulus.Authorization.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions.DataQuality;

/// <summary>Writes findings to <c>ModulusDataQualityFindings</c> and removes those past the retention period.</summary>
public sealed class DataQualityFindingStore(
    IDbContextFactory<AuthorizationStoreDbContextBase> factory,
    IOptions<DataQualityFindingOptions> options,
    TimeProvider clock)
{
    /// <summary>Stores a batch of findings in one transaction.</summary>
    public async Task AddAsync(IReadOnlyList<DataQualityFinding> findings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (findings.Count == 0)
            return;

        var now = clock.GetUtcNow().UtcDateTime;
        await using var db = await factory.CreateDbContextAsync(ct);
        foreach (var finding in findings)
        {
            db.DataQualityFindings.Add(new DataQualityFindingRow
            {
                Id = GuidV7.Create(),
                TenantId = finding.TenantId ?? Guid.Empty,
                CreatedAt = now,
                ResourceType = finding.ResourceType,
                RecordId = finding.RecordId,
                Action = finding.Action,
                MissingAttributes = $",{string.Join(',', finding.MissingAttributes)},",
            });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Reads findings, newest first. A <c>TenantId</c> of <see cref="Guid.Empty"/> selects the findings made with no
    /// company in scope; <c>null</c> selects every company. Each filter is optional and exact.
    /// </summary>
    public async Task<IReadOnlyList<DataQualityFindingRecord>> ListAsync(DataQualityFindingQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var take = Math.Clamp(query.Take, 1, DataQualityFindingQuery.MaxTake);
        var skip = Math.Max(query.Skip, 0);

        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = db.DataQualityFindings.AsNoTracking().AsQueryable();
        if (query.TenantId is { } tenantId)
            rows = rows.Where(f => f.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(query.ResourceType))
            rows = rows.Where(f => f.ResourceType == query.ResourceType);
        if (!string.IsNullOrWhiteSpace(query.Attribute))
        {
            var needle = $",{query.Attribute},";
            rows = rows.Where(f => f.MissingAttributes.Contains(needle));
        }

        var found = await rows
            .OrderByDescending(f => f.CreatedAt)
            .ThenBy(f => f.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return found.Select(f => new DataQualityFindingRecord(
            f.Id,
            f.TenantId,
            DateTime.SpecifyKind(f.CreatedAt, DateTimeKind.Utc),
            f.ResourceType,
            f.RecordId,
            f.Action,
            f.MissingAttributes.Split(',', StringSplitOptions.RemoveEmptyEntries))).ToList();
    }

    /// <summary>Removes the findings older than the retention period. Returns how many rows were removed.</summary>
    public async Task<int> PurgeAsync(CancellationToken ct = default)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - options.Value.Retention;
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.DataQualityFindings
            .Where(f => f.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
