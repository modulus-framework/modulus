using Microsoft.EntityFrameworkCore;
using Modulus.Core.Abstractions;

namespace Modulus.EntityFrameworkCore.ChangeHistory;

/// <summary>Which field-level changes to read.</summary>
public sealed record EntityChangeQuery
{
    /// <summary>The entity's CLR type name, as recorded (<c>Product</c>).</summary>
    public required string EntityName { get; init; }

    /// <summary>One record's key as recorded (composite keys joined with <c>|</c>); null for every record.</summary>
    public string? EntityKey { get; init; }

    /// <summary>Only changes at or after this instant.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Only changes before this instant.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Only changes of this property.</summary>
    public string? PropertyName { get; init; }

    /// <summary>The most changes returned, newest first.</summary>
    public int Take { get; init; } = 100;
}

/// <summary>
/// Reads the field-level history <see cref="IEntityChangeHistoryWriter"/> records ("who changed this invoice's amount,
/// and from what"). History rows are not tenant-filtered by the model, so the reader filters them itself: inside a
/// company only that company's rows, in the host context every row.
/// </summary>
public interface IEntityChangeHistoryReader
{
    /// <summary>The changes matching <paramref name="query"/>, newest first, across every module database.</summary>
    Task<IReadOnlyList<EntityChange>> QueryAsync(EntityChangeQuery query, CancellationToken ct = default);
}

/// <summary>Reads every registered module context that maps <see cref="EntityChange"/>.</summary>
internal sealed class EntityChangeHistoryReader(IEnumerable<DbContext> contexts, ICurrentTenant currentTenant)
    : IEntityChangeHistoryReader
{
    public async Task<IReadOnlyList<EntityChange>> QueryAsync(EntityChangeQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var take = Math.Clamp(query.Take, 1, 1000);
        var tenant = currentTenant.IsHost ? (Guid?)null : currentTenant.TenantId ?? Guid.Empty;
        var from = query.From?.UtcDateTime;
        var to = query.To?.UtcDateTime;

        var results = new List<EntityChange>();
        foreach (var context in contexts.Distinct())
        {
            if (context.Model.FindEntityType(typeof(EntityChange)) is null)
                continue;

            var rows = context.Set<EntityChange>().AsNoTracking().Where(c => c.EntityName == query.EntityName);
            if (tenant is { } tenantId)
                rows = rows.Where(c => c.TenantId == tenantId);
            if (query.EntityKey is { } key)
                rows = rows.Where(c => c.EntityKey == key);
            if (query.PropertyName is { } property)
                rows = rows.Where(c => c.PropertyName == property);
            if (from is { } start)
                rows = rows.Where(c => c.ChangedAt >= start);
            if (to is { } end)
                rows = rows.Where(c => c.ChangedAt < end);

            results.AddRange(await rows.OrderByDescending(c => c.ChangedAt).Take(take).ToListAsync(ct));
        }

        return [.. results.OrderByDescending(c => c.ChangedAt).Take(take)];
    }
}
