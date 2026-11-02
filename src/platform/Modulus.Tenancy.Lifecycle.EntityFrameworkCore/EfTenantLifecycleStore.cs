namespace Modulus.Tenancy.Lifecycle.EntityFrameworkCore;

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// The lifecycle store in a <typeparamref name="TContext"/> (the host database). Operations are read and written as whole rows.
/// Concurrent writers to one operation are prevented by the engine's distributed lock (when registered); run one node without it.
/// </summary>
public sealed class EfTenantLifecycleStore<TContext>(TContext db) : ITenantLifecycleStore
    where TContext : TenantLifecycleDbContext
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <inheritdoc />
    public async Task AddOperationAsync(LifecycleOperation operation, CancellationToken ct)
    {
        db.LifecycleOperations.Add(ToRow(operation));
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<LifecycleOperation?> FindOperationAsync(Guid id, CancellationToken ct)
    {
        var row = await db.LifecycleOperations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        return row is null ? null : ToModel(row);
    }

    /// <inheritdoc />
    public async Task SaveOperationAsync(LifecycleOperation operation, CancellationToken ct)
    {
        var row = await db.LifecycleOperations.FirstOrDefaultAsync(o => o.Id == operation.Id, ct)
            ?? throw new InvalidOperationException("The lifecycle operation was not stored.");
        CopyTo(operation, row);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LifecycleOperation>> ListOperationsAsync(Guid tenantId, CancellationToken ct)
    {
        var rows = await db.LifecycleOperations.AsNoTracking()
            .Where(o => o.TenantId == tenantId)
            .OrderByDescending(o => o.RequestedAt)
            .ToListAsync(ct);
        return rows.Select(ToModel).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LifecycleOperation>> ListActiveOperationsAsync(CancellationToken ct)
    {
        var active = new[] { (int)OperationState.Pending, (int)OperationState.Running, (int)OperationState.OnHold };
        var rows = await db.LifecycleOperations.AsNoTracking()
            .Where(o => active.Contains(o.State))
            .OrderBy(o => o.RequestedAt)
            .ToListAsync(ct);
        return rows.Select(ToModel).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LifecycleOperation>> ListExpiredExportsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.LifecycleOperations.AsNoTracking()
            .Where(o => o.ExportPath != null && o.ExportExpiresAt <= now)
            .ToListAsync(ct);
        return rows.Select(ToModel).ToList();
    }

    /// <inheritdoc />
    public async Task AddLegalHoldAsync(LegalHold hold, CancellationToken ct)
    {
        db.LegalHolds.Add(new LegalHoldRow
        {
            Id = hold.Id,
            TenantId = hold.TenantId,
            Reason = hold.Reason,
            PlacedBy = hold.PlacedBy,
            PlacedAt = hold.PlacedAt,
            ReleasedBy = hold.ReleasedBy,
            ReleasedAt = hold.ReleasedAt,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<LegalHold?> FindLegalHoldAsync(Guid id, CancellationToken ct)
    {
        var row = await db.LegalHolds.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id, ct);
        return row is null ? null : ToModel(row);
    }

    /// <inheritdoc />
    public async Task SaveLegalHoldAsync(LegalHold hold, CancellationToken ct)
    {
        var row = await db.LegalHolds.FirstOrDefaultAsync(h => h.Id == hold.Id, ct)
            ?? throw new InvalidOperationException("The legal hold was not stored.");
        row.ReleasedBy = hold.ReleasedBy;
        row.ReleasedAt = hold.ReleasedAt;
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LegalHold>> ListActiveLegalHoldsAsync(Guid tenantId, CancellationToken ct)
    {
        var rows = await db.LegalHolds.AsNoTracking()
            .Where(h => h.TenantId == tenantId && h.ReleasedAt == null)
            .ToListAsync(ct);
        return rows.Select(ToModel).ToList();
    }

    /// <inheritdoc />
    public async Task SavePendingDeletionAsync(PendingDeletion item, CancellationToken ct)
    {
        var row = await db.PendingDeletions.FirstOrDefaultAsync(p => p.Id == item.Id, ct);
        if (row is null)
        {
            row = new PendingDeletionRow { Id = item.Id };
            db.PendingDeletions.Add(row);
        }
        row.TenantId = item.TenantId;
        row.OperationId = item.OperationId;
        row.Module = item.Module;
        row.Dataset = item.Dataset;
        row.DeleteAfter = item.DeleteAfter;
        row.DoneAt = item.DoneAt;
        row.Rows = item.Rows;
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PendingDeletion>> ListDuePendingDeletionsAsync(DateTimeOffset now, int take, CancellationToken ct)
    {
        var rows = await db.PendingDeletions.AsNoTracking()
            .Where(p => p.DoneAt == null && p.DeleteAfter <= now)
            .OrderBy(p => p.DeleteAfter)
            .Take(take)
            .ToListAsync(ct);
        return rows.Select(ToModel).ToList();
    }

    private static OperationRow ToRow(LifecycleOperation operation)
    {
        var row = new OperationRow { Id = operation.Id };
        CopyTo(operation, row);
        return row;
    }

    private static void CopyTo(LifecycleOperation operation, OperationRow row)
    {
        row.TenantId = operation.TenantId;
        row.Kind = (int)operation.Kind;
        row.State = (int)operation.State;
        row.RequestedBy = operation.RequestedBy;
        row.RequestedAt = operation.RequestedAt;
        row.CompletedAt = operation.CompletedAt;
        row.ExportFirst = operation.ExportFirst;
        row.ExportPath = operation.ExportPath;
        row.ExportExpiresAt = operation.ExportExpiresAt;
        row.Error = operation.Error;
        row.StepsJson = JsonSerializer.Serialize(operation.Steps, Json);
        row.OutcomesJson = JsonSerializer.Serialize(operation.Outcomes, Json);
        row.CertificatePayload = operation.CertificatePayload;
        row.CertificateSignature = operation.CertificateSignature;
        row.CertificateKeyId = operation.CertificateKeyId;
    }

    private static LifecycleOperation ToModel(OperationRow row) => new()
    {
        Id = row.Id,
        TenantId = row.TenantId,
        Kind = (OperationKind)row.Kind,
        State = (OperationState)row.State,
        RequestedBy = row.RequestedBy,
        RequestedAt = row.RequestedAt,
        CompletedAt = row.CompletedAt,
        ExportFirst = row.ExportFirst,
        ExportPath = row.ExportPath,
        ExportExpiresAt = row.ExportExpiresAt,
        Error = row.Error,
        Steps = JsonSerializer.Deserialize<List<OperationStep>>(row.StepsJson, Json) ?? [],
        Outcomes = JsonSerializer.Deserialize<List<DatasetOutcome>>(row.OutcomesJson, Json) ?? [],
        CertificatePayload = row.CertificatePayload,
        CertificateSignature = row.CertificateSignature,
        CertificateKeyId = row.CertificateKeyId,
    };

    private static LegalHold ToModel(LegalHoldRow row) => new()
    {
        Id = row.Id,
        TenantId = row.TenantId,
        Reason = row.Reason,
        PlacedBy = row.PlacedBy,
        PlacedAt = row.PlacedAt,
        ReleasedBy = row.ReleasedBy,
        ReleasedAt = row.ReleasedAt,
    };

    private static PendingDeletion ToModel(PendingDeletionRow row) => new()
    {
        Id = row.Id,
        TenantId = row.TenantId,
        OperationId = row.OperationId,
        Module = row.Module,
        Dataset = row.Dataset,
        DeleteAfter = row.DeleteAfter,
        DoneAt = row.DoneAt,
        Rows = row.Rows,
    };
}
