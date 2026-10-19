using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.Workflow;

namespace Modulus.Authorization.EntityFrameworkCore;

internal static class ApprovalJson
{
    private static readonly JsonSerializerOptions s_options = new(JsonSerializerDefaults.Web);

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, s_options);

    public static T Read<T>(string json) where T : new() => JsonSerializer.Deserialize<T>(json, s_options) ?? new T();
}

/// <summary>Approval chains of the current company (tenant-filtered table <c>ModulusApprovalChains</c>).</summary>
public sealed class EfApprovalChainStore(IDbContextFactory<AuthorizationStoreDbContextBase> factory) : IApprovalChainStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalChain>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.ApprovalChains.AsNoTracking().ToListAsync(ct);
        return [.. rows.OrderBy(r => r.Name, StringComparer.Ordinal).Select(ToChain)];
    }

    /// <inheritdoc />
    public async Task<ApprovalChain?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.ApprovalChains.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        return row is null ? null : ToChain(row);
    }

    /// <inheritdoc />
    public async Task<ApprovalChain> SaveAsync(ApprovalChain chain, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chain);
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.ApprovalChains.SingleOrDefaultAsync(c => c.Id == chain.Id, ct);
        if (row is null)
        {
            row = new ApprovalChainRow { Id = chain.Id };
            db.ApprovalChains.Add(row);
        }

        row.Name = chain.Name.Trim();
        row.DocumentType = chain.DocumentType.Trim();
        row.MinAmount = chain.MinAmount;
        row.MaxAmount = chain.MaxAmount;
        row.Currency = string.IsNullOrWhiteSpace(chain.Currency) ? null : chain.Currency.Trim().ToUpperInvariant();
        row.IsActive = chain.IsActive;
        row.Steps = ApprovalJson.Write(chain.Steps);
        await db.SaveChangesAsync(ct);
        return ToChain(row);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.ApprovalChains.Where(c => c.Id == id).ExecuteDeleteAsync(ct) > 0;
    }

    private static ApprovalChain ToChain(ApprovalChainRow r)
        => new(r.Id, r.Name, r.DocumentType, r.MinAmount, r.MaxAmount, r.Currency, ApprovalJson.Read<List<ApprovalStep>>(r.Steps), r.IsActive);
}

/// <summary>Approval requests of the current company (tenant-filtered table <c>ModulusApprovalRequests</c>; <c>Version</c> is a concurrency token).</summary>
public sealed class EfApprovalRequestStore(IDbContextFactory<AuthorizationStoreDbContextBase> factory) : IApprovalRequestStore
{
    /// <inheritdoc />
    public async Task AddAsync(ApprovalRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = new ApprovalRequestRow { Id = request.Id, TenantId = request.TenantId ?? default };
        Fill(row, request);
        db.ApprovalRequests.Add(row);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<ApprovalRequest?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.ApprovalRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : ToRequest(row);
    }

    /// <inheritdoc />
    public async Task<bool> TryUpdateAsync(ApprovalRequest updated, long expectedVersion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(updated);
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.ApprovalRequests.SingleOrDefaultAsync(r => r.Id == updated.Id && r.Version == expectedVersion, ct);
        if (row is null)
            return false;

        Fill(row, updated);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalRequest>> PendingAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.ApprovalRequests.AsNoTracking().Where(r => r.Status == (int)ApprovalStatus.Pending).ToListAsync(ct);
        return [.. rows.OrderBy(r => r.CreatedAt).Select(ToRequest)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalRequest>> RequestedByAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.ApprovalRequests.AsNoTracking().Where(r => r.RequestedBy == userId).ToListAsync(ct);
        return [.. rows.OrderByDescending(r => r.CreatedAt).Select(ToRequest)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalRequest>> ForDocumentAsync(string documentType, string documentId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var type = documentType.Trim();
        var rows = await db.ApprovalRequests.AsNoTracking().Where(r => r.DocumentType == type && r.DocumentId == documentId).ToListAsync(ct);
        return [.. rows.OrderByDescending(r => r.CreatedAt).Select(ToRequest)];
    }

    private static void Fill(ApprovalRequestRow row, ApprovalRequest r)
    {
        row.ChainId = r.ChainId;
        row.DocumentType = r.DocumentType;
        row.DocumentId = r.DocumentId;
        row.Amount = r.Amount;
        row.Currency = r.Currency;
        row.OrgUnitId = r.OrgUnitId;
        row.RequestedBy = r.RequestedBy;
        row.Summary = r.Summary;
        row.Steps = ApprovalJson.Write(r.Steps);
        row.CurrentStep = r.CurrentStep;
        row.Status = (int)r.Status;
        row.Decisions = ApprovalJson.Write(r.Decisions);
        row.CreatedAt = r.CreatedAt;
        row.StepStartedAt = r.StepStartedAt;
        row.Escalated = r.Escalated;
        row.Version = r.Version;
        row.CompletedAt = r.CompletedAt;
    }

    private static ApprovalRequest ToRequest(ApprovalRequestRow r)
        => new(r.Id, r.TenantId == default ? null : r.TenantId, r.ChainId, r.DocumentType, r.DocumentId, r.Amount, r.Currency, r.OrgUnitId,
            r.RequestedBy, r.Summary, ApprovalJson.Read<List<ApprovalStep>>(r.Steps), r.CurrentStep, (ApprovalStatus)r.Status,
            ApprovalJson.Read<List<ApprovalDecision>>(r.Decisions), r.CreatedAt, r.StepStartedAt, r.Escalated, r.Version, r.CompletedAt);
}
